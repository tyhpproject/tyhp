package com.tyhp.lang.workspace

import com.tyhp.lang.lsp.ResolveProjectFileOptions
import com.tyhp.lang.lsp.parentDirectory
import com.tyhp.lang.lsp.resolveTyhpProjectFile

/**
 * Forced `tyhp.projectPath` snapshots. Workspace scanning / include matching
 * lives in [ProjectIndex].
 */

typealias WorkspaceSnapshot = ProjectIndexSnapshot

fun detectWorkspaceProject(options: ResolveProjectFileOptions): WorkspaceSnapshot =
    snapshotFromProjectFile(resolveTyhpProjectFile(options))

fun snapshotFromProjectFile(projectFilePath: String?): WorkspaceSnapshot {
    val file = projectFilePath?.trim()?.takeIf { it.isNotEmpty() } ?: return ProjectIndexSnapshot()
    val dir = parentDirectory(file) ?: posixDirname(file)
    val name = dir.substringAfterLast('/').substringAfterLast('\\').trim().takeIf { it.isNotEmpty() }
        ?: posixBasename(dir)
    return ProjectIndexSnapshot(
        projectFilePath = file,
        projectDir = dir,
        projectName = name,
    )
}

fun projectStatusLabel(snapshot: WorkspaceSnapshot): String {
    val name = snapshot.projectName?.trim().orEmpty()
    return name.ifEmpty { "not in a Tyhp project" }
}

/**
 * Content root that should receive `tyhp init` (and run-config cwd fallback).
 * Prefers the longest root that contains [filePath], else the first root.
 */
fun contentRootForPath(filePath: String?, contentRoots: List<String>): String? {
    val path = filePath?.trim().orEmpty()
    if (path.isNotEmpty()) {
        val match = contentRoots
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .filter { root ->
                path == root ||
                    path.startsWith("$root/") ||
                    path.startsWith("$root\\")
            }
            .maxByOrNull { it.length }
        if (match != null) {
            return match
        }
    }
    return contentRoots.firstOrNull()?.trim()?.takeIf { it.isNotEmpty() }
}
