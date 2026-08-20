package com.tyhp.lang.debug

import com.intellij.notification.NotificationAction
import com.intellij.notification.NotificationGroupManager
import com.intellij.notification.NotificationType
import com.intellij.openapi.Disposable
import com.intellij.openapi.components.Service
import com.intellij.openapi.diagnostic.thisLogger
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.vfs.VirtualFileManager
import com.intellij.openapi.vfs.newvfs.BulkFileListener
import com.intellij.openapi.vfs.newvfs.events.VFileEvent
import com.intellij.util.concurrency.AppExecutorUtil
import com.tyhp.lang.binary.BinaryManager
import com.tyhp.lang.binary.resolveTyhpBinary
import com.tyhp.lang.settings.TyhpSettings
import com.tyhp.lang.settings.TyhpSettingsListener
import com.tyhp.lang.workspace.WorkspaceService
import java.io.File
import java.nio.file.Files
import java.nio.file.Path
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import javax.swing.JComponent

private const val NOTIFICATION_GROUP = "Tyhp Language"
private const val RESTART_DEBOUNCE_MS = 400L

fun interface ProxyStateListener {
    fun onProxyStateChanged(state: ProxyRunState)
}

/**
 * Starts/stops `tyhp xdebug_proxy` using [com.tyhp.lang.binary.resolveTyhpBinary].
 * Does not reimplement DBGp — PhpStorm’s built-in XDebug is the client.
 */
@Service(Service.Level.PROJECT)
class XdebugProxyManager(private val project: Project) : Disposable {
    private val log = thisLogger()
    private val logPanel = XdebugProxyLogPanel()
    private val listeners = CopyOnWriteArrayList<ProxyStateListener>()
    private val disposed = AtomicBoolean(false)
    private val warnedNoMaps = AtomicBoolean(false)

    @Volatile
    private var bannerIdePort: Int? = null

    @Volatile
    var lastLaunch: ResolvedProxyLaunch? = null
        private set

    @Volatile
    var boundIdePort: Int? = null
        private set

    private var restartFuture: ScheduledFuture<*>? = null

    val logComponent: JComponent get() = logPanel.component

    val currentState: ProxyRunState
        get() = controller.currentState

    val isListening: Boolean
        get() = proxyIsListening(currentState)

    val listeningIdePort: Int?
        get() = if (isListening) boundIdePort ?: lastLaunch?.idePort else null

    private val controller = ProxyProcessController(
        object : ProxyProcessHooks {
            override fun spawn(command: String, args: List<String>, cwd: String?): SpawnedProxy {
                return spawnProxy(command, args, cwd)
            }

            override fun waitForListening(port: Int, child: SpawnedProxy, abort: AtomicBoolean): Int {
                return waitUntilListening(port, child, abort)
            }

            override fun stopProcess(child: SpawnedProxy) {
                stopSpawnedProxy(child)
            }

            override fun onLog(line: String) {
                appendLog(line)
            }

            override fun onState(state: ProxyRunState) {
                publishState(state)
            }

            override fun onUnexpectedExit(code: Int?) {
                val suffix = if (code != null) " (code $code)" else ""
                appendLog("XDebug proxy exited unexpectedly$suffix.")
                notifyWarn(
                    "Tyhp XDebug proxy stopped unexpectedly. Check the Tyhp XDebug Proxy tool window.",
                )
            }
        },
    )

    init {
        BinaryManager.getInstance().addResolutionListener(this) { changedProject, _ ->
            if (changedProject == null || changedProject == project) {
                scheduleRunningRestart()
            }
        }
        project.messageBus.connect(this).subscribe(
            TyhpSettingsListener.TOPIC,
            TyhpSettingsListener { scheduleRunningRestart() },
        )
        WorkspaceService.getInstance(project).addSnapshotListener(this) { scheduleRunningRestart() }
        project.messageBus.connect(this).subscribe(
            VirtualFileManager.VFS_CHANGES,
            object : BulkFileListener {
                override fun after(events: List<VFileEvent>) {
                    val projectFile = WorkspaceService.getInstance(project).snapshot.projectFilePath
                    if (events.any { event -> isTyhpJsonEvent(event, projectFile) }) {
                        scheduleRunningRestart()
                    }
                }
            },
        )
        appendLog("Tyhp XDebug proxy manager ready for project ${project.name}")
    }

