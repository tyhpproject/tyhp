package com.tyhp.lang.binary

import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.Disposable
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.application.PathManager
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.options.ShowSettingsUtil
import com.intellij.openapi.progress.ProgressIndicator
import com.intellij.openapi.progress.ProgressManager
import com.intellij.openapi.progress.Task
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.util.Disposer
import com.tyhp.lang.settings.InstallMode
import com.tyhp.lang.settings.TyhpConfigurable
import com.tyhp.lang.settings.TyhpSettings
import com.tyhp.lang.settings.normalizeReleaseTag
import com.tyhp.lang.settings.tagsMatch
import java.nio.file.Path
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicBoolean

private const val STARTUP_UPDATE_DELAY_MS = 5_000L
private const val UPDATE_CHECK_INTERVAL_MS = 6L * 60L * 60L * 1000L
private const val NOTIFICATION_GROUP = "Tyhp Language"

@Service(Service.Level.APP)
class BinaryManager {
    private val log = thisLogger()
    private val started = AtomicBoolean(false)
    private val offeredFix = AtomicBoolean(false)
    private val autoUpdateScheduled = AtomicBoolean(false)

    @Volatile
    var lastResolution: TyhpBinaryResolution = inactiveResolution()
        private set

    private val resolutionListeners = CopyOnWriteArrayList<BinaryResolutionListener>()

    /**
     * Subscribe to CLI resolution changes (PATH probe, settings, install).
     * Later phases (LSP) restart processes from this instead of reading `tyhp.path`.
     */
    fun addResolutionListener(parentDisposable: Disposable, listener: BinaryResolutionListener) {
        resolutionListeners.add(listener)
        Disposer.register(parentDisposable) { resolutionListeners.remove(listener) }
    }

    private fun publishResolution(project: Project?, resolution: TyhpBinaryResolution): TyhpBinaryResolution {
        val previous = lastResolution
        lastResolution = resolution
        val unchanged =
            previous.status == resolution.status &&
                previous.executablePath == resolution.executablePath &&
                previous.message == resolution.message
        if (unchanged) {
            return resolution
        }
        for (listener in resolutionListeners) {
            try {
                listener.onResolutionChanged(project, resolution)
            } catch (err: Throwable) {
                log.warn("Binary resolution listener failed: ${errorMessage(err)}", err)
            }
        }
        return resolution
    }

    private val pluginStoragePath: String
        get() = Path.of(PathManager.getConfigPath(), "tyhp-lang").toString()

    private val cliDir: Path
        get() = pluginInstallDir(pluginStoragePath)

    fun onProjectOpened(project: Project) {
        try {
            // Probe on every project open: this service is app-scoped, but each
            // project can have its own unset `tyhp.path` project override, so a
            // single app-wide latch here would leave later-opened projects (in a
            // multi-window PhpStorm session) unprobed. probeAndPopulatePath is a
            // no-op once a path is set, so re-running it per project is cheap.
            probeAndPopulatePath(project)
            if (started.compareAndSet(false, true)) {
                scheduleAutoUpdate()
            }
            refreshResolution(project)
        } catch (err: Throwable) {
            val message = errorMessage(err)
            log.warn("Tyhp binary activation error: $message", err)
            publishResolution(project, missingResolution(message))
            notifyError(project, message)
        }
    }

    /**
     * Re-run PATH discovery when `tyhp.path` is empty, then validate. Never throws.
     */
    fun refresh(project: Project?): TyhpBinaryResolution {
        try {
            probeAndPopulatePath(project)
        } catch (err: Throwable) {
            val message = errorMessage(err)
            log.warn("PATH probe error: $message", err)
        }
        return refreshResolution(project)
    }

    fun resolve(project: Project?): TyhpBinaryResolution = refreshResolution(project)

    fun installInteractive(project: Project?) {
        val choice = Messages.showDialog(
            project,
            "Choose where to install the Tyhp compiler CLI.\n\n" +
                "Global installs into ~/.local/bin/tyhp (Unix) or %LOCALAPPDATA%\\Programs\\tyhp\\tyhp.exe (Windows) " +
                "and is never auto-updated.\n\n" +
                "Plugin only installs under this plugin’s config storage. Auto-update and tyhp.binary.pinnedVersion apply.",
            "Tyhp: Install / Update CLI",
            arrayOf("Global", "Plugin only", "Cancel"),
            0,
            Messages.getQuestionIcon(),
        )
        val mode = when (choice) {
            0 -> "global"
            1 -> "extension"
            else -> return
        }
        runInstall(project, mode)
    }

