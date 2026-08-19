import * as path from "path";
import * as vscode from "vscode";
import {
    CloseAction,
    ErrorAction,
    LanguageClient,
    RevealOutputChannelOn,
    State,
    Trace,
} from "vscode-languageclient/node";
import { resolveTyhpBinary } from "../binary/BinaryManager";
import * as settings from "../config/settings";
import { probeLanguageServerSupport } from "./cliProbe";
import { createOwnerMiddleware } from "./documentRouting";
import { RestartBackoff, shouldScheduleCrashRestart } from "./restartBackoff";
import { buildLanguageServerArgs } from "./serverArgs";

export type LspClientState = "stopped" | "starting" | "running" | "error";

export interface LspSessionHost {
    readonly output: vscode.OutputChannel;
    readonly disposed: () => boolean;
    ownerProjectFileOf(uri: vscode.Uri): string | undefined;
    offerMissingBinary(detail: string): Promise<void>;
    giveUpStarting(detail: string): Promise<void>;
    onSessionState(projectFilePath: string, state: LspClientState): void;
}

/**
 * One `tyhp language_server` process bound to a single `tyhp.json`.
 */
export class LspSession {
    private readonly backoff = new RestartBackoff();
    private readonly clientDisposables: vscode.Disposable[] = [];
    private client: LanguageClient | undefined;
    private startInFlight: Promise<void> | undefined;
    private restartTimer: NodeJS.Timeout | undefined;
    private restartScheduled = false;
    private intentionalStop = false;
    private lastStartedKey: string | undefined;
    private reachedRunning = false;
    private state: LspClientState = "stopped";

    constructor(
        readonly projectFilePath: string,
        private readonly host: LspSessionHost
    ) {}

    get currentState(): LspClientState {
        return this.state;
    }

    async start(): Promise<void> {
        if (this.host.disposed()) {
            return;
        }
        if (this.startInFlight) {
            return this.startInFlight;
        }
        this.startInFlight = this.startImpl().finally(() => {
            this.startInFlight = undefined;
        });
        return this.startInFlight;
    }

    async restart(): Promise<void> {
        if (this.host.disposed()) {
            return;
        }
        this.cancelScheduledRestart();
        this.backoff.reset();
        this.reachedRunning = false;
        await this.stopClient({ intentional: true });
        await this.start();
    }

    async stop(): Promise<void> {
        this.cancelScheduledRestart();
        await this.stopClient({ intentional: true });
        this.setState("stopped");
    }

    private async startImpl(): Promise<void> {
        if (this.host.disposed()) {
            return;
        }
        if (this.client && (this.state === "running" || this.state === "starting")) {
            return;
        }
        if (this.client) {
            await this.stopClient({ intentional: true });
            this.intentionalStop = false;
        }

        const resolved = await resolveTyhpBinary();
        if (resolved.status !== "ok" || !resolved.executablePath) {
            this.setState("error");
            const detail =
                resolved.message ??
                "Tyhp CLI was not found. Use “Tyhp: Install / Update CLI” or set `tyhp.path`.";
            this.host.output.appendLine(`[${this.label()}] Cannot start language server: ${detail}`);
            await this.host.offerMissingBinary(detail);
            return;
        }

        const support = await probeLanguageServerSupport(resolved.executablePath);
        if (support === "unimplemented") {
            this.host.output.appendLine(
                `[${this.label()}] CLI at ${resolved.executablePath} does not implement language_server (pre-Story 19 stub).`
            );
            await this.host.giveUpStarting(
                "This Tyhp CLI is too old to run the language server. Install a current CLI or set `tyhp.path` to a local build that includes `language_server`."
            );
            this.setState("error");
            return;
        }

        const args = buildLanguageServerArgs({
            projectFilePath: this.projectFilePath,
            extraArgs: settings.getLanguageServerArgs(),
        });
        const cwd = path.dirname(this.projectFilePath);
        this.lastStartedKey = serverKey(resolved.executablePath, args, cwd);
        this.setState("starting");
        this.intentionalStop = false;
        this.reachedRunning = false;
        this.host.output.appendLine(
            `[${this.label()}] Starting: ${resolved.executablePath} ${args.join(" ")} (cwd ${cwd})`
        );

        const client = this.createClient(resolved.executablePath, args, cwd);
        this.client = client;
        this.clientDisposables.push(
            client.onDidChangeState((event) => {
                if (event.newState === State.Running) {
                    this.reachedRunning = true;
                    this.setState("running");
                    this.backoff.reset();
                    this.host.output.appendLine(`[${this.label()}] Language server is running.`);
                } else if (event.newState === State.Stopped && this.state === "running") {
                    this.setState("stopped");
                }
            })
        );

        try {
            await client.start();
            this.applyTrace();
            this.reachedRunning = true;
            this.setState("running");
            this.backoff.reset();
        } catch (err) {
            this.setState("error");
            const message = err instanceof Error ? err.message : String(err);
            this.host.output.appendLine(`[${this.label()}] Language server failed to start: ${message}`);
            await this.stopClient({ intentional: true });
            this.intentionalStop = false;
            this.scheduleCrashRestart({ neverStarted: !this.reachedRunning });
        }
    }

