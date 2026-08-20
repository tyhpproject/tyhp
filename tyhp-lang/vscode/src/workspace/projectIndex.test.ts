import assert from "node:assert/strict";
import { test } from "node:test";
import { indexedProjectFromJson, ProjectIndex, snapshotFromOwner } from "./projectIndex";

const CORE_JSON = JSON.stringify({
    include: ["./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef"],
    exclude: [],
});

const ROOT_JSON = JSON.stringify({
    include: [],
    exclude: [],
});

const APP_JSON = JSON.stringify({
    include: ["./src/**/*.tyhp"],
    exclude: [],
});

test("root tyhp.json with empty include owns nothing", () => {
    const index = new ProjectIndex(
        [
            indexedProjectFromJson("/repo/tyhp.json", ROOT_JSON),
            indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", CORE_JSON),
        ],
        false
    );
    assert.equal(index.ownerOf("/repo/README.tyhp"), undefined);
    assert.equal(
        index.ownerOf("/repo/runtime/packages/core/tyhp_src/Type.tyhp")?.projectName,
        "core"
    );
});

test("invalid JSON project owns nothing but stays in the index", () => {
    const index = new ProjectIndex(
        [indexedProjectFromJson("/repo/broken/tyhp.json", "{ not json")],
        false
    );
    assert.equal(index.ownerOf("/repo/broken/src/a.tyhp"), undefined);
});

test("non-ancestor include still owns the file", () => {
    const index = new ProjectIndex(
        [indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", CORE_JSON)],
        false
    );
    const owner = index.ownerOf("/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef");
    assert.equal(owner?.projectName, "core");
});

test("does not merge two matching projects", () => {
    const asyncJson = JSON.stringify({
        include: ["./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef"],
    });
    const index = new ProjectIndex(
        [
            indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", CORE_JSON),
            indexedProjectFromJson("/repo/runtime/packages/async/tyhp.json", asyncJson),
        ],
        false
    );
    const owner = index.ownerOf("/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef");
    // hops are equal; `core` is the shorter `tyhp.json` path
    assert.equal(owner?.projectName, "core");
});

test("snapshotFromOwner is empty when the file is not in a project", () => {
    const snap = snapshotFromOwner(undefined);
    assert.equal(snap.projectFilePath, undefined);
    assert.equal(snap.projectName, undefined);
});

test("app src file is not owned by core", () => {
    const index = new ProjectIndex(
        [
            indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", CORE_JSON),
            indexedProjectFromJson("/repo/app/tyhp.json", APP_JSON),
        ],
        false
    );
    assert.equal(index.ownerOf("/repo/app/src/Main.tyhp")?.projectName, "app");
    assert.equal(index.ownerOf("/repo/runtime/packages/core/tyhp_src/Type.tyhp")?.projectName, "core");
});