    fun reveal(project: Project?) {
        val resolved = lastResolution
        if (resolved.status == BinaryStatus.OK && !resolved.executablePath.isNullOrBlank()) {
            Messages.showInfoMessage(project, "Tyhp CLI: ${resolved.executablePath}", "Tyhp CLI Path")
            return
        }
        val message = resolved.message ?: "Tyhp CLI is not available."
        val pick = Messages.showDialog(
            project,
            message,
            "Tyhp CLI",
            arrayOf("Install / Update CLI", "Open Settings", "Cancel"),
            0,
            Messages.getErrorIcon(),
        )
        when (pick) {
            0 -> installInteractive(project)
            1 -> openSettings(project)
        }
    }

    private fun runInstall(project: Project?, mode: String) {
        ProgressManager.getInstance().run(object : Task.Backgroundable(project, "Installing Tyhp CLI", false) {
            override fun run(indicator: ProgressIndicator) {
                indicator.isIndeterminate = true
                try {
                    val installer = Installer(pluginStoragePath, InstallerLogger { line -> log.info(line) })
                    val pin = TyhpSettings.getPinnedVersion()
                    val tag = if (pin.isNotEmpty()) {
                        normalizeReleaseTag(pin)
                    } else {
                        fetchLatestRelease().tagName
                    }
                    log.info("Install / Update ($mode) → $tag")
                    val result = installer.install(mode, tag)
                    ApplicationManager.getApplication().invokeLater {
                        TyhpSettings.setTyhpPath(project, result.executablePath)
                        TyhpSettings.setInstallMode(if (mode == "extension") InstallMode.EXTENSION else InstallMode.GLOBAL)
                        if (mode == "global") {
                            deleteInstallMetadata(cliDir)
                        }
                        refreshResolution(project)
                        val pathNote = if (mode == "global") {
                            " If `tyhp` is not on PATH, add the install directory or keep using `tyhp.path`."
                        } else {
                            ""
                        }
                        notifyInfo(
                            project,
                            "Installed Tyhp CLI ${result.version} at ${result.executablePath}.$pathNote",
                        )
                    }
                } catch (err: Throwable) {
                    val message = errorMessage(err)
                    log.warn("Install failed: $message", err)
                    ApplicationManager.getApplication().invokeLater {
                        publishResolution(project, missingResolution(message, TyhpSettings.getInstallMode()))
                        notifyError(project, "Tyhp CLI install failed: $message")
                    }
                }
            }
        })
    }

    private fun probeAndPopulatePath(project: Project?) {
        if (!TyhpSettings.tyhpPathIsUnset(project)) {
            return
        }
        val found = probeTyhpOnPath() ?: return
        var absolute = found
        try {
            absolute = Path.of(found).toRealPath().toString()
        } catch (_: Exception) {
            absolute = found
        }
        log.info("PATH probe found tyhp at $absolute; writing tyhp.path")
        TyhpSettings.setTyhpPath(project, absolute)
        TyhpSettings.setInstallMode(InstallMode.PATH)
        deleteInstallMetadata(cliDir)
    }

    /**
     * Never throws: [resolveTyhpBinary] is the single resolve API later phases call.
     */
    private fun refreshResolution(project: Project?): TyhpBinaryResolution {
        return try {
            doRefreshResolution(project)
        } catch (err: Throwable) {
            val message = errorMessage(err)
            log.warn("Resolution error: $message", err)
            offerFix(project, message)
            return publishResolution(project, missingResolution(message))
        }
    }

    private fun doRefreshResolution(project: Project?): TyhpBinaryResolution {
        val configured = TyhpSettings.getTyhpPath(project)
        val installMode = TyhpSettings.getInstallMode()

        if (configured.isNotEmpty()) {
            val check = validateTyhpPath(configured)
            if (check.ok && !check.absolutePath.isNullOrBlank()) {
                return publishResolution(
                    project,
                    TyhpBinaryResolution(
                        status = BinaryStatus.OK,
                        executablePath = check.absolutePath,
                        source = BinarySource.SETTING,
                        installMode = installMode,
                    ),
                )
            }
            val message = check.message
                ?: "Tyhp CLI at `$configured` is missing or is not a file. Use “Tyhp: Install / Update CLI” or fix `tyhp.path`."
            offerFix(project, message)
            return publishResolution(
                project,
                TyhpBinaryResolution(
                    status = BinaryStatus.INVALID,
                    executablePath = check.absolutePath,
                    message = message,
                    source = BinarySource.SETTING,
                    installMode = installMode,
                ),
            )
        }

        val probed = probeTyhpOnPath()
        if (probed != null) {
            return publishResolution(
                project,
                TyhpBinaryResolution(
                    status = BinaryStatus.OK,
                    executablePath = probed,
                    source = BinarySource.PATH,
                    installMode = installMode,
                ),
            )
        }

        val message =
            "Tyhp CLI was not found. Use “Tyhp: Install / Update CLI” or set `tyhp.path` to an existing binary."
        offerFix(project, message)
        return publishResolution(project, missingResolution(message, installMode))
    }

