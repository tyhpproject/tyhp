import { TyhpJsonProjectSnapshot, TyhpJsonProxySection } from "./proxyConfig";

/**
 * Reads the `xdebugProxy`, `build.generateSourcemap`, and `output.path`
 * fields from a `tyhp.json` document. Returns `undefined` when the text is
 * not JSON.
 */
export function parseTyhpJsonProject(raw: string): TyhpJsonProjectSnapshot | undefined {
    let parsed: unknown;
    try {
        parsed = JSON.parse(raw) as unknown;
    } catch {
        return undefined;
    }
    if (!isRecord(parsed)) {
        return undefined;
    }

    const build = isRecord(parsed.build) ? parsed.build : undefined;
    const output = isRecord(parsed.output) ? parsed.output : undefined;
    const proxyRaw = isRecord(parsed.xdebugProxy) ? parsed.xdebugProxy : undefined;

    return {
        xdebugProxy: proxyRaw ? parseProxySection(proxyRaw) : undefined,
        generateSourcemap: readBool(build?.generateSourcemap) === true,
        outputPath: readString(output?.path),
    };
}

function parseProxySection(raw: Record<string, unknown>): TyhpJsonProxySection {
    const section: TyhpJsonProxySection = {};
    const idePort = readPort(raw.idePort);
    if (idePort !== undefined) {
        section.idePort = idePort;
    }
    const xdebugPort = readPort(raw.xdebugPort);
    if (xdebugPort !== undefined) {
        section.xdebugPort = xdebugPort;
    }
    const sourceMapDir = readString(raw.sourceMapDir);
    if (sourceMapDir !== undefined) {
        section.sourceMapDir = sourceMapDir;
    }
    const ideKey = readString(raw.ideKey);
    if (ideKey !== undefined) {
        section.ideKey = ideKey;
    }
    return section;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readBool(value: unknown): boolean | undefined {
    return typeof value === "boolean" ? value : undefined;
}

function readString(value: unknown): string | undefined {
    if (typeof value !== "string") {
        return undefined;
    }
    const trimmed = value.trim();
    return trimmed === "" ? undefined : trimmed;
}

function readPort(value: unknown): number | undefined {
    if (typeof value === "number" && Number.isInteger(value)) {
        return value;
    }
    if (typeof value === "string" && value.trim() !== "") {
        const parsed = Number.parseInt(value, 10);
        return Number.isInteger(parsed) ? parsed : undefined;
    }
    return undefined;
}
