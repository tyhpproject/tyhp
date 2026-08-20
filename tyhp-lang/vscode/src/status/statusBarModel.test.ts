import assert from "node:assert/strict";
import { test } from "node:test";
import { formatStatusBar, proxyStatusActions } from "./statusBarModel";

test("healthy project + running LSP", () => {
    const view = formatStatusBar({
        projectLabel: "demo",
        lspState: "running",
        binaryStatus: "ok",
        binaryPath: "/usr/local/bin/tyhp",
    });
    assert.equal(view.text, "$(check) Tyhp · demo · ready");
    assert.equal(view.error, false);
    assert.equal(view.warning, false);
    assert.match(view.tooltip, /\/usr\/local\/bin\/tyhp/);
});

test("binary missing uses error background and CLI missing copy", () => {
    const view = formatStatusBar({
        projectLabel: "demo",
        lspState: "error",
        binaryStatus: "missing",
        binaryMessage: "Tyhp CLI was not found.",
    });
    assert.equal(view.text, "$(error) Tyhp · demo · CLI missing");
    assert.equal(view.error, true);
    assert.match(view.tooltip, /Tyhp CLI was not found/);
});

test("LSP error with a healthy binary", () => {
    const view = formatStatusBar({
        projectLabel: "demo",
        lspState: "error",
        binaryStatus: "ok",
        binaryPath: "/bin/tyhp",
    });
    assert.equal(view.text, "$(error) Tyhp · demo · LSP error");
    assert.equal(view.error, true);
});

test("not in a Tyhp project is a warning when LSP is ready", () => {
    const view = formatStatusBar({
        projectLabel: "not in a Tyhp project",
        lspState: "running",
        binaryStatus: "ok",
        binaryPath: "/bin/tyhp",
    });
    assert.equal(view.text, "$(warning) Tyhp · not in a Tyhp project · ready");
    assert.equal(view.warning, true);
    assert.equal(view.error, false);
});

test("starting shows a spinner", () => {
    const view = formatStatusBar({
        projectLabel: "demo",
        lspState: "starting",
        binaryStatus: "ok",
        binaryPath: "/bin/tyhp",
    });
    assert.equal(view.text, "$(sync~spin) Tyhp · demo · starting");
});

test("running proxy is appended to a healthy status", () => {
    const view = formatStatusBar({
        projectLabel: "demo",
        lspState: "running",
        binaryStatus: "ok",
        binaryPath: "/bin/tyhp",
        proxyState: "running",
        proxyDetail: "IDE 9003 / XDebug 9004",
    });
    assert.equal(view.text, "$(check) Tyhp · demo · ready · proxy");
    assert.match(view.tooltip, /XDebug proxy: listening \(IDE 9003 \/ XDebug 9004\)/);
});

test("proxy error is a warning when LSP is healthy", () => {
    const view = formatStatusBar({
        projectLabel: "demo",
        lspState: "running",
        binaryStatus: "ok",
        binaryPath: "/bin/tyhp",
        proxyState: "error",
    });
    assert.equal(view.text, "$(warning) Tyhp · demo · proxy error");
    assert.equal(view.warning, true);
    assert.equal(view.error, false);
});

test("status-bar proxy actions cover start/stop/restart", () => {
    assert.deepEqual(proxyStatusActions("stopped"), ["start"]);
    assert.deepEqual(proxyStatusActions("running"), ["stop", "restart"]);
    assert.deepEqual(proxyStatusActions("starting"), ["stop", "restart"]);
    assert.deepEqual(proxyStatusActions("error"), ["start", "restart"]);
});
