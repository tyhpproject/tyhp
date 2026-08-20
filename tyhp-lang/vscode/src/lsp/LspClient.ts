import * as vscode from "vscode";
import * as settings from "../config/settings";
import { LspClientState, LspSession, LspSessionHost } from "./LspSession";

export type { LspClientState };

const OUTPUT_CHANNEL_NAME = "Tyhp Language Server";

let instance: LspClient | undefined;

export interface LspWorkspaceLookup {
    ownerOfUri(uri: vscode.Uri): { projectFilePath: string } | undefined;
}

/**
 * One language client per owned `tyhp.json`, started lazily when a file that
 * project owns is opened. Idle-stopped when no open Tyhp documents remain for
 * that project.
 */
export class LspClient implements vscode.Disposable, LspSessionHost {
    readonly output: vscode.OutputChannel;
    private readonly disposables: vscode.Disposable[] = [];
    private readonly sessions = new Map<string, LspSession>();
    private readonly stateEmitter = new vscode.EventEmitter<LspClientState>();
    private disposedFlag = false;
    private missingBinaryNotified = false;
    private gaveUpNotified = false;
    private workspace: LspWorkspaceLookup | undefined;

    readonly onDidChangeClientState = this.stateEmitter.event;

    constructor() {
        this.output = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
        this.disposables.push(
            this.output,
            this.stateEmitter,
            vscode.workspace.onDidChangeConfiguration((e) => {
                if (
                    e.affectsConfiguration("tyhp.path") ||
                    e.affectsConfiguration("tyhp.projectPath") ||
                    e.affectsConfiguration("tyhp.languageServer.args")
                ) {
                    void this.restart();
                } else if (e.affectsConfiguration("tyhp.languageServer.trace")) {
                    this.applyTrace();
                }
            }),
            vscode.workspace.onDidChangeWorkspaceFolders(() => {
                void this.restart();
            }),
            vscode.workspace.onDidOpenTextDocument((document) => {
                void this.ensureSessionForDocument(document);
            }),
            vscode.workspace.onDidCloseTextDocument(() => {
                void this.stopIdleSessions();
            }),
            vscode.window.onDidChangeActiveTextEditor(() => {
                this.fireStateForActiveEditor();
            })
        );
    }

    bindWorkspace(workspace: LspWorkspaceLookup): void {
        this.workspace = workspace;
    }

    disposed = (): boolean => this.disposedFlag;

    ownerProjectFileOf(uri: vscode.Uri): string | undefined {
        return this.workspace?.ownerOfUri(uri)?.projectFilePath;
    }

    get currentState(): LspClientState {
        const session = this.sessionForActiveEditor();
        return session?.currentState ?? "stopped";
    }

    /**
     * Start sessions for documents that are already open (activation / CLI refresh).
     * Does not start a server for every discovered `tyhp.json`.
     */
    async start(): Promise<void> {
        await this.ensureOpenDocuments();
    }

    async ensureSessionForActiveDocument(): Promise<void> {
        const document = vscode.window.activeTextEditor?.document;
        if (document) {
            await this.ensureSessionForDocument(document);
        }
    }

    async ensureSessionForDocument(document: vscode.TextDocument): Promise<void> {
        if (this.disposedFlag || document.languageId !== "tyhp" || document.uri.scheme !== "file") {
            return;
        }
        const owner = this.workspace?.ownerOfUri(document.uri);
        if (!owner) {
            return;
        }
        await this.ensureSession(owner.projectFilePath);
    }

    async ensureOpenDocuments(): Promise<void> {
        for (const document of vscode.workspace.textDocuments) {
            await this.ensureSessionForDocument(document);
        }
        await this.stopIdleSessions();
        this.fireStateForActiveEditor();
    }

    async restart(): Promise<void> {
        if (this.disposedFlag) {
            return;
        }
        this.gaveUpNotified = false;
        const running = [...this.sessions.values()];
        for (const session of running) {
            await session.restart();
        }
        await this.ensureOpenDocuments();
    }

