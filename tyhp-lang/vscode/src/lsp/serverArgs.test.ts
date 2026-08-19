import assert from "node:assert/strict";
import { test } from "node:test";
import { LANGUAGE_SERVER_ACTION, buildLanguageServerArgs } from "./serverArgs";

test("default argv is language_server --quiet --stdio", () => {
    assert.deepEqual(buildLanguageServerArgs(), [
        LANGUAGE_SERVER_ACTION,
        "--quiet",
        "--stdio",
    ]);
});

test("passes --tyhp-project as an inline value flag when a project file is known", () => {
    assert.deepEqual(buildLanguageServerArgs({ projectFilePath: "/ws/tyhp.json" }), [
        LANGUAGE_SERVER_ACTION,
        "--quiet",
        "--stdio",
        "--tyhp-project=/ws/tyhp.json",
    ]);
});

test("omits --tyhp-project when the path is empty or whitespace", () => {
    assert.deepEqual(buildLanguageServerArgs({ projectFilePath: "  " }), [
        LANGUAGE_SERVER_ACTION,
        "--quiet",
        "--stdio",
    ]);
});

test("appends extra args after the subcommand and does not duplicate language_server", () => {
    assert.deepEqual(
        buildLanguageServerArgs({
            extraArgs: [LANGUAGE_SERVER_ACTION, "--locale=en-US"],
            projectFilePath: "/p/tyhp.json",
        }),
        [
            LANGUAGE_SERVER_ACTION,
            "--quiet",
            "--stdio",
            "--tyhp-project=/p/tyhp.json",
            "--locale=en-US",
        ]
    );
});

test("does not add built-in flags that extra args already supply", () => {
    assert.deepEqual(
        buildLanguageServerArgs({
            extraArgs: ["--quiet", "--stdio", "--tyhp-project", "/other/tyhp.json"],
            projectFilePath: "/ws/tyhp.json",
        }),
        [LANGUAGE_SERVER_ACTION, "--quiet", "--stdio", "--tyhp-project", "/other/tyhp.json"]
    );
});

test("honors -q as the short quiet alias", () => {
    assert.deepEqual(buildLanguageServerArgs({ extraArgs: ["-q"] }), [
        LANGUAGE_SERVER_ACTION,
        "--stdio",
        "-q",
    ]);
});

test("can suppress quiet and stdio when explicitly disabled", () => {
    assert.deepEqual(buildLanguageServerArgs({ quiet: false, stdio: false }), [LANGUAGE_SERVER_ACTION]);
});
