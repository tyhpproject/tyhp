import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import * as settings from "../config/settings";
import { getLspClient } from "../lsp/LspClient";
import { isForcedProjectPath, ResolveProjectFileOptions, resolveTyhpProjectFile, TYHP_PROJECT_FILE } from "../lsp/projectPath";
import { hasAncestorTyhpJson, matchingWorkspaceRoot, shouldSkipIndexedTyhpJson } from "./pathUtils";
import { IndexedProject, ProjectIndex, indexedProjectFromJson, snapshotFromOwner } from "./projectIndex";
import { WorkspaceSnapshot } from "./projectDetection";

let instance: WorkspaceService | undefined;

const EMPTY_SNAPSHOT: WorkspaceSnapshot = snapshotFromOwner(undefined);

const INDEX_EXCLUDE =
    "{**/node_modules/**,**/vendor/**,**/.git/**,**/bin/**,**/obj/**,**/dist/**,**/build/**}";

/**
 * Indexes every `tyhp.json` under the workspace (or the single forced
 * `tyhp.projectPath`), matches files by include/exclude, and exposes the
 * active editor's owner to tasks / status bar / init / LSP.
 */
export class WorkspaceService implements vscode.Disposable {
    private readonly snapshotEmitter = new vscode.EventEmitter<WorkspaceSnapshot>();
    private readonly disposables: vscode.Disposable[] = [];
    private current: WorkspaceSnapshot = EMPTY_SNAPSHOT;
    private index: ProjectIndex = new ProjectIndex([], process.platform === "win32");
    private rebuildInFlight: Promise<ProjectIndex> | undefined;

    readonly onDidChange = this.snapshotEmitter.event;

    constructor() {
        const watcher = vscode.workspace.createFileSystemWatcher(`**/${TYHP_PROJECT_FILE}`);
        this.disposables.push(
            this.snapshotEmitter,
            watcher,
            watcher.onDidCreate(() => this.handleProjectFileEvent()),
            watcher.onDidChange((uri) => this.handleProjectFileChanged(uri)),
            watcher.onDidDelete(() => this.handleProjectFileEvent()),
            vscode.workspace.onDidChangeWorkspaceFolders(() => {
                void this.rebuildIndex();
            }),
            vscode.workspace.onDidChangeConfiguration((e) => {
                if (e.affectsConfiguration("tyhp.projectPath")) {
                    void this.rebuildIndex();
                }
            }),
            vscode.window.onDidChangeActiveTextEditor(() => {
                this.updateSnapshotFromActiveEditor();
            })
        );
    }

    get snapshot(): WorkspaceSnapshot {
        return this.current;
    }

    get projectIndex(): ProjectIndex {
        return this.index;
    }

    ownerOfUri(uri: vscode.Uri): IndexedProject | undefined {
        if (uri.scheme !== "file") {
            return undefined;
        }
        return this.index.ownerOf(uri.fsPath);
    }

    ownerOfPath(filePath: string): IndexedProject | undefined {
        return this.index.ownerOf(filePath);
    }

    fileHasAncestorTyhpJson(filePath: string): boolean {
        const folders = vscode.workspace.workspaceFolders ?? [];
        const roots = folders.map((folder) => folder.uri.fsPath);
        const root = matchingWorkspaceRoot(filePath, roots, process.platform === "win32");
        return hasAncestorTyhpJson(filePath, root, (candidate) => fs.existsSync(candidate), path.join);
    }

    isForcedProject(): boolean {
        return isForcedProjectPath(settings.getProjectPath());
    }

    refresh(): WorkspaceSnapshot {
        this.updateSnapshotFromActiveEditor();
        return this.current;
    }

    async rebuildIndex(): Promise<ProjectIndex> {
        if (this.rebuildInFlight) {
            return this.rebuildInFlight;
        }
        this.rebuildInFlight = this.rebuildIndexImpl().finally(() => {
            this.rebuildInFlight = undefined;
        });
        return this.rebuildInFlight;
    }

