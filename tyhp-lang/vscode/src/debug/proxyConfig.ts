/**
 * Argv and port/sourcemap resolution for `tyhp xdebug_proxy`.
 *
 * Flags match `DisplayHelp.XDebugProxyHelp` / `docs/content/cli_xdebugProxy.md`:
 * `--ide-port`, `--xdebug-port`, `--sourcemap-dir`, `--ide-key`, plus global
 * `--tyhp-project`. Do not invent extra switches.
 */

export const XDEBUG_PROXY_ACTION = "xdebug_proxy";

/** Story 18 / CLI default for the IDE (DBGp client) listen port. */
export const DEFAULT_IDE_PORT = 9003;

/** Story 18 / CLI default for the XDebug engine listen port. */
export const DEFAULT_XDEBUG_PORT = 9004;

export type ConfigValueSource = "settings" | "tyhp.json" | "default" | "omitted";

/**
 * VS Code `tyhp.xdebugProxy.*` values that the user actually stored
 * (not package.json defaults). Empty / unset fields mean “not explicit”.
 */
export interface ExplicitProxySettings {
    idePort?: number;
    xdebugPort?: number;
    sourceMapDir?: string;
    ideKey?: string;
}

export interface TyhpJsonProxySection {
    idePort?: number;
    xdebugPort?: number;
    sourceMapDir?: string;
    ideKey?: string;
}

export interface TyhpJsonProjectSnapshot {
    xdebugProxy?: TyhpJsonProxySection;
    generateSourcemap: boolean;
    outputPath?: string;
}

export interface ResolvedProxyLaunch {
    idePort: number;
    xdebugPort: number;
    sourceMapDir?: string;
    ideKey?: string;
    generateSourcemap: boolean;
    outputPath?: string;
    idePortSource: ConfigValueSource;
    xdebugPortSource: ConfigValueSource;
    sourceMapDirSource: ConfigValueSource;
    ideKeySource: ConfigValueSource;
}

export interface XdebugProxyArgOptions {
    projectFilePath?: string;
    idePort?: number;
    xdebugPort?: number;
    sourceMapDir?: string;
    ideKey?: string;
}

/**
 * Settings win when explicitly set; otherwise `tyhp.json` `xdebugProxy`;
 * otherwise Story 18 defaults (9003 / 9004). `sourceMapDir` / `ideKey` are
 * omitted from argv when neither source provides them so the CLI can use
 * `output.path` / accept any idekey.
 */
export function resolveProxyLaunch(
    settings: ExplicitProxySettings,
    project?: TyhpJsonProjectSnapshot
): ResolvedProxyLaunch {
    const json = project?.xdebugProxy;
    const ide = pickPort(settings.idePort, json?.idePort, DEFAULT_IDE_PORT);
    const xdebug = pickPort(settings.xdebugPort, json?.xdebugPort, DEFAULT_XDEBUG_PORT);
    const sourceMapDir = pickOptionalString(settings.sourceMapDir, json?.sourceMapDir);
    const ideKey = pickOptionalString(settings.ideKey, json?.ideKey);

    return {
        idePort: ide.value,
        xdebugPort: xdebug.value,
        sourceMapDir: sourceMapDir.value,
        ideKey: ideKey.value,
        generateSourcemap: project?.generateSourcemap === true,
        outputPath: nonEmpty(project?.outputPath),
        idePortSource: ide.source,
        xdebugPortSource: xdebug.source,
        sourceMapDirSource: sourceMapDir.source,
        ideKeySource: ideKey.source,
    };
}

/**
 * Returns argv for `tyhp xdebug_proxy` (not including the executable).
 *
 * Exact shape:
 * `xdebug_proxy [--tyhp-project=<file>] [--ide-port=<n>] [--xdebug-port=<n>]
 * [--sourcemap-dir=<path>] [--ide-key=<key>]`
 *
 * Ports are always passed once resolved so the process matches launch.json.
 * Sourcemap dir and ide-key are passed only when known.
 */
export function buildXdebugProxyArgs(options: XdebugProxyArgOptions): string[] {
    const args: string[] = [XDEBUG_PROXY_ACTION];
    const project = options.projectFilePath?.trim() ?? "";
    if (project !== "") {
        args.push(`--tyhp-project=${project}`);
    }
    if (options.idePort !== undefined) {
        args.push(`--ide-port=${options.idePort}`);
    }
    if (options.xdebugPort !== undefined) {
        args.push(`--xdebug-port=${options.xdebugPort}`);
    }
    const sourceMapDir = options.sourceMapDir?.trim() ?? "";
    if (sourceMapDir !== "") {
        args.push(`--sourcemap-dir=${sourceMapDir}`);
    }
    const ideKey = options.ideKey?.trim() ?? "";
    if (ideKey !== "") {
        args.push(`--ide-key=${ideKey}`);
    }
    return args;
}

export function buildXdebugProxyArgsFromLaunch(
    launch: ResolvedProxyLaunch,
    projectFilePath?: string
): string[] {
    return buildXdebugProxyArgs({
        projectFilePath,
        idePort: launch.idePort,
        xdebugPort: launch.xdebugPort,
        sourceMapDir: launch.sourceMapDir,
        ideKey: launch.ideKey,
    });
}

/** Parse `  IDE port:      9003` (and ephemeral bound ports) from CLI banner lines. */
export function parseBoundIdePort(line: string): number | undefined {
    const match = line.match(/IDE port:\s+(\d+)/i);
    if (!match) {
        return undefined;
    }
    return parsePortNumber(match[1]);
}

export function lineWarnsNoSourcemaps(line: string): boolean {
    return /no sourcemaps found/i.test(line);
}

export function isValidPort(value: number): boolean {
    return Number.isInteger(value) && value >= 0 && value <= 65535;
}

export function countPhpMapFiles(names: readonly string[]): number {
    let count = 0;
    for (const name of names) {
        if (name.toLowerCase().endsWith(".php.map")) {
            count += 1;
        }
    }
    return count;
}

function pickPort(
    settingsValue: number | undefined,
    jsonValue: number | undefined,
    fallback: number
): { value: number; source: ConfigValueSource } {
    if (settingsValue !== undefined && isValidPort(settingsValue)) {
        return { value: settingsValue, source: "settings" };
    }
    if (jsonValue !== undefined && isValidPort(jsonValue)) {
        return { value: jsonValue, source: "tyhp.json" };
    }
    return { value: fallback, source: "default" };
}

function pickOptionalString(
    settingsValue: string | undefined,
    jsonValue: string | undefined
): { value: string | undefined; source: ConfigValueSource } {
    const fromSettings = nonEmpty(settingsValue);
    if (fromSettings) {
        return { value: fromSettings, source: "settings" };
    }
    const fromJson = nonEmpty(jsonValue);
    if (fromJson) {
        return { value: fromJson, source: "tyhp.json" };
    }
    return { value: undefined, source: "omitted" };
}

function nonEmpty(value: string | undefined | null): string | undefined {
    const trimmed = (value ?? "").trim();
    return trimmed === "" ? undefined : trimmed;
}

function parsePortNumber(raw: string): number | undefined {
    const value = Number.parseInt(raw, 10);
    return isValidPort(value) ? value : undefined;
}
