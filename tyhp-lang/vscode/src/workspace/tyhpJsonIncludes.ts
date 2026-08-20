/**
 * Reads `include` / `exclude` glob arrays from a `tyhp.json` document.
 * Invalid JSON or a non-object is treated as `include: []` (owns nothing).
 */

export interface TyhpJsonGlobs {
    readonly include: string[];
    readonly exclude: string[];
}

const EMPTY: TyhpJsonGlobs = { include: [], exclude: [] };

export function parseTyhpJsonGlobs(raw: string): TyhpJsonGlobs {
    let parsed: unknown;
    try {
        parsed = JSON.parse(raw) as unknown;
    } catch {
        return { include: [], exclude: [] };
    }
    if (!isRecord(parsed)) {
        return { include: [], exclude: [] };
    }
    return {
        include: stringList(parsed.include),
        exclude: stringList(parsed.exclude),
    };
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

function stringList(value: unknown): string[] {
    if (!Array.isArray(value)) {
        return [];
    }
    const out: string[] = [];
    for (const item of value) {
        if (typeof item === "string" && item.trim() !== "") {
            out.push(item);
        }
    }
    return out;
}

export const EMPTY_TYHP_JSON_GLOBS = EMPTY;
