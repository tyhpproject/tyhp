package com.tyhp.lang.debug

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.project.DumbAwareAction

class StartXdebugProxyAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        if (project.isDisposed || project.isDefault) {
            return
        }
        XdebugProxyManager.getInstance(project).startAsync()
    }
}

class StopXdebugProxyAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        if (project.isDisposed || project.isDefault) {
            return
        }
        XdebugProxyManager.getInstance(project).stopAsync()
    }
}

class RestartXdebugProxyAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        if (project.isDisposed || project.isDefault) {
            return
        }
        XdebugProxyManager.getInstance(project).restartAsync()
    }
}

class CreatePhpRemoteDebugAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        val project = e.project ?: return
        if (project.isDisposed || project.isDefault) {
            return
        }
        XdebugProxyManager.getInstance(project).createPhpRemoteDebugConfiguration()
    }
}
