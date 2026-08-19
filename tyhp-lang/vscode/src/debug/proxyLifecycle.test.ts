import assert from "node:assert/strict";
import { test } from "node:test";
import { nextProxyState, proxyIsActive, proxyIsListening } from "./proxyLifecycle";

test("start then listening reaches running", () => {
    let state = nextProxyState("stopped", { type: "startRequested" });
    assert.equal(state, "starting");
    state = nextProxyState(state, { type: "listening" });
    assert.equal(state, "running");
    assert.equal(proxyIsListening(state), true);
    assert.equal(proxyIsActive(state), true);
});

test("start while already running is a no-op", () => {
    assert.equal(nextProxyState("running", { type: "startRequested" }), "running");
    assert.equal(nextProxyState("starting", { type: "startRequested" }), "starting");
});

test("start failure from starting becomes error", () => {
    assert.equal(nextProxyState("starting", { type: "startFailed" }), "error");
    assert.equal(nextProxyState("running", { type: "startFailed" }), "running");
});

test("requested stop then expected exit becomes stopped", () => {
    let state = nextProxyState("running", { type: "stopRequested" });
    assert.equal(state, "stopping");
    state = nextProxyState(state, { type: "exited", expected: true });
    assert.equal(state, "stopped");
    assert.equal(proxyIsActive(state), false);
});

test("unexpected exit from running becomes error", () => {
    assert.equal(nextProxyState("running", { type: "exited", expected: false }), "error");
    assert.equal(nextProxyState("starting", { type: "exited", expected: false }), "error");
});

test("stop from stopped stays stopped", () => {
    assert.equal(nextProxyState("stopped", { type: "stopRequested" }), "stopped");
    assert.equal(nextProxyState("stopped", { type: "exited", expected: true }), "stopped");
});

test("cleanup exit after start failure stays error", () => {
    const failed = nextProxyState("starting", { type: "startFailed" });
    assert.equal(failed, "error");
    assert.equal(nextProxyState(failed, { type: "exited", expected: true }), "error");
});
