import assert from "node:assert/strict";
import { test } from "node:test";
import {
    SettingsScope,
    explicitInspectedValue,
    isPathUnset,
    normalizeReleaseTag,
    parseInstallMode,
    pathWriteTarget,
    tagsMatch,
} from "./settingsCore";

test("empty and whitespace path values are unset", () => {
    assert.equal(isPathUnset(undefined), true);
    assert.equal(isPathUnset(null), true);
    assert.equal(isPathUnset(""), true);
    assert.equal(isPathUnset("   "), true);
    assert.equal(isPathUnset("/usr/local/bin/tyhp"), false);
});

test("path write target is User/Global unless a workspace override exists", () => {
    assert.equal(pathWriteTarget(undefined), SettingsScope.Global);
    assert.equal(pathWriteTarget({}), SettingsScope.Global);
    assert.equal(pathWriteTarget({ globalValue: "/user/tyhp" }), SettingsScope.Global);
    assert.equal(pathWriteTarget({ workspaceValue: "" }), SettingsScope.Workspace);
    assert.equal(pathWriteTarget({ workspaceValue: "/ws/tyhp" }), SettingsScope.Workspace);
    assert.equal(
        pathWriteTarget({ workspaceFolderValue: "/folder/tyhp", workspaceValue: "/ws/tyhp" }),
        SettingsScope.WorkspaceFolder
    );
});

test("install mode parsing", () => {
    assert.equal(parseInstallMode("path"), "path");
    assert.equal(parseInstallMode("global"), "global");
    assert.equal(parseInstallMode("extension"), "extension");
    assert.equal(parseInstallMode("nope"), "path");
    assert.equal(parseInstallMode(undefined), "path");
});

test("release tags normalize and compare with optional v prefix", () => {
    assert.equal(normalizeReleaseTag("805.0.0-alpha.1"), "v805.0.0-alpha.1");
    assert.equal(normalizeReleaseTag("v805.0.0-alpha.1"), "v805.0.0-alpha.1");
    assert.equal(normalizeReleaseTag(""), "");
    assert.equal(tagsMatch("805.0.0-alpha.1", "v805.0.0-alpha.1"), true);
    assert.equal(tagsMatch("v805.0.0-alpha.2", "v805.0.0-alpha.1"), false);
});

test("explicit inspected value prefers folder then workspace then user", () => {
    assert.equal(explicitInspectedValue(undefined), undefined);
    assert.equal(explicitInspectedValue({}), undefined);
    assert.equal(explicitInspectedValue({}), undefined);
    assert.equal(explicitInspectedValue({ globalValue: 9005 }), 9005);
    assert.equal(explicitInspectedValue({ globalValue: 9005, workspaceValue: 9010 }), 9010);
    assert.equal(
        explicitInspectedValue({
            globalValue: 9005,
            workspaceValue: 9010,
            workspaceFolderValue: 9111,
        }),
        9111
    );
});
