import { createHash } from "crypto";
import { createReadStream } from "fs";

const HASH_RE = /^([a-fA-F0-9]{64})\s+\*?(\S+)\s*$/;

export class ChecksumError extends Error {
    constructor(message: string) {
        super(message);
        this.name = "ChecksumError";
    }
}

/** Parse GNU `sha256sum` output (`checksums.txt` from `scripts/release.sh`). */
export function parseChecksumFile(contents: string): Map<string, string> {
    const map = new Map<string, string>();
    for (const rawLine of contents.split(/\r?\n/)) {
        const line = rawLine.trim();
        if (line === "" || line.startsWith("#")) {
            continue;
        }
        const match = HASH_RE.exec(line);
        if (!match) {
            continue;
        }
        map.set(match[2], match[1].toLowerCase());
    }
    return map;
}

export function expectedChecksum(checksums: Map<string, string>, assetName: string): string {
    const hash = checksums.get(assetName);
    if (!hash) {
        throw new ChecksumError(
            `checksums.txt has no SHA-256 for \`${assetName}\`. Refusing to install an unverified binary.`
        );
    }
    return hash;
}

export async function sha256File(filePath: string): Promise<string> {
    const hash = createHash("sha256");
    const stream = createReadStream(filePath);
    for await (const chunk of stream) {
        hash.update(chunk);
    }
    return hash.digest("hex");
}

export function assertChecksum(actualHex: string, expectedHex: string, assetName: string): void {
    if (actualHex.toLowerCase() !== expectedHex.toLowerCase()) {
        throw new ChecksumError(
            `SHA-256 mismatch for \`${assetName}\`: expected ${expectedHex}, got ${actualHex}. ` +
                "The download may be corrupt or tampered with."
        );
    }
}
