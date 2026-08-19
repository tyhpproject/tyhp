package com.tyhp.lang.lsp

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.project.DumbAwareAction

class RestartLanguageServerAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        if (project.isDisposed || project.isDefault) {
            return
        }
        TyhpLspLifecycle.getInstance(project).restartNow("user action")
    }
}
