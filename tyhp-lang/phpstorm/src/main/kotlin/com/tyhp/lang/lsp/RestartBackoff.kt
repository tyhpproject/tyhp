package com.tyhp.lang.lsp

/** Default delay before the first unexpected-exit restart. */
const val DEFAULT_RESTART_INITIAL_MS = 1_000L

/** Cap so a crash loop cannot hammer the CLI. */
const val DEFAULT_RESTART_MAX_MS = 30_000L

/**
 * Exponential backoff for language-server crash recovery.
 * [consecutiveFailures] is 1-based (first crash → initial delay).
 */
fun nextRestartDelayMs(
    consecutiveFailures: Int,
    initialMs: Long = DEFAULT_RESTART_INITIAL_MS,
    maxMs: Long = DEFAULT_RESTART_MAX_MS,
): Long {
    val n = if (consecutiveFailures < 1) 1 else consecutiveFailures
    val exponent = minOf(n - 1, 16)
    val delay = initialMs * (1L shl exponent)
    return minOf(maxMs, delay)
}

class RestartBackoff(
    private val initialMs: Long = DEFAULT_RESTART_INITIAL_MS,
    private val maxMs: Long = DEFAULT_RESTART_MAX_MS,
) {
    var consecutiveFailures: Int = 0
        private set

    fun nextDelayMs(): Long {
        consecutiveFailures += 1
        return nextRestartDelayMs(consecutiveFailures, initialMs, maxMs)
    }

    fun reset() {
        consecutiveFailures = 0
    }
}