    async restartSession(projectFilePath: string): Promise<void> {
        const session = this.sessions.get(projectFilePath);
        if (session) {
            await session.restart();
            return;
        }
        await this.ensureOpenDocuments();
    }

    async stopIdleSessions(): Promise<void> {
        if (this.disposedFlag) {
            return;
        }
        const openOwners = this.openOwnedProjectFiles();
        const idle = [...this.sessions.keys()].filter((key) => !openOwners.has(key));
        for (const key of idle) {
            const session = this.sessions.get(key);
            this.sessions.delete(key);
            this.output.appendLine(
                `[${sessionLabel(key)}] Stopping language server (no open documents in this project).`
            );
            await session?.stop();
        }
        this.fireStateForActiveEditor();
    }

    async stop(): Promise<void> {
        this.disposedFlag = true;
        const running = [...this.sessions.values()];
        this.sessions.clear();
        for (const session of running) {
            await session.stop();
        }
        this.stateEmitter.fire("stopped");
    }

    dispose(): void {
        this.disposedFlag = true;
        void this.stop();
        for (const d of this.disposables) {
            d.dispose();
        }
        this.disposables.length = 0;
    }

    async offerMissingBinary(detail: string): Promise<void> {
        if (this.missingBinaryNotified) {
            return;
        }
        this.missingBinaryNotified = true;
        this.output.appendLine(
            `Language server not started (CLI unavailable). Use the Tyhp status bar or “Tyhp: Install / Update CLI”. ${detail}`
        );
    }

    async giveUpStarting(detail: string): Promise<void> {
        if (this.gaveUpNotified) {
            this.output.appendLine(detail);
            this.fireStateForActiveEditor();
            return;
        }
        this.gaveUpNotified = true;
        this.output.appendLine(detail);
        this.fireStateForActiveEditor();
        const pick = await vscode.window.showErrorMessage(
            detail,
            "Install / Update CLI",
            "Open Settings",
            "Show Output"
        );
        if (pick === "Install / Update CLI") {
            await vscode.commands.executeCommand("tyhp.installCli");
        } else if (pick === "Open Settings") {
            await vscode.commands.executeCommand("workbench.action.openSettings", "tyhp.path");
        } else if (pick === "Show Output") {
            this.output.show(true);
        }
    }

    onSessionState(_projectFilePath: string, _state: LspClientState): void {
        this.fireStateForActiveEditor();
    }

    private async ensureSession(projectFilePath: string): Promise<void> {
        if (this.disposedFlag) {
            return;
        }
        let session = this.sessions.get(projectFilePath);
        if (!session) {
            session = new LspSession(projectFilePath, this);
            this.sessions.set(projectFilePath, session);
        }
        await session.start();
    }

    private openOwnedProjectFiles(): Set<string> {
        const owners = new Set<string>();
        for (const document of vscode.workspace.textDocuments) {
            if (document.languageId !== "tyhp" || document.uri.scheme !== "file") {
                continue;
            }
            const owner = this.workspace?.ownerOfUri(document.uri);
            if (owner) {
                owners.add(owner.projectFilePath);
            }
        }
        return owners;
    }

    private sessionForActiveEditor(): LspSession | undefined {
        const uri = vscode.window.activeTextEditor?.document.uri;
        if (!uri) {
            return undefined;
        }
        const owner = this.ownerProjectFileOf(uri);
        return owner ? this.sessions.get(owner) : undefined;
    }

    private applyTrace(): void {
        for (const session of this.sessions.values()) {
            session.applyTrace();
        }
        this.output.appendLine(`LSP trace: ${settings.getLanguageServerTrace()}`);
    }

    private fireStateForActiveEditor(): void {
        this.stateEmitter.fire(this.currentState);
    }
}

function sessionLabel(projectFilePath: string): string {
    const posix = projectFilePath.replace(/\\/g, "/");
    const parts = posix.split("/");
    return parts.length >= 2 ? parts[parts.length - 2] : posix;
}

export function registerLspClient(context: vscode.ExtensionContext): LspClient {
    instance = new LspClient();
    context.subscriptions.push(instance);
    return instance;
}

export function getLspClient(): LspClient | undefined {
    return instance;
}
