import * as fs from "fs";
import * as vscode from "vscode";
import { InstallMode, normalizeReleaseTag, tagsMatch } from "../config/settingsCore";
import * as settings from "../config/settings";
import { Installer, fetchLatestRelease } from "./Installer";
import { deleteInstallMetadata, isExtensionOwnedInstall, readInstallMetadata } from "./metadata";
import { probeTyhpOnPath, validateTyhpPath } from "./PathProbe";
import { extensionInstallDir, isManagedInstallPath } from "./platform";
import { UpdateService } from "./UpdateService";

export type BinarySource = "setting" | "path" | "none";

/**
 * Result of locating the Tyhp CLI. Phase 4+ (LSP, tasks, XDebug proxy) must call
 * {@link resolveTyhpBinary} rather than reading `tyhp.path` on their own.
 */
export interface TyhpBinaryResolution {
    status: "ok" | "missing" | "invalid";
    /** Absolute filesystem path when `status` is `"ok"`. */
    executablePath?: string;
    message?: string;
    source: BinarySource;
    installMode: InstallMode;
}

const LAST_UPDATE_CHECK_KEY = "tyhp.binary.lastUpdateCheck";
const STARTUP_UPDATE_DELAY_MS = 5_000;
const UPDATE_CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000;

let instance: BinaryManager | undefined;

export class BinaryManager implements vscode.Disposable {
    private readonly output: vscode.OutputChannel;
    private readonly resolutionEmitter = new vscode.EventEmitter<TyhpBinaryResolution>();
    private readonly installer: Installer;
    private readonly updates: UpdateService;
    private readonly cliDir: string;
    private resolution: TyhpBinaryResolution;
    private offeredFix = false;
    private readonly disposables: vscode.Disposable[] = [];

    readonly onDidChangeResolution = this.resolutionEmitter.event;

    constructor(private readonly context: vscode.ExtensionContext) {
        this.output = vscode.window.createOutputChannel("Tyhp");
        this.cliDir = extensionInstallDir(context.globalStorageUri.fsPath);
        this.installer = new Installer(context.globalStorageUri.fsPath, this.output);
        this.updates = new UpdateService(this.installer);
        this.resolution = missingResolution("Tyhp CLI has not been resolved yet.");
        this.disposables.push(
            this.output,
            this.resolutionEmitter,
            vscode.workspace.onDidChangeConfiguration((e) => {
                if (
                    e.affectsConfiguration("tyhp.path") ||
                    e.affectsConfiguration("tyhp.binary")
                ) {
                    void this.refreshResolution();
                }
            })
        );
    }

    get lastResolution(): TyhpBinaryResolution {
        return this.resolution;
    }

    async initialize(): Promise<void> {
        try {
            await this.probeAndPopulatePath();
            await this.refreshResolution();
            this.scheduleAutoUpdate();
        } catch (err) {
            const message = errorMessage(err);
            this.output.appendLine(`Activation error: ${message}`);
            this.setResolution(missingResolution(message));
        }
    }

    /**
     * Re-run PATH discovery when `tyhp.path` is empty, then validate. Never throws
     * (see {@link refreshResolution}); the "Refresh Tyhp binary" command surfaces
     * failures via the status bar instead of an unhandled rejection.
     */
    async refresh(): Promise<TyhpBinaryResolution> {
        try {
            await this.probeAndPopulatePath();
        } catch (err) {
            const message = errorMessage(err);
            this.output.appendLine(`PATH probe error: ${message}`);
        }
        return this.refreshResolution();
    }

    async resolve(): Promise<TyhpBinaryResolution> {
        return this.refreshResolution();
    }

