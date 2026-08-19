import assert from "node:assert/strict";
import { test } from "node:test";
import { isExtensionOwnedInstall, InstallMetadata } from "./metadata";
import { planAutoUpdate } from "./UpdateService";

const owned: InstallMetadata = {
    installedBy: "tyhp-lang.tyhp",
    version: "v805.0.0-alpha.1",
    mode: "extension",
    assetName: "tyhp-osx-arm64",
    installedAt: "2026-08-18T00:00:00.000Z",
};

test("planAutoUpdate only floats latest for extension-owned installs", () => {
    const skipGlobal = planAutoUpdate(
        { installMode: "global", autoUpdate: true, pinnedVersion: "" },
        { ...owned, mode: "global" },
        "v805.0.0-alpha.2"
    );
    assert.equal(skipGlobal.shouldInstall, false);

    const skipPath = planAutoUpdate(
        { installMode: "path", autoUpdate: true, pinnedVersion: "" },
        undefined,
        "v805.0.0-alpha.2"
    );
    assert.equal(skipPath.shouldInstall, false);

    const update = planAutoUpdate(
        { installMode: "extension", autoUpdate: true, pinnedVersion: "" },
        owned,
        "v805.0.0-alpha.2"
    );
    assert.equal(update.shouldInstall, true);
    assert.equal(update.version, "v805.0.0-alpha.2");
});

test("isExtensionOwnedInstall requires our id and extension mode", () => {
    assert.equal(isExtensionOwnedInstall(owned), true);
    assert.equal(isExtensionOwnedInstall({ ...owned, mode: "global" }), false);
    assert.equal(isExtensionOwnedInstall({ ...owned, installedBy: "other" }), false);
    assert.equal(isExtensionOwnedInstall(undefined), false);
});
