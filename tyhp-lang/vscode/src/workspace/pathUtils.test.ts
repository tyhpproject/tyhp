import assert from "node:assert/strict";
import { test } from "node:test";
import {
    hasAncestorTyhpJson,
    matchingWorkspaceRoot,
    shouldSkipIndexedTyhpJson,
} from "./pathUtils";

test("skips tyhp.json under node_modules vendor .git bin obj dist build", () => {
    assert.equal(shouldSkipIndexedTyhpJson("/ws/node_modules/pkg/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/vendor/foo/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/.git/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/bin/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/obj/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/dist/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/build/tyhp.json"), true);
    assert.equal(shouldSkipIndexedTyhpJson("/ws/runtime/packages/core/tyhp.json"), false);
});

test("hasAncestorTyhpJson walks up to the workspace root", () => {
    const files = new Set(["/ws/runtime/packages/core/tyhp.json", "/ws/tyhp.json"]);
    assert.equal(
        hasAncestorTyhpJson(
            "/ws/runtime/packages/core/tyhp_src/Type.tyhp",
            "/ws",
            (p) => files.has(p)
        ),
        true
    );
    assert.equal(
        hasAncestorTyhpJson("/ws/orphan/src/a.tyhp", "/ws", (p) => files.has(p)),
        true
    );
    assert.equal(
        hasAncestorTyhpJson("/ws/orphan/src/a.tyhp", "/ws", () => false),
        false
    );
});

test("matchingWorkspaceRoot prefers the longest containing folder", () => {
    assert.equal(
        matchingWorkspaceRoot("/ws/app/src/a.tyhp", ["/ws", "/ws/app"]),
        "/ws/app"
    );
    assert.equal(matchingWorkspaceRoot("/other/a.tyhp", ["/ws"]), undefined);
});
