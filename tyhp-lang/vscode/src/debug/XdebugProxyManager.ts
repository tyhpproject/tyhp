import { spawn, ChildProcess } from "node:child_process";
import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { resolveTyhpBinary } from "../binary/BinaryManager";
import * as settings from "../config/settings";
import { getWorkspaceService } from "../workspace/WorkspaceService";
import {
    ProxyProcessController,
    SpawnedProxy,
    stopSpawnedProxy,
} from "./ProxyProcessController";
import {
    countPhpMapFiles,
    lineWarnsNoSourcemaps,
    parseBoundIdePort,
    ResolvedProxyLaunch,
    buildXdebugProxyArgsFromLaunch,
    resolveProxyLaunch,
} from "./proxyConfig";
import {
    SOURCEMAP_DOCS_URL,
    XDEBUG_PROXY_DOCS_URL,
    proxyStartFailedGuidance,
    sourcemapGuidance,
} from "./proxyGuidance";
import { ProxyRunState, proxyIsListening } from "./proxyLifecycle";
import { parseTyhpJsonProject } from "./tyhpJson";
import { probeTcpPort, sleep } from "./waitForTcp";

const OUTPUT_CHANNEL_NAME = "Tyhp XDebug Proxy";
const LISTEN_TIMEOUT_MS = 15_000;
const LISTEN_ADDRESS = "127.0.0.1";

let instance: XdebugProxyManager | undefined;

export class XdebugProxyManager implements vscode.Disposable {
    private readonly output: vscode.OutputChannel;
    private readonly stateEmitter = new vscode.EventEmitter<ProxyRunState>();
    private readonly disposables: vscode.Disposable[] = [];
    private readonly controller: ProxyProcessController;
    private boundIdePort: number | undefined;
    private bannerIdePort: number | undefined;
    private lastLaunch: ResolvedProxyLaunch | undefined;
    private warnedNoMaps = false;
    private restartTimer: NodeJS.Timeout | undefined;
    private disposed = false;

    readonly onDidChangeState = this.stateEmitter.event;

