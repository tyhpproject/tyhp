import assert from "node:assert/strict";
import { test } from "node:test";
import {
    DEFAULT_IDE_PORT,
    DEFAULT_XDEBUG_PORT,
    XDEBUG_PROXY_ACTION,
    buildXdebugProxyArgs,
    buildXdebugProxyArgsFromLaunch,
    countPhpMapFiles,
    lineWarnsNoSourcemaps,
    parseBoundIdePort,
    resolveProxyLaunch,
} from "./proxyConfig";

test("default argv is xdebug_proxy with resolved default ports", () => {
    assert.deepEqual(
        buildXdebugProxyArgs({
            idePort: DEFAULT_IDE_PORT,
            xdebugPort: DEFAULT_XDEBUG_PORT,
        }),
        [XDEBUG_PROXY_ACTION, `--ide-port=${DEFAULT_IDE_PORT}`, `--xdebug-port=${DEFAULT_XDEBUG_PORT}`]
    );
});

test("passes --tyhp-project and optional sourcemap/ide-key as inline value flags", () => {
    assert.deepEqual(
        buildXdebugProxyArgs({
            projectFilePath: "/ws/tyhp.json",
            idePort: 9010,
            xdebugPort: 9011,
            sourceMapDir: "./build/",
            ideKey: "tyhp",
        }),
        [
            XDEBUG_PROXY_ACTION,
            "--tyhp-project=/ws/tyhp.json",
            "--ide-port=9010",
            "--xdebug-port=9011",
            "--sourcemap-dir=./build/",
            "--ide-key=tyhp",
        ]
    );
});

test("omits empty project, sourcemap dir, and ide-key", () => {
    assert.deepEqual(
        buildXdebugProxyArgs({
            projectFilePath: "  ",
            idePort: 9003,
            xdebugPort: 9004,
            sourceMapDir: "",
            ideKey: "   ",
        }),
        [XDEBUG_PROXY_ACTION, "--ide-port=9003", "--xdebug-port=9004"]
    );
});

test("settings ports win over tyhp.json", () => {
    const launch = resolveProxyLaunch(
        { idePort: 9111, xdebugPort: 9222 },
        {
            generateSourcemap: true,
            xdebugProxy: { idePort: 9005, xdebugPort: 9006, sourceMapDir: "/from-json" },
        }
    );
    assert.equal(launch.idePort, 9111);
    assert.equal(launch.xdebugPort, 9222);
    assert.equal(launch.idePortSource, "settings");
    assert.equal(launch.xdebugPortSource, "settings");
    assert.equal(launch.sourceMapDir, "/from-json");
    assert.equal(launch.sourceMapDirSource, "tyhp.json");
    assert.equal(launch.generateSourcemap, true);
});

test("tyhp.json ports win when settings are not explicit", () => {
    const launch = resolveProxyLaunch(
        {},
        {
            generateSourcemap: false,
            outputPath: "build/",
            xdebugProxy: { idePort: 9010, xdebugPort: 9011, ideKey: "app" },
        }
    );
    assert.equal(launch.idePort, 9010);
    assert.equal(launch.xdebugPort, 9011);
    assert.equal(launch.idePortSource, "tyhp.json");
    assert.equal(launch.xdebugPortSource, "tyhp.json");
    assert.equal(launch.ideKey, "app");
    assert.equal(launch.ideKeySource, "tyhp.json");
    assert.equal(launch.sourceMapDirSource, "omitted");
    assert.equal(launch.outputPath, "build/");
});

test("Story 18 defaults apply when neither settings nor tyhp.json set ports", () => {
    const launch = resolveProxyLaunch({}, { generateSourcemap: true });
    assert.equal(launch.idePort, DEFAULT_IDE_PORT);
    assert.equal(launch.xdebugPort, DEFAULT_XDEBUG_PORT);
    assert.equal(launch.idePortSource, "default");
    assert.equal(launch.xdebugPortSource, "default");
    assert.equal(launch.generateSourcemap, true);
});

test("settings sourcemap dir wins over tyhp.json", () => {
    const launch = resolveProxyLaunch(
        { sourceMapDir: "/from-settings" },
        {
            generateSourcemap: true,
            xdebugProxy: { sourceMapDir: "/from-json" },
        }
    );
    assert.equal(launch.sourceMapDir, "/from-settings");
    assert.equal(launch.sourceMapDirSource, "settings");
});

test("invalid settings port falls through to tyhp.json then default", () => {
    const fromJson = resolveProxyLaunch(
        { idePort: 70000, xdebugPort: -1 },
        { generateSourcemap: false, xdebugProxy: { idePort: 9010, xdebugPort: 9011 } }
    );
    assert.equal(fromJson.idePort, 9010);
    assert.equal(fromJson.xdebugPort, 9011);

    const fromDefault = resolveProxyLaunch({ idePort: 70000 }, { generateSourcemap: false });
    assert.equal(fromDefault.idePort, DEFAULT_IDE_PORT);
});

test("buildXdebugProxyArgsFromLaunch includes resolved ports and omits empty optionals", () => {
    const launch = resolveProxyLaunch({}, { generateSourcemap: true });
    assert.deepEqual(buildXdebugProxyArgsFromLaunch(launch, "/p/tyhp.json"), [
        XDEBUG_PROXY_ACTION,
        "--tyhp-project=/p/tyhp.json",
        "--ide-port=9003",
        "--xdebug-port=9004",
    ]);
});

test("parses bound IDE port from the CLI startup banner", () => {
    assert.equal(parseBoundIdePort("  IDE port:      9003"), 9003);
    assert.equal(parseBoundIdePort("  IDE port:      0"), 0);
    assert.equal(parseBoundIdePort("  XDebug port:   9004"), undefined);
});

test("detects the CLI no-sourcemaps warning", () => {
    assert.equal(
        lineWarnsNoSourcemaps("No sourcemaps found in `./build/`. Build the project with sourcemap generation enabled first."),
        true
    );
    assert.equal(lineWarnsNoSourcemaps("XDebug Proxy started"), false);
});

test("counts .php.map files case-insensitively", () => {
    assert.equal(countPhpMapFiles(["User.php", "User.php.map", "nested/Foo.PHP.MAP", "readme.md"]), 2);
});
