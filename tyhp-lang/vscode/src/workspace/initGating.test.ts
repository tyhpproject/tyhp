import assert from "node:assert/strict";
import { test } from "node:test";
import { buildInitArgs, shouldPromptInit } from "./initGating";

const promptable = {
    languageId: "tyhp",
    hasOwner: false,
    hasAncestorTyhpJson: false,
    hasForcedProject: false,
    hasWorkspaceFolder: true,
    dontAskAgain: false,
    promptedThisSession: false,
};

test("init argv is init --yes (non-interactive; no --tyhp-project)", () => {
    assert.deepEqual(buildInitArgs(), ["init", "--yes"]);
});

test("prompts when a Tyhp file has no owner and no ancestor tyhp.json", () => {
    assert.equal(shouldPromptInit(promptable), true);
});

test("does not prompt when the file has an include owner", () => {
    assert.equal(shouldPromptInit({ ...promptable, hasOwner: true }), false);
});

test("does not prompt when an ancestor tyhp.json exists (even if include misses)", () => {
    assert.equal(shouldPromptInit({ ...promptable, hasAncestorTyhpJson: true }), false);
});

test("does not prompt when tyhp.projectPath forces a project", () => {
    assert.equal(shouldPromptInit({ ...promptable, hasForcedProject: true }), false);
});

test("does not prompt for non-Tyhp documents", () => {
    assert.equal(shouldPromptInit({ ...promptable, languageId: "php" }), false);
});

test("does not prompt without a workspace folder", () => {
    assert.equal(shouldPromptInit({ ...promptable, hasWorkspaceFolder: false }), false);
});

test("does not prompt again this session or after Don't Ask Again", () => {
    assert.equal(shouldPromptInit({ ...promptable, promptedThisSession: true }), false);
    assert.equal(shouldPromptInit({ ...promptable, dontAskAgain: true }), false);
});
