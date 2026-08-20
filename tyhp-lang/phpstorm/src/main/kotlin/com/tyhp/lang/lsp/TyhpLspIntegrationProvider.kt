package com.tyhp.lang.lsp

import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspClient
import com.intellij.platform.lsp.api.LspIntegrationProvider
import com.intellij.platform.lsp.api.lsWidget.LspClientWidgetItem
import com.tyhp.lang.binary.resolveTyhpBinary
import com.tyhp.lang.icons.TyhpIcons
import com.tyhp.lang.settings.TyhpConfigurable
import com.tyhp.lang.workspace.WorkspaceService

/**
 * 2026.2 LSP entry point. One client per owned `tyhp.json`, started lazily
 * when a file that project includes is opened. Files with no owner stay
 * TextMate-only.
 */
class TyhpLspIntegrationProvider : LspIntegrationProvider {
    override fun fileOpened(
        project: Project,
        file: VirtualFile,
        clientStarter: LspIntegrationProvider.LspClientStarter,
    ) {
        if (!isTyhpLanguageFile(file)) {
            return
        }

        val lifecycle = TyhpLspLifecycle.getInstance(project)
        val resolved = resolveTyhpBinary(project)
        if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
            lifecycle.onMissingBinary(resolved.message)
            return
        }

        val owner = WorkspaceService.getInstance(project).ownerOf(file) ?: return
        lifecycle.onStarting(resolved.executablePath, owner.projectFilePath)
        clientStarter.ensureClientStarted(TyhpLspClientDescriptor(project, owner.projectFilePath))
    }

    override fun createWidgetItem(lspClient: LspClient, currentFile: VirtualFile?): LspClientWidgetItem =
        LspClientWidgetItem(
            lspClient,
            currentFile,
            TyhpIcons.TyhpFile,
            TyhpConfigurable::class.java,
        )
}