    fun addStateListener(parentDisposable: Disposable, listener: ProxyStateListener) {
        listeners.add(listener)
        Disposer.register(parentDisposable) { listeners.remove(listener) }
    }

    fun resolveLaunch(): ResolvedProxyLaunch {
        val snapshot = WorkspaceService.getInstance(project).snapshot
        val json = readProjectSnapshot(snapshot.projectFilePath)
        val launch = resolveProxyLaunch(TyhpSettings.getExplicitProxySettings(), json)
        lastLaunch = launch
        return launch
    }

    fun start(): Boolean {
        if (disposed.get()) {
            return false
        }
        if (currentState == ProxyRunState.RUNNING || currentState == ProxyRunState.STARTING) {
            return currentState == ProxyRunState.RUNNING
        }

        val resolved = resolveTyhpBinary(project)
        if (!resolved.isOk || resolved.executablePath.isNullOrBlank()) {
            val detail = resolved.message
                ?: "Tyhp CLI was not found. Use Tools → Tyhp → Install / Update CLI or set `tyhp.path`."
            appendLog("Cannot start XDebug proxy: $detail")
            notifyMissingBinary(detail)
            return false
        }

        val snapshot = WorkspaceService.getInstance(project).snapshot
        val launch = resolveLaunch()
        val args = buildXdebugProxyArgsFromLaunch(launch, snapshot.projectFilePath)
        val cwd = snapshot.projectDir ?: project.basePath
        bannerIdePort = null
        warnedNoMaps.set(false)
        boundIdePort = if (launch.idePort > 0) launch.idePort else null

        appendLog("Starting: ${resolved.executablePath} ${args.joinToString(" ")}${if (cwd != null) " (cwd $cwd)" else ""}")
        warnPrerequisites(launch, snapshot.projectDir)

        return try {
            controller.start(
                ProxyStartRequest(
                    command = resolved.executablePath,
                    args = args,
                    cwd = cwd,
                    idePort = launch.idePort,
                ),
            ).get(PROXY_LISTEN_TIMEOUT_MS + 2_000L, TimeUnit.MILLISECONDS)
            boundIdePort = bannerIdePort?.takeIf { it > 0 } ?: launch.idePort
            appendLog("XDebug proxy is listening (IDE $boundIdePort, XDebug ${launch.xdebugPort}).")
            if (warnedNoMaps.get()) {
                notifyWarn(
                    sourcemapGuidance(
                        SourcemapGuidanceOptions(
                            generateSourcemap = launch.generateSourcemap,
                            mapCount = 0,
                            sourceMapDir = launch.sourceMapDir,
                            outputPath = launch.outputPath,
                        ),
                    ) ?: "No sourcemaps were loaded. Build with generateSourcemap enabled. $SOURCEMAP_DOCS_URL",
                )
            }
            true
        } catch (err: Throwable) {
            val message = unwrap(err)
            notifyError(proxyStartFailedGuidance(message))
            false
        }
    }

    fun stop() {
        try {
            controller.stop().get(PROXY_STOP_TIMEOUT_MS + 2_000L, TimeUnit.MILLISECONDS)
            boundIdePort = null
            appendLog("XDebug proxy stopped; listening ports released.")
        } catch (err: Throwable) {
            val message = unwrap(err)
            notifyError("Tyhp XDebug proxy did not stop cleanly: $message. Ports may still be in use.")
        }
    }

    fun restart(): Boolean {
        stop()
        return start()
    }

    fun startAsync() {
        AppExecutorUtil.getAppExecutorService().execute {
            if (!disposed.get() && !project.isDisposed) {
                start()
            }
        }
    }

    fun stopAsync() {
        AppExecutorUtil.getAppExecutorService().execute {
            if (!project.isDisposed) {
                stop()
            }
        }
    }

    fun restartAsync() {
        AppExecutorUtil.getAppExecutorService().execute {
            if (!disposed.get() && !project.isDisposed) {
                restart()
            }
        }
    }

