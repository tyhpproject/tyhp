package com.tyhp.lang.status

import com.intellij.ide.DataManager
import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.popup.JBPopupFactory
import com.intellij.openapi.wm.StatusBar
import com.intellij.openapi.wm.StatusBarWidget
import com.intellij.ui.awt.RelativePoint
import com.intellij.util.Consumer
import com.tyhp.lang.binary.BinaryManager
import com.tyhp.lang.binary.getResolvedTyhpBinary
import com.tyhp.lang.debug.ProxyRunState
import com.tyhp.lang.debug.XdebugProxyManager
import com.tyhp.lang.lsp.LspClientState
import com.tyhp.lang.lsp.TyhpLspStateHub
import com.tyhp.lang.workspace.WorkspaceService
import com.tyhp.lang.workspace.WorkspaceSnapshot
import com.tyhp.lang.workspace.projectStatusLabel
import java.awt.Component
import java.awt.event.MouseEvent

class TyhpStatusBarWidget(private val project: Project) : StatusBarWidget, StatusBarWidget.TextPresentation {
    private var statusBar: StatusBar? = null

    override fun ID(): String = TYHP_STATUS_BAR_WIDGET_ID

    override fun getPresentation(): StatusBarWidget.WidgetPresentation = this

    override fun getAlignment(): Float = Component.CENTER_ALIGNMENT

    override fun getText(): String = view().text

    override fun getTooltipText(): String = view().tooltip

    override fun getClickConsumer(): Consumer<MouseEvent> = Consumer { event ->
        showActionsPopup(event)
    }

    override fun install(statusBar: StatusBar) {
        this.statusBar = statusBar
        val workspace = WorkspaceService.getInstance(project)
        workspace.addSnapshotListener(this) { refresh() }
        BinaryManager.getInstance().addResolutionListener(this) { changedProject, _ ->
            if (changedProject == null || changedProject == project) {
                refresh()
            }
        }
        TyhpLspStateHub.getInstance(project).addListener(this) { refresh() }
        if (!project.isDisposed && !project.isDefault) {
            XdebugProxyManager.getInstance(project).addStateListener(this) { refresh() }
        }
        refresh()
    }

    override fun dispose() {
        statusBar = null
    }

    private fun refresh() {
        statusBar?.updateWidget(ID())
    }

    private fun view(): StatusBarView {
        val binary = getResolvedTyhpBinary()
        val lsp = if (project.isDisposed) LspClientState.STOPPED else TyhpLspStateHub.getInstance(project).currentState
        val snapshot = if (project.isDisposed) {
            WorkspaceSnapshot()
        } else {
            WorkspaceService.getInstance(project).snapshot
        }
        val proxy = if (project.isDisposed || project.isDefault) {
            null
        } else {
            XdebugProxyManager.getInstance(project)
        }
        val launch = proxy?.lastLaunch
        val ide = proxy?.listeningIdePort ?: launch?.idePort
        val xdebug = launch?.xdebugPort
        val proxyDetail = if (ide != null && xdebug != null) "IDE $ide / XDebug $xdebug" else null
        return formatStatusBar(
            StatusBarInput(
                projectLabel = projectStatusLabel(snapshot),
                lspState = lsp,
                binaryStatus = binary.status,
                binaryPath = binary.executablePath,
                binaryMessage = binary.message,
                proxyState = proxy?.currentState ?: ProxyRunState.STOPPED,
                proxyDetail = proxyDetail,
            ),
        )
    }

    private fun showActionsPopup(event: MouseEvent) {
        val hasProject = !WorkspaceService.getInstance(project).snapshot.projectFilePath.isNullOrBlank()
        val group = DefaultActionGroup()
        val actions = ActionManager.getInstance()
        addAction(group, actions.getAction("tyhp.restartLanguageServer"))
        addAction(group, actions.getAction("tyhp.installCli"))
        addAction(group, actions.getAction("tyhp.revealBinary"))
        if (!hasProject) {
            addAction(group, actions.getAction("tyhp.initProject"))
        }
        val proxyState = if (project.isDisposed || project.isDefault) {
            ProxyRunState.STOPPED
        } else {
            XdebugProxyManager.getInstance(project).currentState
        }
        val allowedProxyActions = proxyStatusActions(proxyState)
        if ("start" in allowedProxyActions) {
            addAction(group, actions.getAction("tyhp.startXdebugProxy"))
        }
        if ("stop" in allowedProxyActions) {
            addAction(group, actions.getAction("tyhp.stopXdebugProxy"))
        }
        if ("restart" in allowedProxyActions) {
            addAction(group, actions.getAction("tyhp.restartXdebugProxy"))
        }
        addAction(group, actions.getAction("tyhp.createPhpRemoteDebug"))
        val context = DataManager.getInstance().getDataContext(event.component)
        JBPopupFactory.getInstance()
            .createActionGroupPopup(
                "Tyhp",
                group,
                context,
                JBPopupFactory.ActionSelectionAid.SPEEDSEARCH,
                true,
            )
            .show(RelativePoint(event))
    }

    private fun addAction(group: DefaultActionGroup, action: AnAction?) {
        if (action != null) {
            group.add(action)
        }
    }
}
