import assert from "node:assert/strict";
import { test } from "node:test";
import { fileMatchesProject, globToRegExp, matchesGlob } from "./globMatch";

test("strips ./ prefix so ./tyhp_src/**/*.tyhp matches tyhp_src/Type.tyhp", () => {
    assert.equal(matchesGlob("tyhp_src/Type.tyhp", "./tyhp_src/**/*.tyhp", false), true);
    assert.equal(matchesGlob("./tyhp_src/Type.tyhp", "tyhp_src/**/*.tyhp", false), true);
});

test("** matches zero directories (file directly under the prefix)", () => {
    assert.equal(matchesGlob("tyhp_src/Type.tyhp", "tyhp_src/**/*.tyhp", false), true);
    assert.equal(matchesGlob("tyhp_src/nested/deep/Type.tyhp", "tyhp_src/**/*.tyhp", false), true);
});

test("../ include globs match files outside the project directory", () => {
    const relative = "../../php-extensions/php8.2.9/ext/Core.tyhpdef";
    assert.equal(
        matchesGlob(relative, "../../php-extensions/php8.2.9/**/*.tyhpdef", false),
        true
    );
    assert.equal(matchesGlob(relative, "./tyhp_src/**/*.tyhp", false), false);
});

test("literal relative path without ** matches exactly", () => {
    assert.equal(matchesGlob("../core/package.tyhpdef", "../core/package.tyhpdef", false), true);
    assert.equal(matchesGlob("../core/other.tyhpdef", "../core/package.tyhpdef", false), false);
});

test("empty include owns nothing", () => {
    assert.equal(
        fileMatchesProject({
            projectDir: "/repo/runtime/packages/core",
            filePath: "/repo/runtime/packages/core/tyhp_src/Type.tyhp",
            include: [],
            exclude: [],
            caseInsensitive: false,
        }),
        false
    );
});

test("core-style membership: tyhp_src plus parent php-extensions tyhpdef", () => {
    const include = ["./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef"];
    const base = {
        projectDir: "/repo/runtime/packages/core",
        include,
        exclude: [] as string[],
        caseInsensitive: false,
    };
    assert.equal(
        fileMatchesProject({
            ...base,
            filePath: "/repo/runtime/packages/core/tyhp_src/Type.tyhp",
        }),
        true
    );
    assert.equal(
        fileMatchesProject({
            ...base,
            filePath: "/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef",
        }),
        true
    );
    assert.equal(
        fileMatchesProject({
            ...base,
            filePath: "/repo/runtime/packages/core/README.md",
        }),
        false
    );
    assert.equal(
        fileMatchesProject({
            ...base,
            filePath: "/repo/other/Type.tyhp",
        }),
        false
    );
});

test("exclude wins after include", () => {
    assert.equal(
        fileMatchesProject({
            projectDir: "/app",
            filePath: "/app/src/skip.tyhp",
            include: ["./src/**/*.tyhp"],
            exclude: ["./src/skip.tyhp"],
            caseInsensitive: false,
        }),
        false
    );
    assert.equal(
        fileMatchesProject({
            projectDir: "/app",
            filePath: "/app/src/keep.tyhp",
            include: ["./src/**/*.tyhp"],
            exclude: ["./src/skip.tyhp"],
            caseInsensitive: false,
        }),
        true
    );
});

test("Windows case-insensitive matching", () => {
    assert.equal(matchesGlob("Tyhp_Src/Type.tyhp", "./tyhp_src/**/*.tyhp", true), true);
    assert.equal(matchesGlob("Tyhp_Src/Type.tyhp", "./tyhp_src/**/*.tyhp", false), false);
});

test("globToRegExp is anchored (no partial path match)", () => {
    const re = globToRegExp("src/*.tyhp", false);
    assert.equal(re.test("src/a.tyhp"), true);
    assert.equal(re.test("lib/src/a.tyhp"), false);
});
