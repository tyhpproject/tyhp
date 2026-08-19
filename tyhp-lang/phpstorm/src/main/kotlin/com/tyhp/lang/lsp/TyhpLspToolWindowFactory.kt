package com.tyhp.lang.lsp

import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory

class TyhpLspToolWindowFactory : ToolWindowFactory, DumbAware {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val lifecycle = TyhpLspLifecycle.getInstance(project)
        val content = ContentFactory.getInstance().createContent(lifecycle.logComponent, "", false)
        content.isCloseable = false
        toolWindow.contentManager.addContent(content)
    }
}
