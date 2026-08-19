package com.tyhp.lang

import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.project.Project
import com.intellij.openapi.startup.ProjectActivity
import com.tyhp.lang.binary.BinaryManager
import com.tyhp.lang.debug.XdebugProxyManager
import com.tyhp.lang.workspace.InitPromptController
import com.tyhp.lang.workspace.WorkspaceService

/**
 * Load hook so the plugin is visible in a Gradle-launched PhpStorm sandbox.
 *
 * Startup: binary resolution (Phase 10), LSP client (Phase 11), workspace /
 * init / run configs / status bar (Phase 12), XDebug proxy (Phase 13).
 * TextMate grammars are loaded from `tyhp-lang/vscode/syntaxes/` (canonical
 * source; copied at Gradle build time).
 */
class TyhpPlugin : ProjectActivity {
    override suspend fun execute(project: Project) {
        thisLogger().info("$PLUGIN_NAME ($PLUGIN_ID) loaded for project ${project.name}")
        BinaryManager.getInstance().onProjectOpened(project)
        if (project.isDisposed || project.isDefault) {
            return
        }
        WorkspaceService.getInstance(project)
        InitPromptController.getInstance(project).considerOpenFiles()
        if (!project.isDisposed) {
            XdebugProxyManager.getInstance(project)
        }
    }

    companion object {
        const val PLUGIN_ID: String = "com.tyhp.lang"
        const val PLUGIN_NAME: String = "Tyhp Language"
    }
}