    async installInteractive(): Promise<void> {
        const mode = await vscode.window.showQuickPick(
            [
                {
                    label: "Global",
                    description: "Install into a user-global location",
                    detail: "Never auto-updated. Updates only when you run this command. Sets `tyhp.path`.",
                    mode: "global" as const,
                },
                {
                    label: "Extension only",
                    description: "Install under this extension’s storage",
                    detail: "Auto-update and `tyhp.binary.pinnedVersion` apply. Sets `tyhp.path` to that binary.",
                    mode: "extension" as const,
                },
            ],
            {
                title: "Tyhp: Install / Update CLI",
                placeHolder: "Choose where to install the Tyhp compiler CLI",
                ignoreFocusOut: true,
            }
        );
        if (!mode) {
            return;
        }

        try {
            await vscode.window.withProgress(
                {
                    location: vscode.ProgressLocation.Notification,
                    title: "Installing Tyhp CLI",
                    cancellable: false,
                },
                async () => {
                    const pin = settings.getPinnedVersion();
                    const tag = pin !== "" ? normalizeReleaseTag(pin) : (await fetchLatestRelease()).tag_name;
                    this.output.appendLine(`Install / Update (${mode.mode}) → ${tag}`);
                    const result = await this.installer.install(mode.mode, tag);
                    await settings.setTyhpPath(result.executablePath);
                    await settings.setInstallMode(mode.mode);
                    if (mode.mode === "global") {
                        deleteInstallMetadata(this.cliDir);
                    }
                    await this.refreshResolution();
                    const pathNote =
                        mode.mode === "global"
                            ? ` If \`tyhp\` is not on PATH, add the install directory or keep using \`tyhp.path\`.`
                            : "";
                    void vscode.window.showInformationMessage(
                        `Installed Tyhp CLI ${result.version} at ${result.executablePath}.${pathNote}`
                    );
                }
            );
        } catch (err) {
            const message = errorMessage(err);
            this.output.appendLine(`Install failed: ${message}`);
            const pick = await vscode.window.showErrorMessage(`Tyhp CLI install failed: ${message}`, "Show Output");
            if (pick === "Show Output") {
                this.output.show(true);
            }
        }
    }

    async reveal(): Promise<void> {
        const resolved = this.resolution;
        if (resolved.status === "ok" && resolved.executablePath) {
            void vscode.window.showInformationMessage(`Tyhp CLI: ${resolved.executablePath}`);
            return;
        }
        const pick = await vscode.window.showErrorMessage(
            resolved.message ?? "Tyhp CLI is not available.",
            "Install / Update CLI"
        );
        if (pick === "Install / Update CLI") {
            await this.installInteractive();
        }
    }

    private async probeAndPopulatePath(): Promise<void> {
        if (!settings.tyhpPathIsUnset()) {
            return;
        }
        const found = probeTyhpOnPath();
        if (!found) {
            return;
        }
        let absolute = found;
        try {
            absolute = fs.realpathSync(found);
        } catch {
            absolute = found;
        }
        this.output.appendLine(`PATH probe found tyhp at ${absolute}; writing tyhp.path`);
        await settings.setTyhpPath(absolute);
        await settings.setInstallMode("path");
        deleteInstallMetadata(this.cliDir);
    }

    /**
     * Never throws: {@link resolveTyhpBinary} is the single resolve API later phases
     * (LSP, tasks, XDebug proxy) call, and the `tyhp.path` / `tyhp.binary.*` config-change
     * listener also awaits this directly. An unsupported platform, a transient fs error,
     * or any other unexpected failure must degrade to a `"missing"` resolution + status bar
     * error, not an unhandled rejection that later phases (or VS Code itself) would have to
     * guard against individually.
     */
    private async refreshResolution(): Promise<TyhpBinaryResolution> {
        try {
            return await this.doRefreshResolution();
        } catch (err) {
            const message = errorMessage(err);
            this.output.appendLine(`Resolution error: ${message}`);
            this.setResolution(missingResolution(message));
            await this.offerFix(message);
            return this.resolution;
        }
    }

    private async doRefreshResolution(): Promise<TyhpBinaryResolution> {
        const configured = settings.getTyhpPath();
        const installMode = settings.getInstallMode();

        if (configured !== "") {
            const check = validateTyhpPath(configured);
            if (check.ok && check.absolutePath) {
                this.setResolution({
                    status: "ok",
                    executablePath: check.absolutePath,
                    source: "setting",
                    installMode,
                });
                return this.resolution;
            }
            const message =
                check.message ??
                `Tyhp CLI at \`${configured}\` is missing or is not a file. Use “Tyhp: Install / Update CLI” or fix \`tyhp.path\`.`;
            this.setResolution({
                status: "invalid",
                executablePath: check.absolutePath,
                message,
                source: "setting",
                installMode,
            });
            await this.offerFix(message);
            return this.resolution;
        }

        const probed = probeTyhpOnPath();
        if (probed) {
            this.setResolution({
                status: "ok",
                executablePath: probed,
                source: "path",
                installMode,
            });
            return this.resolution;
        }

        const message =
            "Tyhp CLI was not found. Use “Tyhp: Install / Update CLI” or set `tyhp.path` to an existing binary.";
        this.setResolution(missingResolution(message, installMode));
        await this.offerFix(message);
        return this.resolution;
    }

