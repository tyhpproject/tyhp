import assert from "node:assert/strict";
import { test } from "node:test";
import { isSessionOwner } from "./sessionOwner";

test("session owns only documents whose owner is this tyhp.json", () => {
    assert.equal(isSessionOwner("/repo/core/tyhp.json", "/repo/core/tyhp.json"), true);
    assert.equal(isSessionOwner("/repo/core/tyhp.json", "/repo/async/tyhp.json"), false);
    assert.equal(isSessionOwner("/repo/core/tyhp.json", undefined), false);
});
