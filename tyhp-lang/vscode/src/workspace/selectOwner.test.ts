import assert from "node:assert/strict";
import { test } from "node:test";
import { selectOwner } from "./selectOwner";

test("single candidate is the owner", () => {
    const owner = selectOwner("/repo/runtime/packages/core/tyhp_src/Type.tyhp", [
        { projectFilePath: "/repo/runtime/packages/core/tyhp.json", projectDir: "/repo/runtime/packages/core" },
    ]);
    assert.equal(owner?.projectFilePath, "/repo/runtime/packages/core/tyhp.json");
});

test("empty candidates yields no owner", () => {
    assert.equal(selectOwner("/repo/a.tyhp", []), undefined);
});

test("nearest ancestor wins over a non-ancestor that also matches", () => {
    const owner = selectOwner("/repo/app/pkg/src/Main.tyhp", [
        { projectFilePath: "/repo/runtime/packages/core/tyhp.json", projectDir: "/repo/runtime/packages/core" },
        { projectFilePath: "/repo/app/pkg/tyhp.json", projectDir: "/repo/app/pkg" },
        { projectFilePath: "/repo/app/tyhp.json", projectDir: "/repo/app" },
    ]);
    assert.equal(owner?.projectFilePath, "/repo/app/pkg/tyhp.json");
});

test("among ancestors, the nearest (deepest) folder wins", () => {
    const owner = selectOwner("/ws/lib/mod/src/a.tyhp", [
        { projectFilePath: "/ws/tyhp.json", projectDir: "/ws" },
        { projectFilePath: "/ws/lib/tyhp.json", projectDir: "/ws/lib" },
        { projectFilePath: "/ws/lib/mod/tyhp.json", projectDir: "/ws/lib/mod" },
    ]);
    assert.equal(owner?.projectFilePath, "/ws/lib/mod/tyhp.json");
});

test("when no ancestor matches, nearest by path hops wins", () => {
    const file = "/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef";
    const owner = selectOwner(file, [
        { projectFilePath: "/repo/runtime/packages/core/tyhp.json", projectDir: "/repo/runtime/packages/core" },
        { projectFilePath: "/repo/other/far/away/tyhp.json", projectDir: "/repo/other/far/away" },
    ]);
    assert.equal(owner?.projectFilePath, "/repo/runtime/packages/core/tyhp.json");
});

test("equal hops: shortest tyhp.json path, then lexicographic", () => {
    const file = "/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef";
    const owner = selectOwner(file, [
        { projectFilePath: "/repo/runtime/packages/core/tyhp.json", projectDir: "/repo/runtime/packages/core" },
        { projectFilePath: "/repo/runtime/packages/async/tyhp.json", projectDir: "/repo/runtime/packages/async" },
        { projectFilePath: "/repo/runtime/packages/lambda/tyhp.json", projectDir: "/repo/runtime/packages/lambda" },
    ]);
    assert.equal(owner?.projectFilePath, "/repo/runtime/packages/core/tyhp.json");
});

test("lexicographic tie-break when path lengths are equal", () => {
    const file = "/shared/x.tyhpdef";
    const owner = selectOwner(file, [
        { projectFilePath: "/b/tyhp.json", projectDir: "/b" },
        { projectFilePath: "/a/tyhp.json", projectDir: "/a" },
    ]);
    assert.equal(owner?.projectFilePath, "/a/tyhp.json");
});
