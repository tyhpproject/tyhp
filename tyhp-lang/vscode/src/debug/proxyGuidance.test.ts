import assert from "node:assert/strict";
import { test } from "node:test";
import {
    PHP_DEBUG_EXTENSION_ID,
    TYHP_PHP_DEBUG_CONFIG_NAME,
    isTyhpPhpDebugConfig,
    phpDebugMissingGuidance,
    proxyDownGuidance,
    sourcemapGuidance,
} from "./proxyGuidance";

test("Tyhp launch configs are php configs whose name mentions Tyhp", () => {
    assert.equal(
        isTyhpPhpDebugConfig({ type: "php", name: TYHP_PHP_DEBUG_CONFIG_NAME }),
        true
    );
    assert.equal(isTyhpPhpDebugConfig({ type: "php", name: "Listen for Xdebug" }), false);
    assert.equal(isTyhpPhpDebugConfig({ type: "node", name: "Listen for Tyhp" }), false);
});

test("missing PHP Debug guidance names the extension id", () => {
    assert.match(phpDebugMissingGuidance(), new RegExp(PHP_DEBUG_EXTENSION_ID));
});

test("proxy-down guidance names the IDE port and start command", () => {
    const text = proxyDownGuidance(9003);
    assert.match(text, /9003/);
    assert.match(text, /Start XDebug Proxy/);
});

test("sourcemap guidance when generateSourcemap is off", () => {
    const text = sourcemapGuidance({ generateSourcemap: false });
    assert.ok(text);
    assert.match(text ?? "", /generateSourcemap/);
});

test("sourcemap guidance when maps are missing after a build flag is on", () => {
    const text = sourcemapGuidance({
        generateSourcemap: true,
        mapCount: 0,
        sourceMapDir: "./build/",
    });
    assert.ok(text);
    assert.match(text ?? "", /\.php\.map/);
    assert.match(text ?? "", /build/);
});

test("no sourcemap guidance when maps are present", () => {
    assert.equal(
        sourcemapGuidance({ generateSourcemap: true, mapCount: 3 }),
        undefined
    );
});
