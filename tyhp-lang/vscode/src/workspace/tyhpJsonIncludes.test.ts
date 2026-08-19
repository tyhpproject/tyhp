import assert from "node:assert/strict";
import { test } from "node:test";
import { parseTyhpJsonGlobs } from "./tyhpJsonIncludes";

test("reads include and exclude arrays", () => {
    const globs = parseTyhpJsonGlobs(`{
        "include": ["./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef"],
        "exclude": ["./skip/**"]
    }`);
    assert.deepEqual(globs.include, [
        "./tyhp_src/**/*.tyhp",
        "../../php-extensions/php8.2.9/**/*.tyhpdef",
    ]);
    assert.deepEqual(globs.exclude, ["./skip/**"]);
});

test("missing include is empty (owns nothing)", () => {
    const globs = parseTyhpJsonGlobs(`{ "exclude": [] }`);
    assert.deepEqual(globs.include, []);
    assert.deepEqual(globs.exclude, []);
});

test("invalid JSON is treated as empty include", () => {
    const globs = parseTyhpJsonGlobs("{ not json");
    assert.deepEqual(globs.include, []);
    assert.deepEqual(globs.exclude, []);
});

test("non-object JSON is treated as empty include", () => {
    assert.deepEqual(parseTyhpJsonGlobs("[]").include, []);
    assert.deepEqual(parseTyhpJsonGlobs("null").include, []);
    assert.deepEqual(parseTyhpJsonGlobs('"x"').include, []);
});

test("drops blank include entries", () => {
    const globs = parseTyhpJsonGlobs(`{ "include": ["", "  ", "./src/**/*.tyhp", 1] }`);
    assert.deepEqual(globs.include, ["./src/**/*.tyhp"]);
});