    constructor() {
        this.output = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);
        this.controller = new ProxyProcessController({
            spawn: (command, args, cwd) => this.spawnProxy(command, args, cwd),
            waitForListening: (port, child, abort) => this.waitUntilListening(port, child, abort),
            stopProcess: (child) => stopSpawnedProxy(child),
            onLog: (line) => this.output.appendLine(line),
            onState: (state) => this.stateEmitter.fire(state),
            onUnexpectedExit: (code) => {
                this.output.appendLine(
                    `XDebug proxy exited unexpectedly${code !== null ? ` (code ${code})` : ""}.`
                );
                void vscode.window.showWarningMessage(
                    "Tyhp XDebug proxy stopped unexpectedly. Check Output > Tyhp XDebug Proxy."
                );
            },
        });
        this.disposables.push(
            this.output,
            this.stateEmitter,
            vscode.commands.registerCommand("tyhp.startXdebugProxy", () => this.start()),
            vscode.commands.registerCommand("tyhp.stopXdebugProxy", () => this.stop()),
            vscode.commands.registerCommand("tyhp.restartXdebugProxy", () => this.restart()),
            vscode.workspace.onDidChangeConfiguration((e) => {
                if (
                    e.affectsConfiguration("tyhp.path") ||
                    e.affectsConfiguration("tyhp.projectPath") ||
                    e.affectsConfiguration("tyhp.xdebugProxy")
                ) {
                    this.scheduleRunningRestart();
                }
            }),
            vscode.workspace.onDidChangeWorkspaceFolders(() => this.scheduleRunningRestart())
        );
        const jsonWatcher = vscode.workspace.createFileSystemWatcher("**/tyhp.json");
        this.disposables.push(
            jsonWatcher,
            jsonWatcher.onDidChange(() => this.scheduleRunningRestart()),
            jsonWatcher.onDidCreate(() => this.scheduleRunningRestart()),
            jsonWatcher.onDidDelete(() => this.scheduleRunningRestart())
        );
    }

    get currentState(): ProxyRunState {
        return this.controller.currentState;
    }

    get isListening(): boolean {
        return proxyIsListening(this.currentState);
    }

    get listeningIdePort(): number | undefined {
        return this.isListening ? this.boundIdePort ?? this.lastLaunch?.idePort : undefined;
    }

    get lastResolvedLaunch(): ResolvedProxyLaunch | undefined {
        return this.lastLaunch;
    }

    resolveLaunch(): ResolvedProxyLaunch {
        const snapshot = getWorkspaceService()?.snapshot;
        const project = readProjectSnapshot(snapshot?.projectFilePath);
        const launch = resolveProxyLaunch(settings.getExplicitProxySettings(), project);
        this.lastLaunch = launch;
        return launch;
    }

    async start(): Promise<boolean> {
        if (this.disposed) {
            return false;
        }
        if (this.currentState === "running" || this.currentState === "starting") {
            return this.currentState === "running";
        }

        const resolved = await resolveTyhpBinary();
        if (resolved.status !== "ok" || !resolved.executablePath) {
            const detail =
                resolved.message ??
                "Tyhp CLI was not found. Use “Tyhp: Install / Update CLI” or set `tyhp.path`.";
            this.output.appendLine(`Cannot start XDebug proxy: ${detail}`);
            const pick = await vscode.window.showErrorMessage(detail, "Install / Update CLI");
            if (pick === "Install / Update CLI") {
                await vscode.commands.executeCommand("tyhp.installCli");
            }
            return false;
        }

        const snapshot = getWorkspaceService()?.snapshot;
        const launch = this.resolveLaunch();
        const args = buildXdebugProxyArgsFromLaunch(launch, snapshot?.projectFilePath);
        const cwd =
            snapshot?.projectDir ?? vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        this.bannerIdePort = undefined;
        this.warnedNoMaps = false;
        this.boundIdePort = launch.idePort > 0 ? launch.idePort : undefined;

        this.output.appendLine(
            `Starting: ${resolved.executablePath} ${args.join(" ")}${cwd ? ` (cwd ${cwd})` : ""}`
        );
        this.warnPrerequisites(launch, snapshot?.projectDir);

        try {
            await this.controller.start({
                command: resolved.executablePath,
                args,
                cwd,
                idePort: launch.idePort,
            });
            this.boundIdePort = this.bannerIdePort && this.bannerIdePort > 0
                ? this.bannerIdePort
                : launch.idePort;
            this.output.appendLine(
                `XDebug proxy is listening (IDE ${this.boundIdePort}, XDebug ${launch.xdebugPort}).`
            );
            if (this.warnedNoMaps) {
                void vscode.window.showWarningMessage(
                    sourcemapGuidance({
                        generateSourcemap: launch.generateSourcemap,
                        mapCount: 0,
                        sourceMapDir: launch.sourceMapDir,
                        outputPath: launch.outputPath,
                    }) ??
                        `No sourcemaps were loaded. Build with generateSourcemap enabled. ${SOURCEMAP_DOCS_URL}`
                );
            }
            return true;
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            void vscode.window.showErrorMessage(proxyStartFailedGuidance(message));
            return false;
        }
    }

    async stop(): Promise<void> {
        try {
            await this.controller.stop();
            this.boundIdePort = undefined;
            this.output.appendLine("XDebug proxy stopped; listening ports released.");
        } catch (err) {
            const message = err instanceof Error ? err.message : String(err);
            void vscode.window.showErrorMessage(
                `Tyhp XDebug proxy did not stop cleanly: ${message}. Ports may still be in use.`
            );
        }
    }

    async restart(): Promise<boolean> {
        await this.stop();
        return this.start();
    }

    dispose(): void {
        this.disposed = true;
        if (this.restartTimer) {
            clearTimeout(this.restartTimer);
            this.restartTimer = undefined;
        }
        void this.controller.stop();
        for (const d of this.disposables) {
            d.dispose();
        }
        this.disposables.length = 0;
    }

    private spawnProxy(command: string, args: readonly string[], cwd?: string): SpawnedProxy {
        const child: ChildProcess = spawn(command, [...args], {
            cwd,
            env: process.env,
            stdio: ["ignore", "pipe", "pipe"],
            windowsHide: true,
        });
        const onChunk = (chunk: Buffer | string) => {
            const text = chunk.toString();
            this.output.append(text);
            for (const line of text.split(/\r?\n/)) {
                if (line.trim() === "") {
                    continue;
                }
                const bound = parseBoundIdePort(line);
                if (bound !== undefined) {
                    this.bannerIdePort = bound;
                }
                if (lineWarnsNoSourcemaps(line)) {
                    this.warnedNoMaps = true;
                }
            }
        };
        child.stdout?.on("data", onChunk);
        child.stderr?.on("data", onChunk);
        return wrapChildProcess(child);
    }

    private async waitUntilListening(
        port: number,
        child: SpawnedProxy,
        abort: AbortSignal
    ): Promise<number> {
        const deadline = Date.now() + LISTEN_TIMEOUT_MS;
        while (!abort.aborted && Date.now() < deadline) {
            if (child.exitCode !== null) {
                throw new Error("XDebug proxy process exited before the IDE port was listening");
            }
            const candidate = this.bannerIdePort && this.bannerIdePort > 0 ? this.bannerIdePort : port;
            if (candidate > 0 && (await probeTcpPort(LISTEN_ADDRESS, candidate, 250))) {
                return candidate;
            }
            await sleep(100);
        }
        if (abort.aborted) {
            throw new Error("XDebug proxy start was cancelled");
        }
        throw new Error(
            `Timed out waiting for the XDebug proxy to listen on ${LISTEN_ADDRESS}:${port}. See Output > Tyhp XDebug Proxy.`
        );
    }

    private warnPrerequisites(launch: ResolvedProxyLaunch, projectDir?: string): void {
        const mapDir = resolveMapDirectory(launch, projectDir);
        const mapCount = mapDir !== undefined ? countMapsOnDisk(mapDir) : undefined;
        const guidance = sourcemapGuidance({
            generateSourcemap: launch.generateSourcemap,
            mapCount,
            sourceMapDir: launch.sourceMapDir ?? mapDir,
            outputPath: launch.outputPath,
        });
        if (guidance) {
            this.output.appendLine(guidance);
            void vscode.window.showWarningMessage(guidance, "Open sourcemap docs").then((pick) => {
                if (pick === "Open sourcemap docs") {
                    void vscode.env.openExternal(vscode.Uri.parse(SOURCEMAP_DOCS_URL));
                }
            });
        }
        this.output.appendLine(
            `Point PHP Debug at IDE port ${launch.idePort}; set XDebug client_port to ${launch.xdebugPort}. ${XDEBUG_PROXY_DOCS_URL}`
        );
    }

    private scheduleRunningRestart(): void {
        if (this.disposed) {
            return;
        }
        if (this.currentState !== "running" && this.currentState !== "starting") {
            return;
        }
        if (this.restartTimer) {
            clearTimeout(this.restartTimer);
        }
        this.restartTimer = setTimeout(() => {
            this.restartTimer = undefined;
            this.output.appendLine("Restarting XDebug proxy (settings or tyhp.json changed).");
            void this.restart();
        }, 400);
    }
}

