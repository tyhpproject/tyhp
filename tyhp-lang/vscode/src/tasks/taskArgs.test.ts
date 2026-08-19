import assert from "node:assert/strict";
import { test } from "node:test";
import { buildTyhpTaskArgs, isTyhpTaskAction } from "./taskArgs";

test("build argv is build --quiet --tyhp-project=<file>", () => {
    assert.deepEqual(buildTyhpTaskArgs("build", "/ws/tyhp.json"), [
        "build",
        "--quiet",
        "--tyhp-project=/ws/tyhp.json",
    ]);
});

test("lint argv is lint --quiet --format=json --tyhp-project=<file>", () => {
    assert.deepEqual(buildTyhpTaskArgs("lint", "/ws/tyhp.json"), [
        "lint",
        "--quiet",
        "--format=json",
        "--tyhp-project=/ws/tyhp.json",
    ]);
});

test("omits --tyhp-project when no project file is known", () => {
    assert.deepEqual(buildTyhpTaskArgs("build"), ["build", "--quiet"]);
    assert.deepEqual(buildTyhpTaskArgs("lint", "  "), ["lint", "--quiet", "--format=json"]);
});

test("isTyhpTaskAction accepts only build and lint", () => {
    assert.equal(isTyhpTaskAction("build"), true);
    assert.equal(isTyhpTaskAction("lint"), true);
    assert.equal(isTyhpTaskAction("init"), false);
    assert.equal(isTyhpTaskAction("xdebug_proxy"), false);
});
