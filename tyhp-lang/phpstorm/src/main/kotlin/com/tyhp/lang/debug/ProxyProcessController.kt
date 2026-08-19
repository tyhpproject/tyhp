package com.tyhp.lang.debug

import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Minimal child-process surface used by the proxy controller. Tests inject
 * fakes; the manager wraps `Process`.
 */
interface SpawnedProxy {
    val pid: Long?
    val exitCode: Int?
    fun onExit(listener: (Int?) -> Unit)
    fun onError(listener: (Throwable) -> Unit)
    /** Manager stop path — JVM `Process.destroy()` (SIGTERM on Unix), not a shell `kill`. */
    fun destroyTerm(): Boolean
}

data class ProxyStartRequest(
    val command: String,
    val args: List<String>,
    val cwd: String? = null,
    val idePort: Int,
)

interface ProxyProcessHooks {
    fun spawn(command: String, args: List<String>, cwd: String?): SpawnedProxy
    fun waitForListening(port: Int, child: SpawnedProxy, abort: AtomicBoolean): Int
    fun stopProcess(child: SpawnedProxy)
    fun onLog(line: String) {}
    fun onState(state: ProxyRunState) {}
    fun onUnexpectedExit(code: Int?) {}
}

const val PROXY_STOP_TIMEOUT_MS = 8_000L
const val PROXY_LISTEN_TIMEOUT_MS = 15_000L

/**
 * Serializes start/stop/restart and applies [nextProxyState]. IntelliJ-free so
 * the state machine can be unit-tested as a plain JVM test.
 */
class ProxyProcessController(
    private val hooks: ProxyProcessHooks,
    private val executor: ExecutorService = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "tyhp-xdebug-proxy").apply { isDaemon = true }
    },
) {
    private val lock = Any()

    @Volatile
    var currentState: ProxyRunState = ProxyRunState.STOPPED
        private set

    @Volatile
    private var child: SpawnedProxy? = null

    @Volatile
    private var expectedStop = false

    private val startAbort = AtomicBoolean(false)

    val currentPid: Long?
        get() = child?.pid

    fun start(request: ProxyStartRequest): CompletableFuture<Void> =
        enqueue { startImpl(request) }

    fun stop(): CompletableFuture<Void> =
        enqueue { stopImpl() }

    fun restart(request: ProxyStartRequest): CompletableFuture<Void> =
        enqueue {
            stopImpl()
            startImpl(request)
        }

    fun shutdownExecutor() {
        executor.shutdown()
    }

    private fun enqueue(work: () -> Unit): CompletableFuture<Void> {
        return CompletableFuture.runAsync(work, executor)
    }

    private fun startImpl(request: ProxyStartRequest) {
        if (currentState == ProxyRunState.RUNNING || currentState == ProxyRunState.STARTING) {
            return
        }
        setState(nextProxyState(currentState, ProxyLifecycleEvent.StartRequested))
        expectedStop = false
        startAbort.set(false)
        try {
            val spawned = hooks.spawn(request.command, request.args, request.cwd)
            child = spawned
            spawned.onExit { code -> handleExit(code) }
            spawned.onError { err ->
                hooks.onLog("Proxy process error: ${err.message ?: err}")
            }
            val boundPort = hooks.waitForListening(request.idePort, spawned, startAbort)
            if (startAbort.get()) {
                stopImpl()
                return
            }
            hooks.onLog("Proxy listening on IDE port $boundPort.")
            setState(nextProxyState(currentState, ProxyLifecycleEvent.Listening))
        } catch (err: Throwable) {
            val message = err.message ?: err.toString()
            hooks.onLog("Proxy failed to start: $message")
            setState(nextProxyState(currentState, ProxyLifecycleEvent.StartFailed))
            cleanupChild(keepError = true)
            throw err
        }
    }

    private fun stopImpl() {
        cleanupChild(keepError = false)
    }

    private fun cleanupChild(keepError: Boolean) {
        startAbort.set(true)
        val spawned = child
        if (spawned == null) {
            if (!keepError) {
                setState(ProxyRunState.STOPPED)
            }
            return
        }
        expectedStop = true
        if (!keepError) {
            setState(nextProxyState(currentState, ProxyLifecycleEvent.StopRequested))
        }
        try {
            withTimeout(PROXY_STOP_TIMEOUT_MS, "Proxy did not exit after SIGTERM") {
                hooks.stopProcess(spawned)
            }
        } catch (err: Throwable) {
            hooks.onLog(err.message ?: err.toString())
            setState(ProxyRunState.ERROR)
            child = null
            throw err
        }
        child = null
        if (!keepError) {
            setState(nextProxyState(currentState, ProxyLifecycleEvent.Exited(expected = true)))
        }
    }

    private fun handleExit(code: Int?) {
        val expected = expectedStop
        child = null
        setState(nextProxyState(currentState, ProxyLifecycleEvent.Exited(expected)))
        if (!expected && currentState == ProxyRunState.ERROR) {
            hooks.onUnexpectedExit(code)
        }
    }

    private fun setState(state: ProxyRunState) {
        synchronized(lock) {
            if (currentState == state) {
                return
            }
            currentState = state
        }
        hooks.onState(state)
    }
}

fun waitForChildExit(child: SpawnedProxy, timeoutMs: Long) {
    if (child.exitCode != null) {
        return
    }
    val done = CompletableFuture<Void>()
    child.onExit { done.complete(null) }
    if (child.exitCode != null) {
        return
    }
    try {
        done.get(timeoutMs, TimeUnit.MILLISECONDS)
    } catch (err: Exception) {
        throw IllegalStateException("Proxy process did not exit within ${timeoutMs}ms", err)
    }
}

/**
 * Manager stop path: signal the child with SIGTERM (`Process.destroy`) and wait
 * for exit. Does not use shell `kill` / `killall`.
 */
fun stopSpawnedProxy(child: SpawnedProxy) {
    if (child.exitCode != null) {
        return
    }
    val done = CompletableFuture<Void>()
    child.onExit { done.complete(null) }
    if (child.exitCode != null) {
        return
    }
    child.destroyTerm()
    try {
        done.get(PROXY_STOP_TIMEOUT_MS, TimeUnit.MILLISECONDS)
    } catch (err: Exception) {
        throw IllegalStateException("Proxy process did not exit within ${PROXY_STOP_TIMEOUT_MS}ms", err)
    }
}

private fun withTimeout(ms: Long, message: String, work: () -> Unit) {
    val future = CompletableFuture.runAsync(work)
    try {
        future.get(ms, TimeUnit.MILLISECONDS)
    } catch (err: Exception) {
        throw IllegalStateException(message, err)
    }
}

/** Test helper: an in-memory child that records `destroyTerm` and can be exited. */
class FakeSpawnedProxy : SpawnedProxy {
    override var pid: Long? = 4242
    override var exitCode: Int? = null
        private set

    var killed: Boolean = false
        private set
    var lastSignal: String? = null
        private set

    private val exitListeners = CopyOnWriteArrayList<(Int?) -> Unit>()
    private val errorListeners = CopyOnWriteArrayList<(Throwable) -> Unit>()

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
        killed = true
        lastSignal = "SIGTERM"
        exit(0)
        return true
    }

    fun exit(code: Int? = 0) {
        if (exitCode != null) {
            return
        }
        exitCode = code
        for (listener in exitListeners) {
            listener(code)
        }
    }
}
