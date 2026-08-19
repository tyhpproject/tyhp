package com.tyhp.lang.lsp

import com.intellij.openapi.Disposable
import com.intellij.openapi.components.Service
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import java.util.concurrent.CopyOnWriteArrayList

/** Compact LSP lifecycle for the status bar (Phase 12). Proxy is Phase 13. */
enum class LspClientState {
    STOPPED,
    STARTING,
    RUNNING,
    ERROR,
}

fun interface LspStateListener {
    fun onLspStateChanged(state: LspClientState)
}

/**
 * Project-wide LSP state hub. Lives in the main plugin so the status bar can
 * subscribe without loading Platform LSP types. [TyhpLspLifecycle] publishes
 * here when the optional Ultimate/LSP module is present.
 */
@Service(Service.Level.PROJECT)
class TyhpLspStateHub : Disposable {
    @Volatile
    var currentState: LspClientState = LspClientState.STOPPED
        private set

    private val listeners = CopyOnWriteArrayList<LspStateListener>()

    fun addListener(parentDisposable: Disposable, listener: LspStateListener) {
        listeners.add(listener)
        Disposer.register(parentDisposable) { listeners.remove(listener) }
    }

    fun publish(state: LspClientState) {
        if (currentState == state) {
            return
        }
        currentState = state
        for (listener in listeners) {
            try {
                listener.onLspStateChanged(state)
            } catch (_: Throwable) {
                // Status-bar listeners must not break LSP lifecycle.
            }
        }
    }

    override fun dispose() {
        listeners.clear()
        currentState = LspClientState.STOPPED
    }

    companion object {
        fun getInstance(project: Project): TyhpLspStateHub = project.getService(TyhpLspStateHub::class.java)
    }
}
