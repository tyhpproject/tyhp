/** Default delay before the first unexpected-exit restart. */
export const DEFAULT_RESTART_INITIAL_MS = 1_000;

/** Cap so a crash loop cannot hammer the CLI. */
export const DEFAULT_RESTART_MAX_MS = 30_000;

/**
 * After this many unexpected exits *from a previously running server*, stop
 * retrying. A process that never reaches Running (missing CLI, stub
 * `language_server`, startFailed) must not retry at all.
 */
export const DEFAULT_MAX_CONSECUTIVE_FAILURES = 3;

/**
 * Whether crash recovery should spawn the server again.
 *
 * `neverStarted` is true when the process exited before initialize completed
 * (`startFailed` / stub binary). Retrying that cannot succeed until the user
 * changes `tyhp.path` or installs a current CLI.
 */
export function shouldScheduleCrashRestart(options: {
    neverStarted: boolean;
    consecutiveFailures: number;
    maxFailures?: number;
}): boolean {
    if (options.neverStarted) {
        return false;
    }
    const max = options.maxFailures ?? DEFAULT_MAX_CONSECUTIVE_FAILURES;
    return options.consecutiveFailures < max;
}

/**
 * Exponential backoff for language-server crash recovery.
 * `consecutiveFailures` is 1-based (first crash → initial delay).
 */
export function nextRestartDelayMs(
    consecutiveFailures: number,
    initialMs: number = DEFAULT_RESTART_INITIAL_MS,
    maxMs: number = DEFAULT_RESTART_MAX_MS
): number {
    const n = Number.isFinite(consecutiveFailures) ? Math.max(1, Math.floor(consecutiveFailures)) : 1;
    const exponent = Math.min(n - 1, 16);
    const delay = initialMs * 2 ** exponent;
    return Math.min(maxMs, delay);
}

export class RestartBackoff {
    private failures = 0;

    constructor(
        private readonly initialMs: number = DEFAULT_RESTART_INITIAL_MS,
        private readonly maxMs: number = DEFAULT_RESTART_MAX_MS
    ) {}

    get consecutiveFailures(): number {
        return this.failures;
    }

    nextDelayMs(): number {
        this.failures += 1;
        return nextRestartDelayMs(this.failures, this.initialMs, this.maxMs);
    }

    reset(): void {
        this.failures = 0;
    }
}
