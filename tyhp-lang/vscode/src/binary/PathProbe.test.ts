import assert from "node:assert/strict";
import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { test } from "node:test";
import { lookUpCommandOnPath, probeTyhpOnPath, validateTyhpPath } from "./PathProbe";
import { HostPlatform } from "./platform";

const unix: HostPlatform = { os: "osx", arch: "arm64", nodePlatform: "darwin" };
const win: HostPlatform = { os: "win", arch: "x64", nodePlatform: "win32" };

test("PATH probe finds tyhp in the first matching directory", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "tyhp-path-"));
    const hitDir = path.join(root, "bin");
    const missDir = path.join(root, "empty");
    fs.mkdirSync(hitDir);
    fs.mkdirSync(missDir);
    const binary = path.join(hitDir, "tyhp");
    fs.writeFileSync(binary, "#!/bin/sh\n");
    fs.chmodSync(binary, 0o755);
    try {
        const found = probeTyhpOnPath({
            pathEnv: [missDir, hitDir].join(path.delimiter),
            pathDelimiter: path.delimiter,
            platform: unix,
        });
        assert.equal(found, binary);
    } finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
});

test("PATH probe returns undefined when tyhp is absent", () => {
    const found = probeTyhpOnPath({
        pathEnv: "/no/such/tyhp-bin",
        pathDelimiter: ":",
        platform: unix,
    });
    assert.equal(found, undefined);
});

test("Windows PATH probe prefers tyhp.exe", () => {
    const names: string[] = [];
    const found = probeTyhpOnPath({
        pathEnv: "C:\\Tools",
        pathDelimiter: ";",
        platform: win,
        fs: {
            existsSync(filePath) {
                names.push(filePath);
                return filePath.replace(/\//g, "\\").endsWith("\\tyhp.exe") || filePath.endsWith("/tyhp.exe");
            },
            statSync() {
                return { isFile: () => true };
            },
        },
    });
    assert.ok(found?.endsWith("tyhp.exe"));
    assert.ok(names.some((n) => n.endsWith("tyhp.exe")));
});

test("setting precedence: explicit path is validated; command names resolve via PATH", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "tyhp-validate-"));
    const binary = path.join(root, "tyhp");
    fs.writeFileSync(binary, "#!/bin/sh\n");
    fs.chmodSync(binary, 0o755);
    try {
        const ok = validateTyhpPath(binary, { platform: unix });
        assert.equal(ok.ok, true);
        assert.equal(ok.absolutePath, fs.realpathSync(binary));

        const missing = validateTyhpPath(path.join(root, "missing"), { platform: unix });
        assert.equal(missing.ok, false);
        assert.match(missing.message ?? "", /missing or is not a file/);

        const viaPath = lookUpCommandOnPath("tyhp", {
            pathEnv: root,
            pathDelimiter: path.delimiter,
            platform: unix,
        });
        assert.equal(viaPath, binary);

        const cmd = validateTyhpPath("tyhp", {
            pathEnv: root,
            pathDelimiter: path.delimiter,
            platform: unix,
        });
        assert.equal(cmd.ok, true);
        assert.equal(cmd.absolutePath, fs.realpathSync(binary));
    } finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
});
