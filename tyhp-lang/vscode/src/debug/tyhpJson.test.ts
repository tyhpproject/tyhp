import assert from "node:assert/strict";
import { test } from "node:test";
import { parseTyhpJsonProject } from "./tyhpJson";

test("reads xdebugProxy ports, sourcemap dir, ideKey, generateSourcemap, and output.path", () => {
    const snapshot = parseTyhpJsonProject(`{
        "build": { "generateSourcemap": true },
        "output": { "path": "out/" },
        "xdebugProxy": {
            "idePort": 9010,
            "xdebugPort": 9011,
            "sourceMapDir": "./maps",
            "ideKey": "tyhp"
        }
    }`);
    assert.deepEqual(snapshot, {
        generateSourcemap: true,
        outputPath: "out/",
        xdebugProxy: {
            idePort: 9010,
            xdebugPort: 9011,
            sourceMapDir: "./maps",
            ideKey: "tyhp",
        },
    });
});

test("generateSourcemap defaults to false when omitted", () => {
    const snapshot = parseTyhpJsonProject(`{ "output": { "path": "build/" } }`);
    assert.equal(snapshot?.generateSourcemap, false);
    assert.equal(snapshot?.outputPath, "build/");
    assert.equal(snapshot?.xdebugProxy, undefined);
});

test("accepts numeric ports encoded as strings", () => {
    const snapshot = parseTyhpJsonProject(`{
        "xdebugProxy": { "idePort": "9111", "xdebugPort": "9222" }
    }`);
    assert.equal(snapshot?.xdebugProxy?.idePort, 9111);
    assert.equal(snapshot?.xdebugProxy?.xdebugPort, 9222);
});

test("ignores null sourceMapDir and empty strings", () => {
    const snapshot = parseTyhpJsonProject(`{
        "xdebugProxy": { "sourceMapDir": null, "ideKey": "" }
    }`);
    assert.equal(snapshot?.xdebugProxy?.sourceMapDir, undefined);
    assert.equal(snapshot?.xdebugProxy?.ideKey, undefined);
});

test("returns undefined for invalid JSON", () => {
    assert.equal(parseTyhpJsonProject("{"), undefined);
    assert.equal(parseTyhpJsonProject("[]"), undefined);
});