function readProjectSnapshot(projectFilePath?: string) {
    if (!projectFilePath) {
        return undefined;
    }
    try {
        return parseTyhpJsonProject(fs.readFileSync(projectFilePath, "utf8"));
    } catch {
        return undefined;
    }
}

function resolveMapDirectory(launch: ResolvedProxyLaunch, projectDir?: string): string | undefined {
    const relative = launch.sourceMapDir ?? launch.outputPath ?? "build/";
    if (path.isAbsolute(relative)) {
        return relative;
    }
    if (!projectDir) {
        return undefined;
    }
    return path.join(projectDir, relative);
}

function countMapsOnDisk(dir: string): number | undefined {
    try {
        const entries = fs.readdirSync(dir, { recursive: true, encoding: "utf8" });
        const names = Array.isArray(entries) ? entries.map((entry) => String(entry)) : [];
        return countPhpMapFiles(names);
    } catch {
        return undefined;
    }
}

function wrapChildProcess(child: ChildProcess): SpawnedProxy {
    return {
        pid: child.pid,
        stdout: child.stdout ?? undefined,
        stderr: child.stderr ?? undefined,
        get exitCode() {
            return child.exitCode;
        },
        on(event, listener) {
            child.on(event, listener);
        },
        kill(signal) {
            return child.kill(signal);
        },
    };
}

export function registerXdebugProxy(context: vscode.ExtensionContext): XdebugProxyManager {
    instance = new XdebugProxyManager();
    context.subscriptions.push(instance);
    return instance;
}

export function getXdebugProxy(): XdebugProxyManager | undefined {
    return instance;
}
