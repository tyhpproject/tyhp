package com.tyhp.lang.lsp

import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.Disposable
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.options.ShowSettingsUtil
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.FileEditorManagerListener
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.platform.lsp.api.LspClientManager
import com.intellij.util.concurrency.AppExecutorUtil
import com.tyhp.lang.binary.BinaryManager
import com.tyhp.lang.binary.resolveTyhpBinary
import com.tyhp.lang.settings.TyhpConfigurable
import com.tyhp.lang.settings.TyhpSettings
import com.tyhp.lang.settings.TyhpSettingsListener
import com.tyhp.lang.workspace.TyhpProjectFileListener
import com.tyhp.lang.workspace.WorkspaceService
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

private const val NOTIFICATION_GROUP = "Tyhp Language"

/**
 * Restarts the language server when `tyhp.path` / project path change, recovers
 * from unexpected process exits with backoff, and owns the LSP log panel.
 *
 * Closing the project (or unloading the plugin) disposes this service and
 * stops `tyhp language_server`.
 */
class TyhpLspLifecycle(private val project: Project) : Disposable {
    private val log = thisLogger()
    private val backoff = RestartBackoff()
    private val logPanel = TyhpLspLogPanel()
    private val missingBinaryNotified = AtomicBoolean(false)
    private val restartScheduled = AtomicBoolean(false)
    private val stopping = AtomicBoolean(false)

    @Volatile
    private var lastStartedKey: String? = null

    @Volatile
    private var crashRestart: ScheduledFuture<*>? = null

    val logComponent get() = logPanel.component

    init {
        BinaryManager.getInstance().addResolutionListener(this) { changedProject, _ ->
            if (changedProject == null || changedProject == project) {
                onConfigChanged("binary resolution")
            }
        }
        project.messageBus.connect(this).subscribe(
            TyhpSettingsListener.TOPIC,
            TyhpSettingsListener { onConfigChanged("settings") },
        )
        project.messageBus.connect(this).subscribe(
            TyhpProjectFileListener.TOPIC,
            TyhpProjectFileListener { _, _, _ -> onConfigChanged("project file") },
        )
        project.messageBus.connect(this).subscribe(
            FileEditorManagerListener.FILE_EDITOR_MANAGER,
            object : FileEditorManagerListener {
                override fun fileClosed(source: FileEditorManager, file: VirtualFile) {
                    stopIfNoOwnedDocuments()
                }
            },
        )
        appendLog("Tyhp language server lifecycle ready for project ${project.name}")
    }

    fun appendLog(line: String) {
        logPanel.append(line)
        log.info(line)
    }

    fun onStarting(executablePath: String, projectFile: String? = resolveTyhpProjectFileFor(project)) {
        publishLspState(LspClientState.STARTING)
        lastStartedKey = currentLanguageServerKey(project, executablePath)
        val args = buildLanguageServerArgs(
            LanguageServerArgOptions(
                projectFilePath = projectFile,
                extraArgs = TyhpSettings.getLanguageServerArgs(),
            ),
        )
        val cwd = serverWorkingDirectory(projectFile, contentRootPaths(project))
        val label = projectFile?.let { java.io.File(it).parentFile?.name } ?: project.name
        appendLog(
            "[$label] Starting: $executablePath ${args.joinToString(" ")}${cwd?.let { " (cwd $it)" } ?: ""}",
        )
    }

    fun onMissingBinary(detail: String?) {
        publishLspState(LspClientState.ERROR)
        lastStartedKey = currentLanguageServerKey(project, "")
        val message = detail
            ?: "Tyhp CLI was not found. Use Tools → Tyhp → Install / Update CLI or set `tyhp.path`."
        appendLog("Cannot start language server: $message")
        if (!missingBinaryNotified.compareAndSet(false, true)) {
            return
        }
        val notification = NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp Language Server", message, NotificationType.ERROR)
        notification.addAction(
            NotificationAction.createSimple("Install / Update CLI") {
                BinaryManager.getInstance().installInteractive(project)
            },
        )
        notification.addAction(
            NotificationAction.createSimple("Refresh Tyhp binary") {
                BinaryManager.getInstance().refresh(project)
            },
        )
        notification.addAction(
            NotificationAction.createSimple("Open Settings") {
                if (!project.isDisposed) {
                    ShowSettingsUtil.getInstance().showSettingsDialog(project, TyhpConfigurable::class.java)
                }
            },
        )
        notification.notify(project)
    }

    fun onServerInitialized() {
        publishLspState(LspClientState.RUNNING)
        backoff.reset()
        restartScheduled.set(false)
        crashRestart?.cancel(false)
        crashRestart = null
        missingBinaryNotified.set(false)
        appendLog("Language server is running.")
    }

    fun onServerStopped(shutdownNormally: Boolean) {
        if (stopping.get() || project.isDisposed) {
            return
        }
        if (shutdownNormally) {
            if (lspHubState() != LspClientState.STARTING) {
                publishLspState(LspClientState.STOPPED)
            }
            appendLog("Language server stopped.")
            return
        }
        publishLspState(LspClientState.ERROR)
        appendLog("Language server process exited unexpectedly.")
        scheduleCrashRestart()
    }

