import assert from "node:assert/strict";
import { test } from "node:test";
import { decideExplicitInstall, decideStartupUpdate, shouldAutoUpdate } from "./policy";

test("PATH and global installs never auto-update", () => {
    const latest = "v805.0.0-alpha.2";
    for (const installMode of ["path", "global"] as const) {
        const decision = decideStartupUpdate({
            installMode,
            installedByExtension: installMode === "global",
            autoUpdate: true,
            pinnedVersion: "",
            currentVersion: "v805.0.0-alpha.1",
            latestVersion: latest,
        });
        assert.equal(decision.action, "none");
        assert.equal(shouldAutoUpdate({
            installMode,
            installedByExtension: true,
            autoUpdate: true,
            pinnedVersion: "",
            currentVersion: "v805.0.0-alpha.1",
            latestVersion: latest,
        }), false);
    }
});

test("extension-only auto-update requires this extension owns the binary", () => {
    const decision = decideStartupUpdate({
        installMode: "extension",
        installedByExtension: false,
        autoUpdate: true,
        pinnedVersion: "",
        currentVersion: "v805.0.0-alpha.1",
        latestVersion: "v805.0.0-alpha.2",
    });
    assert.equal(decision.action, "none");
});

test("extension-only auto-update installs latest when newer and enabled", () => {
    const decision = decideStartupUpdate({
        installMode: "extension",
        installedByExtension: true,
        autoUpdate: true,
        pinnedVersion: "",
        currentVersion: "v805.0.0-alpha.1",
        latestVersion: "v805.0.0-alpha.2",
    });
    assert.deepEqual(decision, {
        action: "install",
        version: "v805.0.0-alpha.2",
        reason: "Newer release v805.0.0-alpha.2 is available",
    });
});

test("extension-only auto-update is skipped when already current or disabled", () => {
    const current = decideStartupUpdate({
        installMode: "extension",
        installedByExtension: true,
        autoUpdate: true,
        pinnedVersion: "",
        currentVersion: "v805.0.0-alpha.2",
        latestVersion: "v805.0.0-alpha.2",
    });
    assert.equal(current.action, "none");

    const disabled = decideStartupUpdate({
        installMode: "extension",
        installedByExtension: true,
        autoUpdate: false,
        pinnedVersion: "",
        currentVersion: "v805.0.0-alpha.1",
        latestVersion: "v805.0.0-alpha.2",
    });
    assert.equal(disabled.action, "none");
});

test("pinned version is kept and other latest tags are not auto-installed", () => {
    const alreadyPinned = decideStartupUpdate({
        installMode: "extension",
        installedByExtension: true,
        autoUpdate: true,
        pinnedVersion: "805.0.0-alpha.1",
        currentVersion: "v805.0.0-alpha.1",
        latestVersion: "v805.0.0-alpha.2",
    });
    assert.equal(alreadyPinned.action, "none");

    const moveToPin = decideStartupUpdate({
        installMode: "extension",
        installedByExtension: true,
        autoUpdate: false,
        pinnedVersion: "v805.0.0-alpha.1",
        currentVersion: "v805.0.0-alpha.2",
        latestVersion: "v805.0.0-alpha.3",
    });
    assert.equal(moveToPin.action, "install");
    if (moveToPin.action === "install") {
        assert.equal(moveToPin.version, "v805.0.0-alpha.1");
    }
});

test("explicit install uses pin when set, otherwise latest", () => {
    const pinned = decideExplicitInstall("805.0.0-alpha.1", "v805.0.0-alpha.9");
    assert.equal(pinned.action, "install");
    if (pinned.action === "install") {
        assert.equal(pinned.version, "v805.0.0-alpha.1");
    }

    const latest = decideExplicitInstall("", "v805.0.0-alpha.9");
    assert.equal(latest.action, "install");
    if (latest.action === "install") {
        assert.equal(latest.version, "v805.0.0-alpha.9");
    }

    const none = decideExplicitInstall("", "");
    assert.equal(none.action, "none");
});