    fun createPhpRemoteDebugConfiguration(): PhpRemoteDebugEnsureResult {
        val launch = resolveLaunch()
        val plan = phpRemoteDebugPlan(launch)
        var result: PhpRemoteDebugEnsureResult? = null
        runOnEdt {
            result = ensurePhpRemoteDebugConfiguration(project, plan)
        }
        val done = result ?: PhpRemoteDebugEnsureResult(
            created = false,
            alreadyExisted = false,
            typeFound = false,
            debugPortApplied = false,
            message = phpRemoteDebugMissingGuidance(),
        )
        appendLog(done.message)
        notifyInfo(done.message)
        // Misconfiguration guidance: the config now points at the proxy IDE port, but
        // debugging will not work until the proxy is actually listening there.
        if (!isListening) {
            val guidance = proxyDownGuidance(launch.idePort)
            appendLog(guidance)
            notifyWarn(guidance)
        }
        return done
    }

    override fun dispose() {
        disposed.set(true)
        restartFuture?.cancel(false)
        restartFuture = null
        try {
            controller.stop().get(PROXY_STOP_TIMEOUT_MS + 1_000L, TimeUnit.MILLISECONDS)
        } catch (_: Throwable) {
            // Project is closing; do not kill/killall.
        }
        controller.shutdownExecutor()
        listeners.clear()
    }

    private fun spawnProxy(command: String, args: List<String>, cwd: String?): SpawnedProxy {
        val pb = ProcessBuilder(listOf(command) + args)
        pb.redirectErrorStream(true)
        if (!cwd.isNullOrBlank()) {
            pb.directory(File(cwd))
        }
        val process = pb.start()
        return JvmSpawnedProxy(process) { line ->
            appendLog(line)
            val bound = parseBoundIdePort(line)
            if (bound != null) {
                bannerIdePort = bound
            }
            if (lineWarnsNoSourcemaps(line)) {
                warnedNoMaps.set(true)
            }
        }
    }

    private fun waitUntilListening(port: Int, child: SpawnedProxy, abort: AtomicBoolean): Int {
        val deadline = System.currentTimeMillis() + PROXY_LISTEN_TIMEOUT_MS
        while (!abort.get() && System.currentTimeMillis() < deadline) {
            if (child.exitCode != null) {
                throw IllegalStateException("XDebug proxy process exited before the IDE port was listening")
            }
            val candidate = bannerIdePort?.takeIf { it > 0 } ?: port
            if (candidate > 0 && probeTcpPort(PROXY_LISTEN_ADDRESS, candidate, 250)) {
                return candidate
            }
            sleepMs(100)
        }
        if (abort.get()) {
            throw IllegalStateException("XDebug proxy start was cancelled")
        }
        throw IllegalStateException(
            "Timed out waiting for the XDebug proxy to listen on $PROXY_LISTEN_ADDRESS:$port. See the Tyhp XDebug Proxy tool window.",
        )
    }

    private fun warnPrerequisites(launch: ResolvedProxyLaunch, projectDir: String?) {
        val mapDir = resolveMapDirectory(launch, projectDir)
        val mapCount = mapDir?.let { countMapsOnDisk(it) }
        val guidance = sourcemapGuidance(
            SourcemapGuidanceOptions(
                generateSourcemap = launch.generateSourcemap,
                mapCount = mapCount,
                sourceMapDir = launch.sourceMapDir ?: mapDir,
                outputPath = launch.outputPath,
            ),
        )
        if (guidance != null) {
            appendLog(guidance)
            notifyWarn(guidance)
        }
        appendLog(
            "Point PhpStorm’s Xdebug debug port at IDE port ${launch.idePort}; set XDebug client_port to ${launch.xdebugPort}. $XDEBUG_PROXY_DOCS_URL",
        )
    }

    private fun scheduleRunningRestart() {
        if (disposed.get() || project.isDisposed) {
            return
        }
        if (currentState != ProxyRunState.RUNNING && currentState != ProxyRunState.STARTING) {
            return
        }
        restartFuture?.cancel(false)
        restartFuture = AppExecutorUtil.getAppScheduledExecutorService().schedule(
            {
                if (disposed.get() || project.isDisposed) {
                    return@schedule
                }
                appendLog("Restarting XDebug proxy (settings or tyhp.json changed).")
                restart()
            },
            RESTART_DEBOUNCE_MS,
            TimeUnit.MILLISECONDS,
        )
    }

    private fun publishState(state: ProxyRunState) {
        for (listener in listeners) {
            try {
                listener.onProxyStateChanged(state)
            } catch (err: Throwable) {
                log.warn("Proxy state listener failed: ${err.message}", err)
            }
        }
    }

    private fun appendLog(line: String) {
        logPanel.append(line)
        log.info(line)
    }

