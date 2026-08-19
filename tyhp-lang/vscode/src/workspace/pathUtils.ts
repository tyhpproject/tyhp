/**
 * POSIX-style path helpers so membership / owner selection match
 * `tyhp build` (globs relative to the `tyhp.json` directory) on every OS.
 */

export function toPosix(p: string): string {
    return p.replace(/\\/g, "/");
}

export function stripDotSlash(p: string): string {
    let out = toPosix(p).trim();
    while (out.startsWith("./")) {
        out = out.slice(2);
    }
    return out;
}

export function posixNormalize(p: string): string {
    const posix = toPosix(p);
    const absolute = posix.startsWith("/");
    const parts = posix.split("/").filter((part, i) => part !== "" && part !== "." && !(i === 0 && part === ""));
    const stack: string[] = [];
    for (const part of parts) {
        if (part === "..") {
            if (stack.length > 0 && stack[stack.length - 1] !== "..") {
                stack.pop();
            } else if (!absolute) {
                stack.push("..");
            }
        } else {
            stack.push(part);
        }
    }
    const joined = stack.join("/");
    if (absolute) {
        return `/${joined}`;
    }
    return joined === "" ? "." : joined;
}

export function posixDirname(p: string): string {
    const n = toPosix(p).replace(/\/+$/, "");
    const i = n.lastIndexOf("/");
    if (i < 0) {
        return ".";
    }
    if (i === 0) {
        return "/";
    }
    return n.slice(0, i);
}

export function posixBasename(p: string): string {
    const n = toPosix(p).replace(/\/+$/, "");
    const i = n.lastIndexOf("/");
    return i < 0 ? n : n.slice(i + 1);
}

function splitAbs(p: string): string[] {
    const n = posixNormalize(p);
    if (n === "/") {
        return [];
    }
    return n.replace(/^\//, "").split("/").filter((part) => part !== "");
}

function commonPrefixLength(a: readonly string[], b: readonly string[], caseInsensitive: boolean): number {
    const n = Math.min(a.length, b.length);
    let i = 0;
    while (i < n) {
        const left = caseInsensitive ? a[i].toLowerCase() : a[i];
        const right = caseInsensitive ? b[i].toLowerCase() : b[i];
        if (left !== right) {
            break;
        }
        i += 1;
    }
    return i;
}

/**
 * Relative POSIX path from [fromDir] to [toPath] (`../` when the target is
 * outside [fromDir]). Matches Node `path.relative` for absolute POSIX paths.
 */
export function posixRelative(fromDir: string, toPath: string, caseInsensitive = false): string {
    const from = splitAbs(fromDir);
    const to = splitAbs(toPath);
    const i = commonPrefixLength(from, to, caseInsensitive);
    const ups = from.length - i;
    const down = to.slice(i);
    const parts = [...Array.from({ length: ups }, () => ".."), ...down];
    return parts.join("/") || ".";
}

/** Directory distance (path components) from [fromDir] to [toPath]. */
export function pathHops(fromDir: string, toPath: string, caseInsensitive = false): number {
    const rel = posixRelative(fromDir, toPath, caseInsensitive);
    if (rel === "." || rel === "") {
        return 0;
    }
    return rel.split("/").filter((part) => part !== "").length;
}

/**
 * True when [filePath] is the directory [dir] or a descendant of it.
 */
export function isPathInside(dir: string, filePath: string, caseInsensitive = false): boolean {
    const rel = posixRelative(dir, filePath, caseInsensitive);
    return rel !== ".." && !rel.startsWith("../") && rel !== ".";
}

/**
 * Walk from [filePath]'s directory up to [workspaceRoot], returning true if
 * any ancestor directory contains `tyhp.json`.
 */
export function hasAncestorTyhpJson(
    filePath: string,
    workspaceRoot: string | undefined,
    exists: (path: string) => boolean,
    join: (dir: string, name: string) => string = (dir, name) => `${toPosix(dir).replace(/\/$/, "")}/${name}`
): boolean {
    if (!workspaceRoot) {
        return false;
    }
    const stop = posixNormalize(workspaceRoot);
    let dir = posixDirname(filePath);
    const seen = new Set<string>();
    while (!seen.has(dir)) {
        seen.add(dir);
        if (exists(join(dir, "tyhp.json"))) {
            return true;
        }
        const normalized = posixNormalize(dir);
        if (normalized === stop) {
            break;
        }
        if (stop !== "/" && !isPathInside(stop, dir) && normalized !== stop) {
            break;
        }
        const parent = posixDirname(dir);
        if (parent === dir) {
            break;
        }
        dir = parent;
    }
    return false;
}

export const INDEX_SKIP_DIR_NAMES = new Set(["node_modules", "vendor", ".git", "bin", "obj", "dist", "build"]);

/** True when a `tyhp.json` path should be ignored during workspace indexing. */
export function shouldSkipIndexedTyhpJson(filePath: string): boolean {
    const parts = toPosix(filePath).split("/");
    return parts.some((part) => INDEX_SKIP_DIR_NAMES.has(part));
}

export function matchingWorkspaceRoot(
    filePath: string,
    workspaceRoots: readonly string[],
    caseInsensitive = false
): string | undefined {
    const matches = workspaceRoots.filter((root) => {
        const n = posixNormalize(root);
        const file = posixNormalize(filePath);
        if (caseInsensitive) {
            return file.toLowerCase() === n.toLowerCase() || isPathInside(n, file, true);
        }
        return file === n || isPathInside(n, file, false);
    });
    if (matches.length === 0) {
        return undefined;
    }
    return matches.reduce((best, cur) => (cur.length >= best.length ? cur : best));
}
