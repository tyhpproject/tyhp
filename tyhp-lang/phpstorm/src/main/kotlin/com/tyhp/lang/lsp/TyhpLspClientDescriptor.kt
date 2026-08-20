package com.tyhp.lang.lsp

import com.intellij.execution.ExecutionException
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspClientDescriptor
import com.intellij.platform.lsp.api.LspServerListener
import com.intellij.platform.lsp.api.ProjectWideLspClientDescriptor
import com.intellij.platform.lsp.api.customization.LspCustomization
import com.intellij.platform.lsp.api.customization.LspDiagnosticsDisabled
import com.intellij.platform.lsp.api.customization.LspDiagnosticsSupport
import com.tyhp.lang.binary.resolveTyhpBinary
import com.tyhp.lang.settings.TyhpSettings
import com.tyhp.lang.workspace.WorkspaceService
import org.eclipse.lsp4j.InitializeParams
import org.eclipse.lsp4j.InitializeResult
import java.io.File

/**
 * Per-`tyhp.json` LSP client for `.tyhp` / `.tyhpdef`. Starts
 * `tyhp language_server --quiet --stdio --tyhp-project=<file>`.
 * [isSupportedFile] keeps each server on files it owns so two type worlds
 * are never merged.
 */
class TyhpLspClientDescriptor(
    project: Project,
    val tyhpProjectFile: String,
) : ProjectWideLspClientDescriptor(project, "Tyhp (${File(tyhpProjectFile).parent})") {
    override fun isSupportedFile(file: VirtualFile): Boolean {
        if (!isTyhpLanguageFile(file)) {
            return false
        }
        val owner = WorkspaceService.getInstance(project).ownerOf(file)
        return owner?.projectFilePath == tyhpProjectFile
    }

    override fun createCommandLine(): GeneralCommandLine {
        val resolved = resolveTyhpBinary(project)
        val exe = resolved.executablePath
        if (!resolved.isOk || exe.isNullOrBlank()) {
            throw ExecutionException(
                resolved.message
                    ?: "Tyhp CLI was not found. Use Tools → Tyhp → Install / Update CLI or set `tyhp.path`.",
            )
        }

        val args = buildLanguageServerArgs(
            LanguageServerArgOptions(
                projectFilePath = tyhpProjectFile,
                extraArgs = TyhpSettings.getLanguageServerArgs(),
            ),
        )
        val cwd = serverWorkingDirectory(tyhpProjectFile, contentRootPaths(project))
        LspClientDescriptor.LOG.info("$this: starting $exe ${args.joinToString(" ")}${cwd?.let { " (cwd $it)" } ?: ""}")

        val cmd = GeneralCommandLine()
        cmd.exePath = exe
        cmd.addParameters(args)
        cmd.charset = Charsets.UTF_8
        if (!cwd.isNullOrBlank()) {
            cmd.setWorkDirectory(File(cwd))
        }
        return cmd
    }

    override fun createInitializeParams(): InitializeParams {
        val params = super.createInitializeParams()
        params.trace = TyhpSettings.getLanguageServerTrace().ifEmpty { "off" }
        return params
    }

    override val lspCustomization: LspCustomization = object : LspCustomization() {
        override val diagnosticsCustomizer =
            if (TyhpSettings.getDiagnosticsEnable()) LspDiagnosticsSupport() else LspDiagnosticsDisabled
    }

    override val lspServerListener: LspServerListener = object : LspServerListener {
        override fun serverInitialized(params: InitializeResult) {
            TyhpLspLifecycle.getInstance(project).onServerInitialized()
        }

        override fun serverStopped(shutdownNormally: Boolean) {
            TyhpLspLifecycle.getInstance(project).onServerStopped(shutdownNormally)
        }
    }

    override fun equals(other: Any?): Boolean {
        if (this === other) {
            return true
        }
        if (other !is TyhpLspClientDescriptor) {
            return false
        }
        return project == other.project && tyhpProjectFile == other.tyhpProjectFile
    }

    override fun hashCode(): Int = 31 * project.hashCode() + tyhpProjectFile.hashCode()
}
