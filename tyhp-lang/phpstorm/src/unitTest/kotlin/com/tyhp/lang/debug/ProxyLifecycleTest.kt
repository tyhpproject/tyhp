package com.tyhp.lang.debug

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ProxyLifecycleTest {
    @Test
    fun `start then listening reaches running`() {
        var state = nextProxyState(ProxyRunState.STOPPED, ProxyLifecycleEvent.StartRequested)
        assertEquals(ProxyRunState.STARTING, state)
        state = nextProxyState(state, ProxyLifecycleEvent.Listening)
        assertEquals(ProxyRunState.RUNNING, state)
        assertTrue(proxyIsListening(state))
        assertTrue(proxyIsActive(state))
    }

    @Test
    fun `start while already running is a no-op`() {
        assertEquals(
            ProxyRunState.RUNNING,
            nextProxyState(ProxyRunState.RUNNING, ProxyLifecycleEvent.StartRequested),
        )
        assertEquals(
            ProxyRunState.STARTING,
            nextProxyState(ProxyRunState.STARTING, ProxyLifecycleEvent.StartRequested),
        )
    }

    @Test
    fun `start failure from starting becomes error`() {
        assertEquals(
            ProxyRunState.ERROR,
            nextProxyState(ProxyRunState.STARTING, ProxyLifecycleEvent.StartFailed),
        )
        assertEquals(
            ProxyRunState.RUNNING,
            nextProxyState(ProxyRunState.RUNNING, ProxyLifecycleEvent.StartFailed),
        )
    }

    @Test
    fun `requested stop then expected exit becomes stopped`() {
        var state = nextProxyState(ProxyRunState.RUNNING, ProxyLifecycleEvent.StopRequested)
        assertEquals(ProxyRunState.STOPPING, state)
        state = nextProxyState(state, ProxyLifecycleEvent.Exited(expected = true))
        assertEquals(ProxyRunState.STOPPED, state)
        assertFalse(proxyIsActive(state))
    }

    @Test
    fun `unexpected exit from running becomes error`() {
        assertEquals(
            ProxyRunState.ERROR,
            nextProxyState(ProxyRunState.RUNNING, ProxyLifecycleEvent.Exited(expected = false)),
        )
        assertEquals(
            ProxyRunState.ERROR,
            nextProxyState(ProxyRunState.STARTING, ProxyLifecycleEvent.Exited(expected = false)),
        )
    }

    @Test
    fun `stop from stopped stays stopped`() {
        assertEquals(
            ProxyRunState.STOPPED,
            nextProxyState(ProxyRunState.STOPPED, ProxyLifecycleEvent.StopRequested),
        )
        assertEquals(
            ProxyRunState.STOPPED,
            nextProxyState(ProxyRunState.STOPPED, ProxyLifecycleEvent.Exited(expected = true)),
        )
    }

    @Test
    fun `cleanup exit after start failure stays error`() {
        val failed = nextProxyState(ProxyRunState.STARTING, ProxyLifecycleEvent.StartFailed)
        assertEquals(ProxyRunState.ERROR, failed)
        assertEquals(ProxyRunState.ERROR, nextProxyState(failed, ProxyLifecycleEvent.Exited(expected = true)))
    }
}
