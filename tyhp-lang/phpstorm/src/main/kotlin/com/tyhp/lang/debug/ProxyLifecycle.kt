package com.tyhp.lang.debug

/**
 * Pure start/stop state machine for the XDebug proxy child process.
 * Unexpected exit from `running` is `error`; a requested stop ends in `stopped`.
 */

enum class ProxyRunState {
    STOPPED,
    STARTING,
    RUNNING,
    STOPPING,
    ERROR,
}

sealed class ProxyLifecycleEvent {
    data object StartRequested : ProxyLifecycleEvent()
    data object Listening : ProxyLifecycleEvent()
    data object StartFailed : ProxyLifecycleEvent()
    data object StopRequested : ProxyLifecycleEvent()
    data class Exited(val expected: Boolean) : ProxyLifecycleEvent()
}

fun nextProxyState(state: ProxyRunState, event: ProxyLifecycleEvent): ProxyRunState {
    return when (event) {
        ProxyLifecycleEvent.StartRequested ->
            if (state == ProxyRunState.RUNNING || state == ProxyRunState.STARTING) {
                state
            } else {
                ProxyRunState.STARTING
            }
        ProxyLifecycleEvent.Listening ->
            if (state == ProxyRunState.STARTING || state == ProxyRunState.RUNNING) {
                ProxyRunState.RUNNING
            } else {
                state
            }
        ProxyLifecycleEvent.StartFailed ->
            if (state == ProxyRunState.STARTING) ProxyRunState.ERROR else state
        ProxyLifecycleEvent.StopRequested ->
            if (state == ProxyRunState.STOPPED) {
                ProxyRunState.STOPPED
            } else {
                ProxyRunState.STOPPING
            }
        is ProxyLifecycleEvent.Exited -> {
            if (state == ProxyRunState.ERROR) {
                ProxyRunState.ERROR
            } else if (event.expected || state == ProxyRunState.STOPPING || state == ProxyRunState.STOPPED) {
                ProxyRunState.STOPPED
            } else {
                ProxyRunState.ERROR
            }
        }
    }
}

fun proxyIsActive(state: ProxyRunState): Boolean =
    state == ProxyRunState.STARTING ||
        state == ProxyRunState.RUNNING ||
        state == ProxyRunState.STOPPING

fun proxyIsListening(state: ProxyRunState): Boolean = state == ProxyRunState.RUNNING
