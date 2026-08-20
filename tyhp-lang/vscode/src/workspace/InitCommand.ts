import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { promisify } from "node:util";
import * as vscode from "vscode";
import { resolveTyhpBinary } from "../binary/BinaryManager";
import { isForcedProjectPath } from "../lsp/projectPath";
import { LspClient } from "../lsp/LspClient";
import * as settings from "../config/settings";
import { buildInitArgs, INIT_DONT_ASK_AGAIN_KEY, shouldPromptInit } from "./initGating";
import { WorkspaceService } from "./WorkspaceService";

const execFileAsync = promisify(execFile);

const INIT_TIMEOUT_MS = 60_000;

/**
 * Command + prompt for `tyhp init` when a Tyhp file is opened without a project.
 */
export class InitCommand implements vscode.Disposable {
    private readonly disposables: vscode.Disposable[] = [];
    private promptedThisSession = false;

    constructor(
        private readonly context: vscode.ExtensionContext,
        private readonly workspace: WorkspaceService,
        private readonly lsp: LspClient
    ) {
        this.disposables.push(
            vscode.commands.registerCommand("tyhp.initProject", () => this.run()),
            vscode.workspace.onDidOpenTextDocument((document) => {
                void this.maybePrompt(document);
            }),
            vscode.window.onDidChangeActiveTextEditor((editor) => {
                if (editor) {
                    void this.maybePrompt(editor.document);
                }
            })
        );
    }

    /**
     * Check the active editor after activation (open-document events may have
     * already fired before we subscribed).
     */
    considerActiveEditor(): void {
        const document = vscode.window.activeTextEditor?.document;
        if (document) {
            void this.maybePrompt(document);
        }
    }

    async run(targetFolder?: vscode.WorkspaceFolder): Promise<void> {
        const folder =
            targetFolder ??
            this.workspace.workspaceFolderFor(vscode.window.activeTextEditor?.document.uri);
        if (!folder) {
            void vscode.window.showErrorMessage(
                "Open a folder in the workspace before running Tyhp: Initialize Project."
            );
            return;
        }

        const existing = join(folder.uri.fsPath, "tyhp.json");
        if (existsSync(existing)) {
            void vscode.window.showInformationMessage(`Tyhp project already exists at ${existing}.`);
            return;
        }

        const resolved = await resolveTyhpBinary();
        if (resolved.status !== "ok" || !resolved.executablePath) {
            const detail =
                resolved.message ??
                "Tyhp CLI was not found. Use “Tyhp: Install / Update CLI” or set `tyhp.path`.";
            const pick = await vscode.window.showErrorMessage(detail, "Install / Update CLI");
            if (pick === "Install / Update CLI") {
                await vscode.commands.executeCommand("tyhp.installCli");
            }
            return;
        }

        const cwd = folder.uri.fsPath;
        const args = buildInitArgs();
        try {
            await vscode.window.withProgress(
                {
                    location: vscode.ProgressLocation.Notification,
                    title: "Running tyhp init",
                    cancellable: false,
                },
                async () => {
                    await execFileAsync(resolved.executablePath as string, args, {
                        cwd,
                        timeout: INIT_TIMEOUT_MS,
                        windowsHide: true,
                    });
                }
            );
        } catch (err) {
            const message = initErrorMessage(err);
            void vscode.window.showErrorMessage(`tyhp init failed: ${message}`);
            return;
        }

        await this.workspace.reloadAfterProjectChange();
        const created = join(cwd, "tyhp.json");
        if (!existsSync(created)) {
            await this.lsp.restart();
            void vscode.window.showWarningMessage(
                `tyhp init finished but tyhp.json was not detected in ${cwd}. Reload the window if the project does not appear.`
            );
            return;
        }
        void vscode.window.showInformationMessage(`Created tyhp.json in ${cwd}.`);
    }

    dispose(): void {
        for (const d of this.disposables) {
            d.dispose();
        }
        this.disposables.length = 0;
    }

    private async maybePrompt(document: vscode.TextDocument): Promise<void> {
        this.workspace.refresh();
        const folder = this.workspace.workspaceFolderFor(document.uri);
        const owner =
            document.uri.scheme === "file" ? this.workspace.ownerOfUri(document.uri) : undefined;
        if (
            !shouldPromptInit({
                languageId: document.languageId,
                hasOwner: Boolean(owner),
                hasAncestorTyhpJson:
                    document.uri.scheme === "file" &&
                    this.workspace.fileHasAncestorTyhpJson(document.uri.fsPath),
                hasForcedProject: isForcedProjectPath(settings.getProjectPath()),
                hasWorkspaceFolder: Boolean(folder),
                dontAskAgain: this.context.workspaceState.get<boolean>(INIT_DONT_ASK_AGAIN_KEY, false),
                promptedThisSession: this.promptedThisSession,
            })
        ) {
            return;
        }

        this.promptedThisSession = true;
        const pick = await vscode.window.showInformationMessage(
            "This file is not in a Tyhp project. Initialize a Tyhp project?",
            "Initialize Project",
            "Not Now",
            "Don't Ask Again"
        );
        if (pick === "Initialize Project") {
            await this.run(folder);
        } else if (pick === "Don't Ask Again") {
            await this.context.workspaceState.update(INIT_DONT_ASK_AGAIN_KEY, true);
        }
    }
}

function initErrorMessage(err: unknown): string {
    if (err && typeof err === "object") {
        const execErr = err as { stderr?: string; stdout?: string; message?: string };
        const stderr = (execErr.stderr ?? "").trim();
        if (stderr !== "") {
            return stderr;
        }
        const stdout = (execErr.stdout ?? "").trim();
        if (stdout !== "") {
            return stdout;
        }
        if (typeof execErr.message === "string" && execErr.message !== "") {
            return execErr.message;
        }
    }
    return err instanceof Error ? err.message : String(err);
}

export function registerInitCommand(
    context: vscode.ExtensionContext,
    workspace: WorkspaceService,
    lsp: LspClient
): InitCommand {
    const command = new InitCommand(context, workspace, lsp);
    context.subscriptions.push(command);
    return command;
}