    private fun offerFix(project: Project?, message: String) {
        if (!offeredFix.compareAndSet(false, true)) {
            return
        }
        notifyError(project, message)
    }

    private fun scheduleAutoUpdate() {
        if (!autoUpdateScheduled.compareAndSet(false, true)) {
            return
        }
        val app = ApplicationManager.getApplication()
        app.executeOnPooledThread {
            try {
                Thread.sleep(STARTUP_UPDATE_DELAY_MS)
            } catch (_: InterruptedException) {
                return@executeOnPooledThread
            }
            if (app.isDisposed) {
                return@executeOnPooledThread
            }
            runAutoUpdate()
        }
    }

    private fun runAutoUpdate() {
        val installMode = TyhpSettings.getInstallMode()
        val configuredPath = TyhpSettings.getTyhpPath(null)
        val platform = try {
            detectHostPlatform()
        } catch (err: UnsupportedPlatformError) {
            log.info(err.message)
            return
        }

        if (shouldSkipAutoUpdateDueToPathDrift(installMode, configuredPath, pluginStoragePath, platform)) {
            log.info("Skipping auto-update: tyhp.path no longer points at the plugin-managed install.")
            return
        }

        val metadata = readInstallMetadata(cliDir)
        val pin = TyhpSettings.getPinnedVersion()
        val pinNeedsApply =
            pin.isNotEmpty() &&
                installMode == InstallMode.EXTENSION &&
                isPluginOwnedInstall(metadata) &&
                !tagsMatch(metadata?.version ?: "", pin)

        if (!pinNeedsApply) {
            val last = TyhpSettings.getLastUpdateCheckEpochMs()
            if (System.currentTimeMillis() - last < UPDATE_CHECK_INTERVAL_MS) {
                log.info("Skipping auto-update check (debounced).")
                return
            }
        }

        try {
            val installer = Installer(pluginStoragePath, InstallerLogger { line -> log.info(line) }, platform)
            val result = UpdateService(installer).checkAndApply(
                UpdateServiceSettings(
                    installMode = installMode,
                    autoUpdate = TyhpSettings.getAutoUpdate(),
                    pinnedVersion = pin,
                ),
                metadata,
                InstallerLogger { line -> log.info(line) },
            )
            TyhpSettings.setLastUpdateCheckEpochMs(System.currentTimeMillis())
            if (result.updated && result.path != null) {
                TyhpSettings.setTyhpPath(null, result.path)
                refreshResolution(null)
                notifyInfo(null, "Updated Tyhp CLI (${result.reason}).")
            }
        } catch (err: Throwable) {
            val message = errorMessage(err)
            log.warn("Auto-update failed: $message", err)
        }
    }

    private fun notifyError(project: Project?, message: String) {
        val notification = NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp CLI", message, NotificationType.ERROR)
        notification.addAction(
            NotificationAction.createSimple("Install / Update CLI") {
                installInteractive(project)
            },
        )
        notification.addAction(
            NotificationAction.createSimple("Open Settings") {
                openSettings(project)
            },
        )
        notification.notify(project)
    }

    private fun notifyInfo(project: Project?, message: String) {
        NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp CLI", message, NotificationType.INFORMATION)
            .notify(project)
    }

    private fun openSettings(project: Project?) {
        if (project != null && !project.isDisposed) {
            ShowSettingsUtil.getInstance().showSettingsDialog(project, TyhpConfigurable::class.java)
        }
    }

    companion object {
        fun getInstance(): BinaryManager =
            ApplicationManager.getApplication().getService(BinaryManager::class.java)
    }
}

/**
 * Resolve the Tyhp CLI executable. Later phases (language server, run configs,
 * XDebug proxy) should call this instead of reading `tyhp.path` directly.
 */
fun resolveTyhpBinary(project: Project? = null): TyhpBinaryResolution {
    val app = ApplicationManager.getApplication() ?: return inactiveResolution()
    val manager = app.getService(BinaryManager::class.java) ?: return inactiveResolution()
    return manager.resolve(project)
}

/** Last cached resolution without hitting the filesystem again. */
fun getResolvedTyhpBinary(): TyhpBinaryResolution {
    val app = ApplicationManager.getApplication() ?: return inactiveResolution()
    val manager = app.getService(BinaryManager::class.java) ?: return inactiveResolution()
    return manager.lastResolution
}

internal fun errorMessage(err: Throwable): String = err.message ?: err.toString()