    fun restartNow(reason: String) {
        if (project.isDisposed || project.isDefault) {
            return
        }
        cancelScheduledRestart()
        backoff.reset()
        doRestart(reason)
    }

    private fun onConfigChanged(reason: String) {
        if (stopping.get() || project.isDisposed || project.isDefault) {
            return
        }
        val resolved = resolveTyhpBinary(project)
        if (reason == "project file") {
            cancelScheduledRestart()
            backoff.reset()
            lastStartedKey = currentLanguageServerKey(project, resolved.executablePath)
            if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
                appendLog("Stopping language server ($reason): ${resolved.message}")
                stopClients()
                onMissingBinary(resolved.message)
                return
            }
            missingBinaryNotified.set(false)
            doRestart(reason)
            return
        }
        val key = currentLanguageServerKey(project, resolved.executablePath)
        if (key == lastStartedKey) {
            return
        }
        val previousKey = lastStartedKey
        lastStartedKey = key
        if (previousKey == null) {
            // First observation: fileOpened starts the client when a Tyhp file is open.
            return
        }
        cancelScheduledRestart()
        backoff.reset()
        if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
            appendLog("Stopping language server ($reason): ${resolved.message}")
            stopClients()
            onMissingBinary(resolved.message)
            return
        }
        missingBinaryNotified.set(false)
        doRestart(reason)
    }

    private fun scheduleCrashRestart() {
        if (stopping.get() || project.isDisposed) {
            return
        }
        if (!restartScheduled.compareAndSet(false, true)) {
            return
        }
        val delay = backoff.nextDelayMs()
        appendLog("Restarting language server in ${delay}ms (attempt ${backoff.consecutiveFailures}).")
        crashRestart = AppExecutorUtil.getAppScheduledExecutorService().schedule(
            {
                restartScheduled.set(false)
                if (!stopping.get() && !project.isDisposed) {
                    retryAfterCrash()
                }
            },
            delay,
            TimeUnit.MILLISECONDS,
        )
    }

    /**
     * Re-validates the binary before every crash-recovery attempt. Without this,
     * a binary that disappeared (uninstalled / bad update) mid-session would retry
     * the same doomed command line forever at the 30s backoff cap instead of ever
     * surfacing the actionable missing-binary notification.
     */
    private fun retryAfterCrash() {
        val resolved = resolveTyhpBinary(project)
        if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
            appendLog("Not retrying: ${resolved.message}")
            onMissingBinary(resolved.message)
            return
        }
        doRestart("crash recovery")
    }

    private fun cancelScheduledRestart() {
        restartScheduled.set(false)
        crashRestart?.cancel(false)
        crashRestart = null
    }

    private fun doRestart(reason: String) {
        if (project.isDisposed || project.isDefault) {
            return
        }
        publishLspState(LspClientState.STARTING)
        appendLog("Restarting language server ($reason).")
        try {
            LspClientManager.getInstance(project)
                .stopAndRestartClientsIfNeeded(TyhpLspIntegrationProvider::class.java)
        } catch (err: Throwable) {
            log.warn("Failed to restart language server: ${err.message}", err)
            appendLog("Failed to restart language server: ${err.message}")
        }
    }

    private fun stopIfNoOwnedDocuments() {
        if (stopping.get() || project.isDisposed || project.isDefault) {
            return
        }
        val openOwners = FileEditorManager.getInstance(project).openFiles
            .filter { isTyhpLanguageFile(it) }
            .mapNotNull { WorkspaceService.getInstance(project).ownerOf(it)?.projectFilePath }
            .toSet()
        if (openOwners.isEmpty()) {
            stopClients()
        }
    }

    private fun stopClients() {
        if (project.isDisposed || project.isDefault) {
            return
        }
        try {
            LspClientManager.getInstance(project).stopClients(TyhpLspIntegrationProvider::class.java)
        } catch (err: Throwable) {
            log.warn("Failed to stop language server: ${err.message}", err)
        }
    }

    override fun dispose() {
        stopping.set(true)
        cancelScheduledRestart()
        stopClients()
        publishLspState(LspClientState.STOPPED)
        appendLog("Language server lifecycle disposed; process stop requested.")
    }

    private fun publishLspState(state: LspClientState) {
        if (project.isDisposed) {
            return
        }
        TyhpLspStateHub.getInstance(project).publish(state)
    }

    private fun lspHubState(): LspClientState {
        if (project.isDisposed) {
            return LspClientState.STOPPED
        }
        return TyhpLspStateHub.getInstance(project).currentState
    }

    companion object {
        fun getInstance(project: Project): TyhpLspLifecycle =
            project.getService(TyhpLspLifecycle::class.java)
    }
}

internal fun invokeOnEdt(action: () -> Unit) {
    val app = ApplicationManager.getApplication()
    if (app == null || app.isDispatchThread) {
        action()
    } else {
        app.invokeLater(action)
    }
}
