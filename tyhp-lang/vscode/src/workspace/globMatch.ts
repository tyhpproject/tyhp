/**
 * Single-path glob matching compatible with
 * `Microsoft.Extensions.FileSystemGlobbing.Matcher` as used by
 * `Project.GetProjectSourceFiles()` (patterns relative to the `tyhp.json`
 * directory). Empty `include` owns nothing.
 */

import { posixRelative, stripDotSlash, toPosix } from "./pathUtils";

const REGEX_ESCAPE = /[.+^${}()|[\]\\]/g;

/**
 * Convert a FileSystemGlobbing-style glob to a RegExp.
 * `*` = one path segment; `**` = zero or more segments; `?` = one character.
 */
export function globToRegExp(glob: string, caseInsensitive: boolean): RegExp {
    const pattern = stripDotSlash(glob);
    let regex = "^";
    let i = 0;
    while (i < pattern.length) {
        const c = pattern[i];
        if (c === "*" && pattern[i + 1] === "*") {
            const after = pattern[i + 2];
            if (after === "/" || after === undefined) {
                if (after === "/") {
                    regex += "(?:.*/)?";
                    i += 3;
                } else {
                    regex += ".*";
                    i += 2;
                }
            } else {
                regex += ".*";
                i += 2;
            }
        } else if (c === "*") {
            regex += "[^/]*";
            i += 1;
        } else if (c === "?") {
            regex += "[^/]";
            i += 1;
        } else {
            regex += c.replace(REGEX_ESCAPE, "\\$&");
            i += 1;
        }
    }
    regex += "$";
    return new RegExp(regex, caseInsensitive ? "i" : "");
}

export function matchesGlob(relativePath: string, glob: string, caseInsensitive: boolean): boolean {
    const rel = stripDotSlash(toPosix(relativePath));
    const re = globToRegExp(glob, caseInsensitive);
    return re.test(rel);
}

export interface MembershipOptions {
    projectDir: string;
    filePath: string;
    include: readonly string[];
    exclude: readonly string[];
    caseInsensitive: boolean;
}

/**
 * Whether [filePath] is owned by a project: any include glob matches and no
 * exclude glob matches. Globs and the relative path are relative to [projectDir].
 */
export function fileMatchesProject(options: MembershipOptions): boolean {
    if (options.include.length === 0) {
        return false;
    }
    const relative = posixRelative(options.projectDir, options.filePath, options.caseInsensitive);
    if (relative === ".") {
        return false;
    }
    const included = options.include.some((glob) => matchesGlob(relative, glob, options.caseInsensitive));
    if (!included) {
        return false;
    }
    const excluded = options.exclude.some((glob) => matchesGlob(relative, glob, options.caseInsensitive));
    return !excluded;
}
