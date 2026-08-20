import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { test } from "node:test";
import { assertChecksum, expectedChecksum, parseChecksumFile, sha256File, ChecksumError } from "./checksum";

test("parseChecksumFile reads GNU sha256sum lines from scripts/release.sh", () => {
    const hash = "a".repeat(64);
    const contents = `${hash}  tyhp-osx-arm64\n${"b".repeat(64)} *tyhp-win-x64.exe\n# comment\n`;
    const map = parseChecksumFile(contents);
    assert.equal(map.get("tyhp-osx-arm64"), hash);
    assert.equal(map.get("tyhp-win-x64.exe"), "b".repeat(64));
    assert.equal(expectedChecksum(map, "tyhp-osx-arm64"), hash);
    assert.throws(() => expectedChecksum(map, "missing"), ChecksumError);
});

test("sha256File matches crypto hash and assertChecksum rejects mismatches", async () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "tyhp-checksum-"));
    const file = path.join(dir, "blob");
    const payload = Buffer.from("tyhp-cli-fixture");
    fs.writeFileSync(file, payload);
    try {
        const expected = createHash("sha256").update(payload).digest("hex");
        const actual = await sha256File(file);
        assert.equal(actual, expected);
        assertChecksum(actual, expected, "blob");
        assert.throws(() => assertChecksum(actual, "c".repeat(64), "blob"), ChecksumError);
    } finally {
        fs.rmSync(dir, { recursive: true, force: true });
    }
});
