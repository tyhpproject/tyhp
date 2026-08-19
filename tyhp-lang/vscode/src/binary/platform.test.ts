import assert from "node:assert/strict";
import * as path from "node:path";
import { test } from "node:test";
import {
    UnsupportedPlatformError,
    chooseAssetVariant,
    detectHostPlatform,
    extensionInstallPath,
    globalInstallDir,
    globalInstallPath,
    installedBinaryFileName,
    isManagedInstallPath,
    releaseAssetName,
} from "./platform";

test("release asset names match scripts/release.sh", () => {
    assert.equal(
        releaseAssetName({ os: "osx", arch: "arm64", nodePlatform: "darwin" }, "self-contained"),
        "tyhp-osx-arm64"
    );
    assert.equal(
        releaseAssetName({ os: "osx", arch: "x64", nodePlatform: "darwin" }, "framework-dependent"),
        "tyhp-osx-x64-fxdependent"
    );
    assert.equal(
        releaseAssetName({ os: "linux", arch: "x64", nodePlatform: "linux" }, "self-contained"),
        "tyhp-linux-x64"
    );
    assert.equal(
        releaseAssetName({ os: "linux", arch: "arm64", nodePlatform: "linux" }, "framework-dependent"),
        "tyhp-linux-arm64-fxdependent"
    );
    assert.equal(
        releaseAssetName({ os: "win", arch: "x64", nodePlatform: "win32" }, "self-contained"),
        "tyhp-win-x64.exe"
    );
    assert.equal(
        releaseAssetName({ os: "win", arch: "x64", nodePlatform: "win32" }, "framework-dependent"),
        "tyhp-win-x64-fxdependent.exe"
    );
});

test("unsupported platforms throw a clear error", () => {
    assert.throws(() => detectHostPlatform("freebsd" as NodeJS.Platform, "x64"), UnsupportedPlatformError);
    assert.throws(() => detectHostPlatform("win32", "arm64"), UnsupportedPlatformError);
    assert.throws(() => detectHostPlatform("linux", "ia32"), UnsupportedPlatformError);
});

test("global install locations match official install scripts", () => {
    assert.equal(globalInstallDir({ os: "osx", arch: "arm64", nodePlatform: "darwin" }, "/Users/me"), path.join("/Users/me", ".local", "bin"));
    assert.equal(
        globalInstallPath({ os: "linux", arch: "x64", nodePlatform: "linux" }, "/home/me"),
        path.join("/home/me", ".local", "bin", "tyhp")
    );
    assert.equal(
        globalInstallDir({ os: "win", arch: "x64", nodePlatform: "win32" }, "C:\\Users\\me", "C:\\Users\\me\\AppData\\Local"),
        path.join("C:\\Users\\me\\AppData\\Local", "Programs", "tyhp")
    );
    assert.equal(installedBinaryFileName({ os: "win", arch: "x64", nodePlatform: "win32" }), "tyhp.exe");
});

test("extension-only always uses self-contained; global follows .NET 9", () => {
    assert.equal(chooseAssetVariant("extension", true), "self-contained");
    assert.equal(chooseAssetVariant("extension", false), "self-contained");
    assert.equal(chooseAssetVariant("global", true), "framework-dependent");
    assert.equal(chooseAssetVariant("global", false), "self-contained");
});

test("extension storage path is under globalStorage/cli", () => {
    const p = extensionInstallPath("/tmp/tyhp-lang.tyhp", { os: "osx", arch: "arm64", nodePlatform: "darwin" });
    assert.equal(p, path.join("/tmp/tyhp-lang.tyhp", "cli", "tyhp"));
});

test("isManagedInstallPath detects drift between tyhp.path and the extension install", () => {
    const platform = { os: "osx", arch: "arm64", nodePlatform: "darwin" } as const;
    const managed = extensionInstallPath("/tmp/tyhp-lang.tyhp", platform);

    // Exactly the path this extension would have written after an "extension" install.
    assert.equal(isManagedInstallPath(managed, "/tmp/tyhp-lang.tyhp", platform), true);
    // Equivalent but non-normalized (e.g. trailing "..") still resolves to the same file.
    assert.equal(
        isManagedInstallPath(path.join(managed, "..", "tyhp"), "/tmp/tyhp-lang.tyhp", platform),
        true
    );
    // A user hand-edited `tyhp.path` to point elsewhere: must not be treated as managed,
    // even though `tyhp.binary.installMode` may still say "extension" (stale state) —
    // "setting wins" and auto-update must never silently overwrite it.
    assert.equal(isManagedInstallPath("/opt/custom/tyhp", "/tmp/tyhp-lang.tyhp", platform), false);
    // Empty/unset is never managed.
    assert.equal(isManagedInstallPath("", "/tmp/tyhp-lang.tyhp", platform), false);
    assert.equal(isManagedInstallPath("   ", "/tmp/tyhp-lang.tyhp", platform), false);
});
