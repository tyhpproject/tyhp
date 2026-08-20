import * as vscode from "vscode";
import { BinaryManager } from "../binary/BinaryManager";
import { XdebugProxyManager } from "../debug/XdebugProxyManager";
import { LspClient } from "../lsp/LspClient";
import { projectStatusLabel } from "../workspace/projectDetection";
import { WorkspaceService } from "../workspace/WorkspaceService";
import { formatStatusBar, proxyStatusActions } from "./statusBarModel";

/**
 * Compact Tyhp status bar: project + LSP + binary + XDebug proxy. Click opens
 * a quick pick of everyday actions including start/stop/restart proxy.
 */
export class StatusBarController implements vscode.Disposable {
    private readonly item: vscode.StatusBarItem;
    private readonly disposables: vscode.Disposable[] = [];

    constructor(
        private readonly manager: BinaryManager,
        private readonly lsp: LspClient,
        private readonly workspace: WorkspaceService,
        private readonly proxy: XdebugProxyManager
    ) {
        this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 80);
        this.item.name = "Tyhp";
        this.item.command = "tyhp.showStatusActions";
        this.item.show();
        this.disposables.push(
            this.item,
            vscode.commands.registerCommand("tyhp.showStatusActions", () => this.showQuickPick()),
            this.manager.onDidChangeResolution(() => this.refresh()),
            this.lsp.onDidChangeClientState(() => this.refresh()),
            this.workspace.onDidChange(() => this.refresh()),
            this.proxy.onDidChangeState(() => this.refresh())
        );
        this.refresh();
    }

    refresh(): void {
        const binary = this.manager.lastResolution;
        const launch = this.proxy.lastResolvedLaunch;
        const ide = this.proxy.listeningIdePort ?? launch?.idePort;
        const xdebug = launch?.xdebugPort;
        const proxyDetail =
            ide !== undefined && xdebug !== undefined ? `IDE ${ide} / XDebug ${xdebug}` : undefined;
        const view = formatStatusBar({
            projectLabel: projectStatusLabel(this.workspace.snapshot),
            lspState: this.lsp.currentState,
            binaryStatus: binary.status,
            binaryPath: binary.executablePath,
            binaryMessage: binary.message,
            proxyState: this.proxy.currentState,
            proxyDetail,
        });
        this.item.text = view.text;
        this.item.tooltip = view.tooltip;
        if (view.error) {
            this.item.backgroundColor = new vscode.ThemeColor("statusBarItem.errorBackground");
        } else if (view.warning) {
            this.item.backgroundColor = new vscode.ThemeColor("statusBarItem.warningBackground");
        } else {
            this.item.backgroundColor = undefined;
        }
    }

    dispose(): void {
        for (const d of this.disposables) {
            d.dispose();
        }
        this.disposables.length = 0;
    }

    private async showQuickPick(): Promise<void> {
        const hasProject = Boolean(this.workspace.snapshot.projectFilePath);
        const proxyState = this.proxy.currentState;
        const items: Array<vscode.QuickPickItem & { command: string }> = [
            {
                label: "$(debug-restart) Restart Language Server",
                description: "tyhp.restartLanguageServer",
                command: "tyhp.restartLanguageServer",
            },
            {
                label: "$(cloud-download) Install / Update CLI",
                description: "tyhp.installCli",
                command: "tyhp.installCli",
            },
            {
                label: "$(file-binary) Reveal CLI Path",
                description: "tyhp.revealBinary",
                command: "tyhp.revealBinary",
            },
        ];
        if (!hasProject) {
            items.splice(2, 0, {
                label: "$(new-file) Initialize Project",
                description: "tyhp.initProject",
                command: "tyhp.initProject",
            });
        }
        items.push(...proxyQuickPickItems(proxyState));
        const pick = await vscode.window.showQuickPick(items, {
            title: "Tyhp",
            placeHolder: "Choose a Tyhp action",
        });
        if (pick) {
            await vscode.commands.executeCommand(pick.command);
        }
    }
}

const PROXY_ITEM: Record<"start" | "stop" | "restart", vscode.QuickPickItem & { command: string }> = {
    start: {
        label: "$(debug-start) Start XDebug Proxy",
        description: "tyhp.startXdebugProxy",
        command: "tyhp.startXdebugProxy",
    },
    stop: {
        label: "$(debug-stop) Stop XDebug Proxy",
        description: "tyhp.stopXdebugProxy",
        command: "tyhp.stopXdebugProxy",
    },
    restart: {
        label: "$(debug-restart) Restart XDebug Proxy",
        description: "tyhp.restartXdebugProxy",
        command: "tyhp.restartXdebugProxy",
    },
};

function proxyQuickPickItems(proxyState: string): Array<vscode.QuickPickItem & { command: string }> {
    const state =
        proxyState === "starting" ||
        proxyState === "running" ||
        proxyState === "stopping" ||
        proxyState === "error"
            ? proxyState
            : "stopped";
    return proxyStatusActions(state).map((action) => PROXY_ITEM[action]);
}

export function registerStatusBar(
    context: vscode.ExtensionContext,
    manager: BinaryManager,
    lsp: LspClient,
    workspace: WorkspaceService,
    proxy: XdebugProxyManager
): StatusBarController {
    const controller = new StatusBarController(manager, lsp, workspace, proxy);
    context.subscriptions.push(controller);
    return controller;
}
