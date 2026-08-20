import { EventEmitter } from "node:events";
import { nextProxyState, ProxyRunState } from "./proxyLifecycle";

/**
 * Minimal child-process surface used by the proxy controller. Tests inject
 * fakes; the manager wraps `child_process.spawn`.
 */
export interface SpawnedProxy {
    readonly pid?: number;
    readonly stdout?: NodeJS.ReadableStream;
    readonly stderr?: NodeJS.ReadableStream;
    readonly exitCode: number | null;
    on(event: "exit" | "error", listener: (...args: unknown[]) => void): void;
    /** Manager stop path — Node `ChildProcess.kill`, not a shell `kill`. */
    kill(signal?: NodeJS.Signals): boolean;
}

export interface ProxyStartRequest {
    command: string;
    args: readonly string[];
    cwd?: string;
    idePort: number;
}

export interface ProxyProcessHooks {
    spawn(command: string, args: readonly string[], cwd?: string): SpawnedProxy;
    waitForListening(
        port: number,
        child: SpawnedProxy,
        abort: AbortSignal
    ): Promise<number>;
    stopProcess(child: SpawnedProxy): Promise<void>;
    onLog?(line: string): void;
    onState?(state: ProxyRunState): void;
    onUnexpectedExit?(code: number | null): void;
    onNoSourcemaps?(): void;
}

const STOP_TIMEOUT_MS = 8_000;

/**
 * Serializes start/stop/restart and applies {@link nextProxyState}. Does not
 * import `vscode` so the state machine can be unit-tested under `node --test`.
 */
export class ProxyProcessController {
    private state: ProxyRunState = "stopped";
    private child: SpawnedProxy | undefined;
    private expectedStop = false;
    private startAbort: AbortController | undefined;
    private queue: Promise<void> = Promise.resolve();
    private readonly exitBound = (code: unknown, _signal: unknown) => {
        this.handleExit(typeof code === "number" ? code : null);
    };

    constructor(private readonly hooks: ProxyProcessHooks) {}

    get currentState(): ProxyRunState {
        return this.state;
    }

    get currentPid(): number | undefined {
        return this.child?.pid;
    }

    start(request: ProxyStartRequest): Promise<void> {
        return this.enqueue(() => this.startImpl(request));
    }

    stop(): Promise<void> {
        return this.enqueue(() => this.stopImpl());
    }

    restart(request: ProxyStartRequest): Promise<void> {
        return this.enqueue(async () => {
            await this.stopImpl();
            await this.startImpl(request);
        });
    }

    private enqueue(work: () => Promise<void>): Promise<void> {
        const run = this.queue.then(work, work);
        this.queue = run.then(
            () => undefined,
            () => undefined
        );
        return run;
    }

    private async startImpl(request: ProxyStartRequest): Promise<void> {
        if (this.state === "running" || this.state === "starting") {
            return;
        }
        this.setState(nextProxyState(this.state, { type: "startRequested" }));
        this.expectedStop = false;
        this.startAbort = new AbortController();
        try {
            const child = this.hooks.spawn(request.command, request.args, request.cwd);
            this.child = child;
            child.on("exit", this.exitBound);
            child.on("error", (err: unknown) => {
                const message = err instanceof Error ? err.message : String(err);
                this.hooks.onLog?.(`Proxy process error: ${message}`);
            });
            const boundPort = await this.hooks.waitForListening(
                request.idePort,
                child,
                this.startAbort.signal
            );
            if (this.startAbort.signal.aborted) {
                await this.stopImpl();
                return;
            }
            this.hooks.onLog?.(`Proxy listening on IDE port ${boundPort}.`);
            this.setState(nextProxyState(this.state, { type: "listening" }));
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            this.hooks.onLog?.(`Proxy failed to start: ${message}`);
            this.setState(nextProxyState(this.state, { type: "startFailed" }));
            await this.cleanupChild({ keepError: true });
            throw err;
        } finally {
            this.startAbort = undefined;
        }
    }

    private async stopImpl(): Promise<void> {
        await this.cleanupChild({ keepError: false });
    }

    private async cleanupChild(options: { keepError: boolean }): Promise<void> {
        this.startAbort?.abort();
        this.startAbort = undefined;
        const child = this.child;
        if (!child) {
            if (!options.keepError) {
                this.setState("stopped");
            }
            return;
        }
        this.expectedStop = true;
        if (!options.keepError) {
            this.setState(nextProxyState(this.state, { type: "stopRequested" }));
        }
        try {
            await withTimeout(
                this.hooks.stopProcess(child),
                STOP_TIMEOUT_MS,
                "Proxy did not exit after SIGTERM"
            );
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            this.hooks.onLog?.(message);
            this.setState("error");
            this.child = undefined;
            throw err;
        }
        this.child = undefined;
        if (!options.keepError) {
            this.setState(nextProxyState(this.state, { type: "exited", expected: true }));
        }
    }

    private handleExit(code: number | null): void {
        const expected = this.expectedStop;
        this.child = undefined;
        this.setState(nextProxyState(this.state, { type: "exited", expected }));
        if (!expected && this.state === "error") {
            this.hooks.onUnexpectedExit?.(code);
        }
    }

    private setState(state: ProxyRunState): void {
        if (this.state === state) {
            return;
        }
        this.state = state;
        this.hooks.onState?.(state);
    }
}

export function waitForChildExit(child: SpawnedProxy, timeoutMs: number): Promise<void> {
    if (child.exitCode !== null) {
        return Promise.resolve();
    }
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            reject(new Error(`Proxy process did not exit within ${timeoutMs}ms`));
        }, timeoutMs);
        child.on("exit", () => {
            clearTimeout(timer);
            resolve();
        });
    });
}

/**
 * Manager stop path: signal the child with SIGTERM and wait for `exit`.
 * Does not use shell `kill` / `killall`.
 */
export async function stopSpawnedProxy(child: SpawnedProxy): Promise<void> {
    if (child.exitCode !== null) {
        return;
    }
    child.kill("SIGTERM");
    await waitForChildExit(child, STOP_TIMEOUT_MS);
}

function withTimeout<T>(promise: Promise<T>, ms: number, message: string): Promise<T> {
    return new Promise<T>((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error(message)), ms);
        promise.then(
            (value) => {
                clearTimeout(timer);
                resolve(value);
            },
            (err) => {
                clearTimeout(timer);
                reject(err);
            }
        );
    });
}

/** Test helper: an in-memory child that records `kill` and can be exited. */
export class FakeSpawnedProxy extends EventEmitter implements SpawnedProxy {
    pid = 4242;
    exitCode: number | null = null;
    killed = false;
    lastSignal: NodeJS.Signals | undefined;

    kill(signal?: NodeJS.Signals): boolean {
        this.killed = true;
        this.lastSignal = signal;
        this.exit(0, signal);
        return true;
    }

    exit(code: number | null = 0, signal?: NodeJS.Signals): void {
        if (this.exitCode !== null) {
            return;
        }
        this.exitCode = code;
        this.emit("exit", code, signal);
    }
}
