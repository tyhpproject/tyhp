package com.tyhp.lang.lsp

import com.intellij.openapi.project.Project
import com.intellij.openapi.startup.ProjectActivity

/**
 * Touch the project-level LSP lifecycle so binary-resolution and settings
 * listeners are registered before the first `.tyhp` file is opened.
 */
class TyhpLspStartupActivity : ProjectActivity {
    override suspend fun execute(project: Project) {
        if (project.isDisposed || project.isDefault) {
            return
        }
        TyhpLspLifecycle.getInstance(project)
    }
}
