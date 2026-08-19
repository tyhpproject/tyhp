package com.tyhp.lang.actions

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.project.DumbAwareAction
import com.tyhp.lang.binary.BinaryManager

class RefreshBinaryAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        BinaryManager.getInstance().refresh(e.project)
    }
}

class InstallCliAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        BinaryManager.getInstance().installInteractive(e.project)
    }
}

class RevealBinaryAction : DumbAwareAction() {
    override fun actionPerformed(e: AnActionEvent) {
        BinaryManager.getInstance().reveal(e.project)
    }
}
