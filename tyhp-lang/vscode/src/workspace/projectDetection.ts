import { isForcedProjectPath, resolveTyhpProjectFile, ResolveProjectFileOptions, TYHP_PROJECT_FILE } from "../lsp/projectPath";
import { ProjectIndexSnapshot, snapshotFromOwner } from "./projectIndex";

export { TYHP_PROJECT_FILE };
export type WorkspaceSnapshot = ProjectIndexSnapshot;

export interface SnapshotPathApi {
    dirname(target: string): string;
    basename(target: string): string;
}

/**
 * Forced `tyhp.projectPath` only. Workspace scanning / include matching lives
 * in {@link ProjectIndex}.
 */
export function detectForcedProject(
    options: ResolveProjectFileOptions,
    paths: SnapshotPathApi
): WorkspaceSnapshot {
    const projectFilePath = resolveTyhpProjectFile(options);
    return snapshotFromProjectFile(projectFilePath, paths);
}

/** @deprecated Use detectForcedProject; workspace-root fallback is gone. */
export function detectWorkspaceProject(
    options: ResolveProjectFileOptions,
    paths: SnapshotPathApi
): WorkspaceSnapshot {
    return detectForcedProject(options, paths);
}

export function snapshotFromProjectFile(
    projectFilePath: string | undefined,
    paths: SnapshotPathApi
): WorkspaceSnapshot {
    if (!projectFilePath) {
        return snapshotFromOwner(undefined);
    }
    const projectDir = paths.dirname(projectFilePath);
    return {
        projectFilePath,
        projectDir,
        projectName: paths.basename(projectDir),
    };
}

export function projectStatusLabel(snapshot: WorkspaceSnapshot): string {
    const name = snapshot.projectName?.trim() ?? "";
    return name !== "" ? name : "not in a Tyhp project";
}

export function isMissingProjectLabel(label: string): boolean {
    const trimmed = label.trim();
    return trimmed === "" || trimmed === "not in a Tyhp project" || trimmed === "no project";
}

export { isForcedProjectPath };
