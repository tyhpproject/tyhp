/**
 * Resolves the `tyhp.json` path passed as `--tyhp-project`.
 * The CLI requires a file (see `CliStartup.TryValidateProjectFile`).
 */

export const TYHP_PROJECT_FILE = "tyhp.json";

export interface ProjectPathFs {
    existsSync(target: string): boolean;
    isDirectory(target: string): boolean;
}

export interface ResolveProjectFileOptions {
    /** `tyhp.projectPath` setting (file or directory; relative or absolute). */
    configuredPath: string;
    /** Workspace folder filesystem paths, in editor order. */
    workspaceRoots: readonly string[];
    join: (...parts: string[]) => string;
    resolve: (...parts: string[]) => string;
    isAbsolute: (target: string) => boolean;
    fs: ProjectPathFs;
}

function expandConfigured(
    configuredPath: string,
    workspaceRoots: readonly string[],
    resolve: (...parts: string[]) => string,
    isAbsolute: (target: string) => boolean
): string {
    const trimmed = configuredPath.trim();
    if (isAbsolute(trimmed) || workspaceRoots.length === 0) {
        return trimmed;
    }
    return resolve(workspaceRoots[0], trimmed);
}

function projectFileIn(dir: string, join: (...parts: string[]) => string): string {
    return join(dir, TYHP_PROJECT_FILE);
}

/**
 * Resolves `tyhp.projectPath` to a `tyhp.json` **file** for `--tyhp-project`.
 * When the setting is empty, returns `undefined` — callers must index
 * workspace `tyhp.json` files and match `include`/`exclude` instead of
 * assuming a workspace-root project.
 */
export function resolveTyhpProjectFile(options: ResolveProjectFileOptions): string | undefined {
    const configured = options.configuredPath.trim();
    if (configured === "") {
        return undefined;
    }

    const expanded = expandConfigured(
        configured,
        options.workspaceRoots,
        options.resolve,
        options.isAbsolute
    );
    if (options.fs.existsSync(expanded)) {
        if (options.fs.isDirectory(expanded)) {
            const nested = projectFileIn(expanded, options.join);
            return options.fs.existsSync(nested) ? nested : undefined;
        }
        return expanded;
    }
    return undefined;
}

/** True when `tyhp.projectPath` is set (forced single-project mode). */
export function isForcedProjectPath(configuredPath: string): boolean {
    return configuredPath.trim() !== "";
}