    /**
     * Re-index after `tyhp init` or a `tyhp.json` change, then lazy-start LSP
     * for open documents.
     */
    async reloadAfterProjectChange(): Promise<WorkspaceSnapshot> {
        await this.rebuildIndex();
        await getLspClient()?.ensureOpenDocuments();
        return this.current;
    }

    /**
     * Folder that should receive `tyhp init` (and task cwd fallback).
     * Prefers the workspace folder containing `uri`, else the first folder.
     */
    workspaceFolderFor(uri?: vscode.Uri): vscode.WorkspaceFolder | undefined {
        if (uri) {
            const match = vscode.workspace.getWorkspaceFolder(uri);
            if (match) {
                return match;
            }
        }
        return vscode.workspace.workspaceFolders?.[0];
    }

    dispose(): void {
        for (const d of this.disposables) {
            d.dispose();
        }
        this.disposables.length = 0;
    }

    private async rebuildIndexImpl(): Promise<ProjectIndex> {
        const caseInsensitive = process.platform === "win32";
        const projects: IndexedProject[] = [];
        const options = vscodeProjectPathOptions();
        if (isForcedProjectPath(options.configuredPath)) {
            const forced = resolveTyhpProjectFile(options);
            if (forced) {
                projects.push(readIndexedProject(forced));
            }
        } else {
            const uris = await vscode.workspace.findFiles(`**/${TYHP_PROJECT_FILE}`, INDEX_EXCLUDE);
            for (const uri of uris) {
                if (shouldSkipIndexedTyhpJson(uri.fsPath)) {
                    continue;
                }
                projects.push(readIndexedProject(uri.fsPath));
            }
        }
        this.index = new ProjectIndex(projects, caseInsensitive);
        this.updateSnapshotFromActiveEditor();
        return this.index;
    }

    private updateSnapshotFromActiveEditor(): void {
        const document = vscode.window.activeTextEditor?.document;
        const owner =
            document && document.languageId === "tyhp" && document.uri.scheme === "file"
                ? this.ownerOfUri(document.uri)
                : undefined;
        const next = snapshotFromOwner(owner);
        const changed =
            next.projectFilePath !== this.current.projectFilePath ||
            next.projectName !== this.current.projectName;
        this.current = next;
        if (changed) {
            this.snapshotEmitter.fire(next);
        }
    }

    private handleProjectFileEvent(): void {
        void this.reloadAfterProjectChange();
    }

    private handleProjectFileChanged(uri: vscode.Uri): void {
        void (async () => {
            await this.rebuildIndex();
            await getLspClient()?.restartSession(uri.fsPath);
            await getLspClient()?.ensureOpenDocuments();
        })();
    }
}

function readIndexedProject(projectFilePath: string): IndexedProject {
    try {
        const raw = fs.readFileSync(projectFilePath, "utf8");
        return indexedProjectFromJson(projectFilePath, raw);
    } catch {
        return indexedProjectFromJson(projectFilePath, "{");
    }
}

export function vscodeProjectPathOptions(): ResolveProjectFileOptions {
    const folders = vscode.workspace.workspaceFolders ?? [];
    return {
        configuredPath: settings.getProjectPath(),
        workspaceRoots: folders.map((folder) => folder.uri.fsPath),
        join: path.join,
        resolve: path.resolve,
        isAbsolute: path.isAbsolute,
        fs: {
            existsSync: (target) => fs.existsSync(target),
            isDirectory: (target) => {
                try {
                    return fs.statSync(target).isDirectory();
                } catch {
                    return false;
                }
            },
        },
    };
}

export function registerWorkspaceService(context: vscode.ExtensionContext): WorkspaceService {
    instance = new WorkspaceService();
    context.subscriptions.push(instance);
    return instance;
}

export function getWorkspaceService(): WorkspaceService | undefined {
    return instance;
}
