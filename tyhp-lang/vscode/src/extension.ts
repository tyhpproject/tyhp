import * as vscode from "vscode";
import {
    registerBinaryManager,
    resolveTyhpBinary,
    getResolvedTyhpBinary,
} from "./binary/BinaryManager";
import { registerDebugConfigProvider } from "./debug/DebugConfigProvider";
import { getXdebugProxy, registerXdebugProxy } from "./debug/XdebugProxyManager";
import { getLspClient, registerLspClient } from "./lsp/LspClient";
import { registerStatusBar } from "./status/StatusBarController";
import { registerTyhpTaskProvider } from "./tasks/TyhpTaskProvider";
import { registerInitCommand } from "./workspace/InitCommand";
import { registerWorkspaceService } from "./workspace/WorkspaceService";

export { resolveTyhpBinary, getResolvedTyhpBinary };
export type { TyhpBinaryResolution } from "./binary/BinaryManager";

/**
 * Extension entry point. Later phases must call {@link resolveTyhpBinary} for the CLI path.
 */
export async function activate(context: vscode.ExtensionContext): Promise<void> {
    try {
        const manager = registerBinaryManager(context);
        const lsp = registerLspClient(context);
        const workspace = registerWorkspaceService(context);
        lsp.bindWorkspace(workspace);
        const init = registerInitCommand(context, workspace, lsp);
        const proxy = registerXdebugProxy(context);
        registerTyhpTaskProvider(context);
        registerDebugConfigProvider(context, proxy);
        registerStatusBar(context, manager, lsp, workspace, proxy);
        context.subscriptions.push(
            vscode.commands.registerCommand("tyhp.refreshBinary", async () => {
                await manager.refresh();
                await lsp.start();
            }),
            vscode.commands.registerCommand("tyhp.installCli", () => manager.installInteractive()),
            vscode.commands.registerCommand("tyhp.revealBinary", () => manager.reveal()),
            vscode.commands.registerCommand("tyhp.restartLanguageServer", () => lsp.restart())
        );
        await manager.initialize();
        await workspace.rebuildIndex();
        await lsp.ensureOpenDocuments();
        init.considerActiveEditor();
    } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        void vscode.window.showErrorMessage(
            `Tyhp extension failed to activate: ${message}. Use “Tyhp: Install / Update CLI” to recover.`
        );
    }
}

export async function deactivate(): Promise<void> {
    await getXdebugProxy()?.stop();
    await getLspClient()?.stop();
}
