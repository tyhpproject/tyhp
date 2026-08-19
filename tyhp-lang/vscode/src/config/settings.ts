import * as vscode from "vscode";
import {
    InspectedSetting,
    InstallMode,
    SettingsScope,
    explicitInspectedValue,
    isPathUnset,
    parseInstallMode,
    pathWriteTarget,
} from "./settingsCore";
import { ExplicitProxySettings } from "../debug/proxyConfig";

const SECTION = "tyhp";

function config(): vscode.WorkspaceConfiguration {
    return vscode.workspace.getConfiguration(SECTION);
}

function toVscodeTarget(scope: SettingsScope): vscode.ConfigurationTarget {
    switch (scope) {
        case SettingsScope.Workspace:
            return vscode.ConfigurationTarget.Workspace;
        case SettingsScope.WorkspaceFolder:
            return vscode.ConfigurationTarget.WorkspaceFolder;
        default:
            return vscode.ConfigurationTarget.Global;
    }
}

export function getTyhpPath(): string {
    return (config().get<string>("path") ?? "").trim();
}

export function inspectTyhpPath(): InspectedSetting<string> | undefined {
    const inspect = config().inspect<string>("path");
    if (!inspect) {
        return undefined;
    }
    return {
        globalValue: inspect.globalValue,
        workspaceValue: inspect.workspaceValue,
        workspaceFolderValue: inspect.workspaceFolderValue,
    };
}

export function tyhpPathIsUnset(): boolean {
    return isPathUnset(getTyhpPath());
}

export function getPathWriteTarget(): SettingsScope {
    return pathWriteTarget(inspectTyhpPath());
}

export async function setTyhpPath(absolutePath: string): Promise<void> {
    const target = toVscodeTarget(getPathWriteTarget());
    await config().update("path", absolutePath, target);
}

export function getInstallMode(): InstallMode {
    return parseInstallMode(config().get<string>("binary.installMode"));
}

export async function setInstallMode(mode: InstallMode): Promise<void> {
    await config().update("binary.installMode", mode, vscode.ConfigurationTarget.Global);
}

export function getAutoUpdate(): boolean {
    return config().get<boolean>("binary.autoUpdate") ?? true;
}

export function getPinnedVersion(): string {
    return (config().get<string>("binary.pinnedVersion") ?? "").trim();
}

export function getProjectPath(): string {
    return (config().get<string>("projectPath") ?? "").trim();
}

export function getLanguageServerArgs(): string[] {
    const args = config().get<string[]>("languageServer.args");
    return Array.isArray(args) ? args : [];
}

export function getLanguageServerTrace(): string {
    return config().get<string>("languageServer.trace") ?? "off";
}

export function getDiagnosticsEnable(): boolean {
    return config().get<boolean>("diagnostics.enable") ?? true;
}

export function getCompletionAutoImport(): boolean {
    return config().get<boolean>("completion.autoImport") ?? true;
}

/**
 * `tyhp.xdebugProxy.*` values the user stored. Unset keys fall through to
 * `tyhp.json` then Story 18 defaults (see `resolveProxyLaunch`).
 */
export function getExplicitProxySettings(): ExplicitProxySettings {
    const idePort = explicitInspectedValue(inspectNumber("xdebugProxy.idePort"));
    const xdebugPort = explicitInspectedValue(inspectNumber("xdebugProxy.xdebugPort"));
    const sourceMapDir = nonEmptyString(
        explicitInspectedValue(inspectString("xdebugProxy.sourceMapDir"))
    );
    const settings: ExplicitProxySettings = {};
    if (typeof idePort === "number") {
        settings.idePort = idePort;
    }
    if (typeof xdebugPort === "number") {
        settings.xdebugPort = xdebugPort;
    }
    if (sourceMapDir) {
        settings.sourceMapDir = sourceMapDir;
    }
    return settings;
}

function inspectNumber(key: string): InspectedSetting<number> | undefined {
    const inspect = config().inspect<number>(key);
    if (!inspect) {
        return undefined;
    }
    return {
        globalValue: inspect.globalValue,
        workspaceValue: inspect.workspaceValue,
        workspaceFolderValue: inspect.workspaceFolderValue,
    };
}

function inspectString(key: string): InspectedSetting<string> | undefined {
    const inspect = config().inspect<string>(key);
    if (!inspect) {
        return undefined;
    }
    return {
        globalValue: inspect.globalValue,
        workspaceValue: inspect.workspaceValue,
        workspaceFolderValue: inspect.workspaceFolderValue,
    };
}

function nonEmptyString(value: string | undefined): string | undefined {
    const trimmed = (value ?? "").trim();
    return trimmed === "" ? undefined : trimmed;
}

export {
    InstallMode,
    SettingsScope,
    isPathUnset,
    parseInstallMode,
    pathWriteTarget,
    inspectTyhpPath as inspectPath,
    explicitInspectedValue,
};
