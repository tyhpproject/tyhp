package com.tyhp.lang.lsp

import kotlin.test.Test
import kotlin.test.assertEquals

class RestartBackoffTest {
    @Test
    fun `first crash uses the initial delay`() {
        assertEquals(DEFAULT_RESTART_INITIAL_MS, nextRestartDelayMs(1))
        assertEquals(DEFAULT_RESTART_INITIAL_MS, nextRestartDelayMs(0))
    }

    @Test
    fun `delays double until the cap`() {
        assertEquals(2_000L, nextRestartDelayMs(2))
        assertEquals(4_000L, nextRestartDelayMs(3))
        assertEquals(8_000L, nextRestartDelayMs(4))
        assertEquals(16_000L, nextRestartDelayMs(5))
        assertEquals(DEFAULT_RESTART_MAX_MS, nextRestartDelayMs(6))
        assertEquals(DEFAULT_RESTART_MAX_MS, nextRestartDelayMs(20))
    }

    @Test
    fun `RestartBackoff increments then resets after a healthy start`() {
        val backoff = RestartBackoff(100, 800)
        assertEquals(100L, backoff.nextDelayMs())
        assertEquals(200L, backoff.nextDelayMs())
        assertEquals(400L, backoff.nextDelayMs())
        assertEquals(800L, backoff.nextDelayMs())
        assertEquals(800L, backoff.nextDelayMs())
        assertEquals(5, backoff.consecutiveFailures)
        backoff.reset()
        assertEquals(0, backoff.consecutiveFailures)
        assertEquals(100L, backoff.nextDelayMs())
    }
}
