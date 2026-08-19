package com.tyhp.lang.debug

import com.intellij.openapi.project.DumbAware
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory

class XdebugProxyToolWindowFactory : ToolWindowFactory, DumbAware {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val manager = XdebugProxyManager.getInstance(project)
        val content = ContentFactory.getInstance().createContent(manager.logComponent, "", false)
        content.isCloseable = false
        toolWindow.contentManager.addContent(content)
    }
}