    private createClient(command: string, args: string[], cwd: string): LanguageClient {
        const projectFilePath = this.projectFilePath;
        const host = this.host;
        const client = new LanguageClient(
            `tyhp.languageServer:${projectFilePath}`,
            "Tyhp Language Server",
            {
                command,
                args,
                options: { cwd },
            },
            {
                documentSelector: [{ language: "tyhp", scheme: "file" }],
                outputChannel: host.output,
                traceOutputChannel: host.output,
                diagnosticCollectionName: `tyhp:${projectFilePath}`,
                revealOutputChannelOn: RevealOutputChannelOn.Never,
                initializationFailedHandler: (error) => {
                    const message = error instanceof Error ? error.message : String(error);
                    host.output.appendLine(`[${this.label()}] Language server initialize failed: ${message}`);
                    return false;
                },
                errorHandler: {
                    error: () => ({ action: ErrorAction.Continue, handled: true }),
                    closed: () => {
                        if (!this.intentionalStop && !host.disposed()) {
                            host.output.appendLine(
                                `[${this.label()}] Language server process exited unexpectedly.`
                            );
                            this.scheduleCrashRestart({ neverStarted: !this.reachedRunning });
                        }
                        return { action: CloseAction.DoNotRestart, handled: true };
                    },
                },
                middleware: createOwnerMiddleware(projectFilePath, (uri) => host.ownerProjectFileOf(uri)),
            }
        );
        return client;
    }

    applyTrace(): void {
        if (!this.client) {
            return;
        }
        const trace = toTrace(settings.getLanguageServerTrace());
        void this.client.setTrace(trace);
        this.host.output.appendLine(`[${this.label()}] LSP trace: ${settings.getLanguageServerTrace()}`);
    }

    private scheduleCrashRestart(options: { neverStarted: boolean }): void {
        if (this.host.disposed() || this.intentionalStop || this.restartScheduled) {
            return;
        }
        if (
            !shouldScheduleCrashRestart({
                neverStarted: options.neverStarted,
                consecutiveFailures: this.backoff.consecutiveFailures,
            })
        ) {
            const detail = options.neverStarted
                ? "The Tyhp language server exited before it finished starting. The CLI may be missing `language_server`, too old, or crashing on launch. Install a current CLI or set `tyhp.path` to a working build."
                : "The Tyhp language server crashed repeatedly and will not be restarted automatically. Use “Tyhp: Restart Language Server” after fixing the CLI.";
            void this.host.giveUpStarting(detail);
            this.setState("error");
            return;
        }
        this.restartScheduled = true;
        const delay = this.backoff.nextDelayMs();
        this.setState("error");
        this.host.output.appendLine(
            `[${this.label()}] Restarting language server in ${delay}ms (attempt ${this.backoff.consecutiveFailures}).`
        );
        this.restartTimer = setTimeout(() => {
            this.restartTimer = undefined;
            this.restartScheduled = false;
            void this.recoverAfterCrash();
        }, delay);
    }

    private async recoverAfterCrash(): Promise<void> {
        await this.stopClient({ intentional: true });
        this.intentionalStop = false;
        await this.start();
    }

    private cancelScheduledRestart(): void {
        this.restartScheduled = false;
        if (this.restartTimer) {
            clearTimeout(this.restartTimer);
            this.restartTimer = undefined;
        }
    }

    private async stopClient(options: { intentional: boolean }): Promise<void> {
        this.intentionalStop = options.intentional;
        const client = this.client;
        this.client = undefined;
        this.lastStartedKey = undefined;
        this.disposeClientListeners();
        if (!client) {
            return;
        }
        try {
            await client.stop();
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            this.host.output.appendLine(`[${this.label()}] Error stopping language server: ${message}`);
        }
    }

    private disposeClientListeners(): void {
        for (const d of this.clientDisposables) {
            d.dispose();
        }
        this.clientDisposables.length = 0;
    }

    private setState(state: LspClientState): void {
        if (this.state === state) {
            return;
        }
        this.state = state;
        this.host.onSessionState(this.projectFilePath, state);
    }

    private label(): string {
        return path.basename(path.dirname(this.projectFilePath));
    }
}

export function serverKey(command: string, args: readonly string[], cwd: string | undefined): string {
    return JSON.stringify([command, args, cwd ?? ""]);
}

export function toTrace(value: string): Trace {
    switch (value) {
        case "verbose":
            return Trace.Verbose;
        case "messages":
            return Trace.Messages;
        default:
            return Trace.Off;
    }
}