    private fun notifyMissingBinary(detail: String) {
        val notification = NotificationGroupManager.getInstance()
            .getNotificationGroup(NOTIFICATION_GROUP)
            .createNotification("Tyhp XDebug Proxy", detail, NotificationType.ERROR)
        notification.addAction(
            NotificationAction.createSimple("Install / Update CLI") {
                BinaryManager.getInstance().installInteractive(project)
            },
        )
        notification.notify(project)
    }

    private fun notifyError(message: String) {
        notifyProxy(project, "Tyhp XDebug Proxy", message, NotificationType.ERROR)
    }

    private fun notifyWarn(message: String) {
        notifyProxy(project, "Tyhp XDebug Proxy", message, NotificationType.WARNING)
    }

    private fun notifyInfo(message: String) {
        notifyProxy(project, "Tyhp XDebug Proxy", message, NotificationType.INFORMATION)
    }

    companion object {
        fun getInstance(project: Project): XdebugProxyManager = project.getService(XdebugProxyManager::class.java)
    }
}

private class JvmSpawnedProxy(
    private val process: Process,
    onLine: (String) -> Unit,
) : SpawnedProxy {
    private val exitListeners = CopyOnWriteArrayList<(Int?) -> Unit>()
    private val errorListeners = CopyOnWriteArrayList<(Throwable) -> Unit>()

    init {
        Thread(
            {
                try {
                    process.inputStream.bufferedReader().use { reader ->
                        while (true) {
                            val line = reader.readLine() ?: break
                            onLine(line)
                        }
                    }
                } catch (err: Throwable) {
                    for (listener in errorListeners) {
                        listener(err)
                    }
                }
            },
            "tyhp-xdebug-proxy-out",
        ).apply { isDaemon = true }.start()

        Thread(
            {
                val code = try {
                    process.waitFor()
                } catch (err: Throwable) {
                    for (listener in errorListeners) {
                        listener(err)
                    }
                    -1
                }
                for (listener in exitListeners) {
                    listener(code)
                }
            },
            "tyhp-xdebug-proxy-wait",
        ).apply { isDaemon = true }.start()
    }

    override val pid: Long?
        get() = try {
            process.pid()
        } catch (_: Throwable) {
            null
        }

    override val exitCode: Int?
        get() = if (process.isAlive) null else process.exitValue()

    override fun onExit(listener: (Int?) -> Unit) {
        exitListeners.add(listener)
        val code = exitCode
        if (code != null) {
            listener(code)
        }
    }

    override fun onError(listener: (Throwable) -> Unit) {
        errorListeners.add(listener)
    }

    override fun destroyTerm(): Boolean {
        process.destroy()
        return true
    }
}

private fun readProjectSnapshot(projectFilePath: String?): TyhpJsonProjectSnapshot? {
    val path = projectFilePath?.trim()?.takeIf { it.isNotEmpty() } ?: return null
    return try {
        parseTyhpJsonProject(Files.readString(Path.of(path)))
    } catch (_: Throwable) {
        null
    }
}

private fun resolveMapDirectory(launch: ResolvedProxyLaunch, projectDir: String?): String? {
    val relative = launch.sourceMapDir ?: launch.outputPath ?: "build/"
    val asPath = Path.of(relative)
    if (asPath.isAbsolute) {
        return relative
    }
    if (projectDir.isNullOrBlank()) {
        return null
    }
    return Path.of(projectDir, relative).toString()
}

private fun countMapsOnDisk(dir: String): Int? {
    val path = Path.of(dir)
    if (!Files.isDirectory(path)) {
        return null
    }
    return try {
        Files.walk(path).use { stream ->
            countPhpMapFiles(stream.map { it.fileName.toString() }.toList())
        }
    } catch (_: Throwable) {
        null
    }
}

private fun isTyhpJsonEvent(event: VFileEvent, projectFile: String?): Boolean {
    val path = event.path
    if (projectFile != null && path == projectFile) {
        return true
    }
    return path.endsWith("/tyhp.json") || path.endsWith("\\tyhp.json")
}

private fun unwrap(err: Throwable): String {
    var current: Throwable? = err
    while (current?.cause != null && current.cause !== current && current.message.isNullOrBlank()) {
        current = current.cause
    }
    val message = current?.message ?: err.message
    return message?.takeIf { it.isNotBlank() } ?: err.toString()
}
