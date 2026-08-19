/**
 * Pure helpers for `tyhp.*` settings. Keep vscode imports out of this file
 * so unit tests can run under `node --test` without the extension host.
 */

export type InstallMode = "path" | "global" | "extension";

export const INSTALL_MODES: readonly InstallMode[] = ["path", "global", "extension"];

/** Mirrors `vscode.ConfigurationTarget` so tests do not import `vscode`. */
export enum SettingsScope {
    Global = 1,
    Workspace = 2,
    WorkspaceFolder = 3,
}

export interface InspectedSetting<T> {
    globalValue?: T;
    workspaceValue?: T;
    workspaceFolderValue?: T;
}

export function isPathUnset(value: string | undefined | null): boolean {
    return value === undefined || value === null || value.trim() === "";
}

export function parseInstallMode(value: unknown): InstallMode {
    if (value === "global" || value === "extension" || value === "path") {
        return value;
    }
    return "path";
}

/**
 * Where to persist `tyhp.path` after a PATH probe or install.
 * User (Global) by default unless a workspace / folder override already exists.
 */
export function pathWriteTarget(inspect: InspectedSetting<string> | undefined): SettingsScope {
    if (!inspect) {
        return SettingsScope.Global;
    }
    if (inspect.workspaceFolderValue !== undefined) {
        return SettingsScope.WorkspaceFolder;
    }
    if (inspect.workspaceValue !== undefined) {
        return SettingsScope.Workspace;
    }
    return SettingsScope.Global;
}

export function normalizePinnedVersion(value: string | undefined | null): string {
    return (value ?? "").trim();
}

/** GitHub tags are `vX.Y.Z…`; settings may omit the `v`. */
export function normalizeReleaseTag(value: string): string {
    const trimmed = value.trim();
    if (trimmed === "") {
        return "";
    }
    return trimmed.startsWith("v") || trimmed.startsWith("V") ? trimmed : `v${trimmed}`;
}

export function tagsMatch(a: string, b: string): boolean {
    const left = normalizeReleaseTag(a);
    const right = normalizeReleaseTag(b);
    if (left === "" || right === "") {
        return false;
    }
    return left.toLowerCase() === right.toLowerCase();
}

/**
 * Value the user actually stored (folder → workspace → user). Package.json
 * defaults are not stored, so this is `undefined` when the setting is unset.
 */
export function explicitInspectedValue<T>(inspect: InspectedSetting<T> | undefined): T | undefined {
    if (!inspect) {
        return undefined;
    }
    if (inspect.workspaceFolderValue !== undefined) {
        return inspect.workspaceFolderValue;
    }
    if (inspect.workspaceValue !== undefined) {
        return inspect.workspaceValue;
    }
    if (inspect.globalValue !== undefined) {
        return inspect.globalValue;
    }
    return undefined;
}
