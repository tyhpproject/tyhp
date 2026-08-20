import assert from "node:assert/strict";
import { test } from "node:test";
import {
    FakeSpawnedProxy,
    ProxyProcessController,
    stopSpawnedProxy,
} from "./ProxyProcessController";

function createHarness(options?: { listenPort?: number; failListen?: string }) {
    const spawned: FakeSpawnedProxy[] = [];
    const states: string[] = [];
    const controller = new ProxyProcessController({
        spawn: () => {
            const child = new FakeSpawnedProxy();
            spawned.push(child);
            return child;
        },
        waitForListening: async (port, _child, abort) => {
            if (abort.aborted) {
                throw new Error("aborted");
            }
            if (options?.failListen) {
                throw new Error(options.failListen);
            }
            return options?.listenPort ?? port;
        },
        stopProcess: async (child) => {
            child.kill("SIGTERM");
        },
        onState: (state) => states.push(state),
    });
    return { controller, spawned, states };
}

test("start then listening reaches running", async () => {
    const { controller, spawned } = createHarness();
    await controller.start({
        command: "/bin/tyhp",
        args: ["xdebug_proxy", "--ide-port=9003", "--xdebug-port=9004"],
        idePort: 9003,
    });
    assert.equal(controller.currentState, "running");
    assert.equal(spawned.length, 1);
    assert.equal(spawned[0]?.killed, false);
});

test("start while running is a no-op (does not spawn a second process)", async () => {
    const { controller, spawned } = createHarness();
    const request = { command: "/bin/tyhp", args: ["xdebug_proxy"], idePort: 9003 };
    await controller.start(request);
    await controller.start(request);
    assert.equal(spawned.length, 1);
    assert.equal(controller.currentState, "running");
});

test("stop signals SIGTERM via the manager stop path and reaches stopped", async () => {
    const { controller, spawned } = createHarness();
    await controller.start({ command: "/bin/tyhp", args: ["xdebug_proxy"], idePort: 9003 });
    await controller.stop();
    assert.equal(controller.currentState, "stopped");
    assert.equal(spawned[0]?.killed, true);
    assert.equal(spawned[0]?.lastSignal, "SIGTERM");
});

test("restart stops then starts a new process", async () => {
    const { controller, spawned } = createHarness();
    const request = { command: "/bin/tyhp", args: ["xdebug_proxy"], idePort: 9003 };
    await controller.start(request);
    await controller.restart(request);
    assert.equal(controller.currentState, "running");
    assert.equal(spawned.length, 2);
    assert.equal(spawned[0]?.killed, true);
    assert.equal(spawned[1]?.killed, false);
});

test("listen failure leaves error state and stops the child", async () => {
    const { controller, spawned } = createHarness({ failListen: "port in use" });
    await assert.rejects(
        () => controller.start({ command: "/bin/tyhp", args: ["xdebug_proxy"], idePort: 9003 }),
        /port in use/
    );
    assert.equal(controller.currentState, "error");
    assert.equal(spawned[0]?.killed, true);
});

test("unexpected exit from running becomes error", async () => {
    const { controller, spawned } = createHarness();
    await controller.start({ command: "/bin/tyhp", args: ["xdebug_proxy"], idePort: 9003 });
    spawned[0]?.exit(1);
    assert.equal(controller.currentState, "error");
});

test("stopSpawnedProxy uses SIGTERM and waits for exit", async () => {
    const child = new FakeSpawnedProxy();
    await stopSpawnedProxy(child);
    assert.equal(child.killed, true);
    assert.equal(child.lastSignal, "SIGTERM");
    assert.equal(child.exitCode, 0);
});
