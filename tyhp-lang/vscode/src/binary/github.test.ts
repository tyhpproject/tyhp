import assert from "node:assert/strict";
import { test } from "node:test";
import { GithubRelease, findAsset, requireAsset, selectLatestRelease } from "./github";

function release(tag: string, draft: boolean, assets: string[]): GithubRelease {
    return {
        tag_name: tag,
        draft,
        prerelease: tag.includes("alpha"),
        published_at: "2026-01-01T00:00:00Z",
        assets: assets.map((name) => ({
            name,
            browser_download_url: `https://github.com/tyhpproject/tyhp/releases/download/${tag}/${name}`,
            size: 10,
        })),
    };
}

test("selectLatestRelease skips drafts and keeps prereleases", () => {
    const releases = [
        release("v805.0.0-alpha.2", true, ["tyhp-osx-arm64"]),
        release("v805.0.0-alpha.1", false, ["tyhp-osx-arm64"]),
    ];
    const latest = selectLatestRelease(releases);
    assert.equal(latest?.tag_name, "v805.0.0-alpha.1");
});

test("requireAsset fails clearly when the platform binary is missing", () => {
    const rel = release("v805.0.0-alpha.1", false, ["checksums.txt"]);
    assert.equal(findAsset(rel, "tyhp-osx-arm64"), undefined);
    assert.throws(() => requireAsset(rel, "tyhp-osx-arm64"), /no asset named/);
    assert.equal(requireAsset(rel, "checksums.txt").name, "checksums.txt");
});
