import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import { detectHostPlatform, HostPlatform, pathProbeNames } from "./platform";

export interface PathProbeFs {
    existsSync(filePath: string): boolean;
    statSync(filePath: string): { isFile(): boolean };
}

const defaultFs: PathProbeFs = {
    existsSync: fs.existsSync,
    statSync: (filePath) => fs.statSync(filePath),
};

export interface PathProbeOptions {
    pathEnv?: string;
    pathDelimiter?: string;
    platform?: HostPlatform;
    fs?: PathProbeFs;
}

function isCandidateFile(filePath: string, io: PathProbeFs): boolean {
    try {
        if (!io.existsSync(filePath)) {
            return false;
        }
        return io.statSync(filePath).isFile();
    } catch {
        return false;
    }
}

/**
 * Search PATH for a `tyhp` executable. Returns an absolute path, or `undefined`.
 */
export function probeTyhpOnPath(options: PathProbeOptions = {}): string | undefined {
    const platform = options.platform ?? detectHostPlatform();
    const pathEnv = options.pathEnv ?? process.env.PATH ?? process.env.Path ?? "";
    const delimiter = options.pathDelimiter ?? path.delimiter;
    const io = options.fs ?? defaultFs;
    const names = pathProbeNames(platform);

    if (pathEnv.trim() === "") {
        return undefined;
    }

    for (const dir of pathEnv.split(delimiter)) {
        const trimmed = dir.trim().replace(/^"(.*)"$/, "$1");
        if (trimmed === "") {
            continue;
        }
        for (const name of names) {
            const candidate = path.resolve(trimmed, name);
            if (isCandidateFile(candidate, io)) {
                return candidate;
            }
        }
    }
    return undefined;
}

export function lookUpCommandOnPath(
    command: string,
    options: PathProbeOptions = {}
): string | undefined {
    const platform = options.platform ?? detectHostPlatform();
    const pathEnv = options.pathEnv ?? process.env.PATH ?? process.env.Path ?? "";
    const delimiter = options.pathDelimiter ?? path.delimiter;
    const io = options.fs ?? defaultFs;

    const names =
        platform.os === "win" && !command.toLowerCase().endsWith(".exe")
            ? [`${command}.exe`, command]
            : [command];

    for (const dir of (pathEnv ?? "").split(delimiter)) {
        const trimmed = dir.trim().replace(/^"(.*)"$/, "$1");
        if (trimmed === "") {
            continue;
        }
        for (const name of names) {
            const candidate = path.resolve(trimmed, name);
            if (isCandidateFile(candidate, io)) {
                return candidate;
            }
        }
    }
    return undefined;
}

export function expandHome(filePath: string, homedir: string = os.homedir()): string {
    if (filePath === "~") {
        return homedir;
    }
    if (filePath.startsWith("~/") || filePath.startsWith("~\\")) {
        return homedir + filePath.slice(1);
    }
    return filePath;
}

export interface ExecutableCheck {
    ok: boolean;
    absolutePath?: string;
    message?: string;
}

/**
 * Validate a `tyhp.path` value: absolute path, `~/…`, or a command on PATH.
 */
export function validateTyhpPath(
    configured: string,
    options: PathProbeOptions & { homedir?: string } = {}
): ExecutableCheck {
    const trimmed = configured.trim();
    if (trimmed === "") {
        return { ok: false, message: "`tyhp.path` is empty" };
    }

    const io = options.fs ?? defaultFs;
    const expanded = expandHome(trimmed, options.homedir);
    const looksLikePath =
        path.isAbsolute(expanded) || expanded.includes("/") || expanded.includes("\\");

    let resolved: string | undefined;
    if (looksLikePath) {
        resolved = path.resolve(expanded);
        if (!isCandidateFile(resolved, io)) {
            return {
                ok: false,
                absolutePath: resolved,
                message: `Tyhp CLI at \`${resolved}\` is missing or is not a file. Use “Tyhp: Install / Update CLI” or fix \`tyhp.path\`.`,
            };
        }
    } else {
        resolved = lookUpCommandOnPath(trimmed, options);
        if (!resolved) {
            return {
                ok: false,
                message: `Command \`${trimmed}\` from \`tyhp.path\` was not found on PATH. Use “Tyhp: Install / Update CLI” or set an absolute path.`,
            };
        }
    }

    try {
        resolved = fs.existsSync(resolved) ? fs.realpathSync(resolved) : resolved;
    } catch {
        // keep resolved as-is
    }

    return { ok: true, absolutePath: resolved };
}