    private async offerFix(message: string): Promise<void> {
        if (this.offeredFix) {
            return;
        }
        this.offeredFix = true;
        const pick = await vscode.window.showErrorMessage(message, "Install / Update CLI", "Show Output");
        if (pick === "Install / Update CLI") {
            await this.installInteractive();
        } else if (pick === "Show Output") {
            this.output.show(true);
        }
    }

    private scheduleAutoUpdate(): void {
        const handle = setTimeout(() => {
            void this.runAutoUpdate();
        }, STARTUP_UPDATE_DELAY_MS);
        this.disposables.push({ dispose: () => clearTimeout(handle) });
    }

    private async runAutoUpdate(): Promise<void> {
        const installMode = settings.getInstallMode();
        const metadata = readInstallMetadata(this.cliDir);

        // `tyhp.binary.installMode` can drift from `tyhp.path` when a user hand-edits
        // settings.json (e.g. points `tyhp.path` at a custom build) without going through
        // "Tyhp: Install / Update CLI" or PATH re-probe, either of which would reset
        // `installMode`/metadata. Guard against auto-updating (and overwriting `tyhp.path`
        // with) the extension-managed binary in that case: "setting wins" must hold even
        // when the stale `installMode` still says `extension`.
        if (
            installMode === "extension" &&
            !isManagedInstallPath(settings.getTyhpPath(), this.context.globalStorageUri.fsPath)
        ) {
            this.output.appendLine(
                "Skipping auto-update: tyhp.path no longer points at the extension-managed install."
            );
            return;
        }

        const pin = settings.getPinnedVersion();
        const pinNeedsApply =
            pin !== "" &&
            installMode === "extension" &&
            isExtensionOwnedInstall(metadata) &&
            !tagsMatch(metadata?.version ?? "", pin);

        if (!pinNeedsApply) {
            const last = this.context.globalState.get<number>(LAST_UPDATE_CHECK_KEY, 0);
            if (Date.now() - last < UPDATE_CHECK_INTERVAL_MS) {
                this.output.appendLine("Skipping auto-update check (debounced).");
                return;
            }
        }

        try {
            const result = await this.updates.checkAndApply(
                {
                    installMode,
                    autoUpdate: settings.getAutoUpdate(),
                    pinnedVersion: pin,
                },
                metadata,
                this.output
            );
            await this.context.globalState.update(LAST_UPDATE_CHECK_KEY, Date.now());
            if (result.updated && result.path) {
                await settings.setTyhpPath(result.path);
                await this.refreshResolution();
                void vscode.window.showInformationMessage(`Updated Tyhp CLI (${result.reason}).`);
            }
        } catch (err) {
            const message = errorMessage(err);
            this.output.appendLine(`Auto-update failed: ${message}`);
        }
    }

    private setResolution(resolution: TyhpBinaryResolution): void {
        this.resolution = resolution;
        this.resolutionEmitter.fire(resolution);
    }

    dispose(): void {
        for (const d of this.disposables) {
            d.dispose();
        }
        this.disposables.length = 0;
    }
}

function missingResolution(message: string, installMode: InstallMode = settings.getInstallMode()): TyhpBinaryResolution {
    return {
        status: "missing",
        message,
        source: "none",
        installMode,
    };
}

function errorMessage(err: unknown): string {
    return err instanceof Error ? err.message : String(err);
}

export function registerBinaryManager(context: vscode.ExtensionContext): BinaryManager {
    instance = new BinaryManager(context);
    context.subscriptions.push(instance);
    return instance;
}

/**
 * Resolve the Tyhp CLI executable. Later phases (language server, tasks,
 * XDebug proxy) should call this instead of reading `tyhp.path` directly.
 */
export async function resolveTyhpBinary(): Promise<TyhpBinaryResolution> {
    if (!instance) {
        return {
            status: "missing",
            source: "none",
            installMode: "path",
            message: "Tyhp binary manager is not active.",
        };
    }
    return instance.resolve();
}

/** Last cached resolution without hitting the filesystem again. */
export function getResolvedTyhpBinary(): TyhpBinaryResolution {
    if (!instance) {
        return {
            status: "missing",
            source: "none",
            installMode: "path",
            message: "Tyhp binary manager is not active.",
        };
    }
    return instance.lastResolution;
}

export function getBinaryManager(): BinaryManager | undefined {
    return instance;
}
