package com.tyhp.lang.status

import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.wm.StatusBar
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.openapi.wm.StatusBarWidgetFactory
import com.tyhp.lang.TyhpPlugin

const val TYHP_STATUS_BAR_WIDGET_ID = "TyhpStatusBar"

class TyhpStatusBarWidgetFactory : StatusBarWidgetFactory {
    override fun getId(): String = TYHP_STATUS_BAR_WIDGET_ID

    override fun getDisplayName(): String = TyhpPlugin.PLUGIN_NAME

    override fun isAvailable(project: Project): Boolean = !project.isDisposed && !project.isDefault

    override fun createWidget(project: Project): StatusBarWidget = TyhpStatusBarWidget(project)

    override fun disposeWidget(widget: StatusBarWidget) {
        Disposer.dispose(widget)
    }

    override fun canBeEnabledOn(statusBar: StatusBar): Boolean = true

    override fun isEnabledByDefault(): Boolean = true
}
