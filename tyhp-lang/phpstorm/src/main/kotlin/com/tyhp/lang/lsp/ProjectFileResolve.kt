package com.tyhp.lang.lsp

import com.intellij.openapi.project.Project
import com.intellij.openapi.roots.ProjectRootManager
import com.intellij.openapi.vfs.VirtualFile
import com.tyhp.lang.filetype.TyhpFileType
import com.tyhp.lang.filetype.TyhpdefFileType
import com.tyhp.lang.settings.TyhpSettings
import java.nio.file.Files
import java.nio.file.Path

fun isTyhpLanguageFile(file: VirtualFile): Boolean {
    val type = file.fileType
    return type == TyhpFileType || type == TyhpdefFileType
}

fun contentRootPaths(project: Project): List<String> {
    val roots = ProjectRootManager.getInstance(project).contentRoots
        .map { it.path }
        .filter { it.isNotBlank() }
    if (roots.isNotEmpty()) {
        return roots
    }
    val base = project.basePath
    return if (!base.isNullOrBlank()) listOf(base) else emptyList()
}

private val nioFs = object : ProjectFileFs {
    override fun exists(path: String): Boolean = Files.exists(Path.of(path))
    override fun isDirectory(path: String): Boolean = Files.isDirectory(Path.of(path))
}

fun resolveTyhpProjectFileFor(project: Project): String? {
    return resolveTyhpProjectFile(
        ResolveProjectFileOptions(
            configuredPath = TyhpSettings.getProjectPath(),
            contentRoots = contentRootPaths(project),
            join = { dir, name -> Path.of(dir, name).toString() },
            resolve = { root, rel -> Path.of(root).resolve(rel).normalize().toAbsolutePath().toString() },
            isAbsolute = { Path.of(it).isAbsolute },
            fs = nioFs,
        ),
    )
}

fun currentLanguageServerKey(project: Project, executablePath: String?): String {
    val projectFile = resolveTyhpProjectFileFor(project)
    val args = buildLanguageServerArgs(
        LanguageServerArgOptions(
            projectFilePath = projectFile,
            extraArgs = TyhpSettings.getLanguageServerArgs(),
        ),
    )
    val cwd = serverWorkingDirectory(projectFile, contentRootPaths(project))
    // `diagnosticsEnable` / `trace` are not argv flags — they are captured once into
    // TyhpLspClientDescriptor.lspCustomization / createInitializeParams() at client
    // construction. Folding them into the key ensures a Settings-panel Apply (which
    // always fires TyhpSettingsListener) actually restarts the client so the new
    // values take effect, instead of silently applying only on the next unrelated
    // restart (or IDE restart).
    val settingsFingerprint = listOf(
        TyhpSettings.getDiagnosticsEnable().toString(),
        TyhpSettings.getLanguageServerTrace(),
    )
    return languageServerKey(executablePath.orEmpty(), args, cwd, settingsFingerprint)
}
