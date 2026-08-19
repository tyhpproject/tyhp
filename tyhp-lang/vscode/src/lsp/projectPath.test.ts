import assert from "node:assert/strict";
import * as path from "node:path";
import { test } from "node:test";
import { resolveTyhpProjectFile } from "./projectPath";

function fsFrom(files: Record<string, "file" | "dir">) {
    return {
        existsSync(target: string): boolean {
            return Object.prototype.hasOwnProperty.call(files, target);
        },
        isDirectory(target: string): boolean {
            return files[target] === "dir";
        },
    };
}

const posix = {
    join: path.posix.join,
    resolve: path.posix.resolve,
    isAbsolute: path.posix.isAbsolute,
};

test("configured file path wins over workspace-root tyhp.json", () => {
    const resolved = resolveTyhpProjectFile({
        configuredPath: "/proj/custom.json",
        workspaceRoots: ["/ws"],
        ...posix,
        fs: fsFrom({
            "/proj/custom.json": "file",
            "/ws/tyhp.json": "file",
        }),
    });
    assert.equal(resolved, "/proj/custom.json");
});

test("configured directory uses tyhp.json inside it", () => {
    const resolved = resolveTyhpProjectFile({
        configuredPath: "/proj",
        workspaceRoots: ["/ws"],
        ...posix,
        fs: fsFrom({
            "/proj": "dir",
            "/proj/tyhp.json": "file",
        }),
    });
    assert.equal(resolved, "/proj/tyhp.json");
});

test("relative configured path is resolved against the first workspace root", () => {
    const resolved = resolveTyhpProjectFile({
        configuredPath: "config/tyhp.json",
        workspaceRoots: ["/ws", "/other"],
        ...posix,
        fs: fsFrom({
            "/ws/config/tyhp.json": "file",
        }),
    });
    assert.equal(resolved, "/ws/config/tyhp.json");
});

test("missing configured path does not invent a flag value", () => {
    const resolved = resolveTyhpProjectFile({
        configuredPath: "/missing/tyhp.json",
        workspaceRoots: ["/ws"],
        ...posix,
        fs: fsFrom({
            "/ws/tyhp.json": "file",
        }),
    });
    assert.equal(resolved, undefined);
});

test("empty setting does not fall back to workspace-root tyhp.json", () => {
    const resolved = resolveTyhpProjectFile({
        configuredPath: "",
        workspaceRoots: ["/empty", "/app"],
        ...posix,
        fs: fsFrom({
            "/app/tyhp.json": "file",
        }),
    });
    assert.equal(resolved, undefined);
});

test("returns undefined when no project file is known", () => {
    const resolved = resolveTyhpProjectFile({
        configuredPath: "  ",
        workspaceRoots: ["/ws"],
        ...posix,
        fs: fsFrom({}),
    });
    assert.equal(resolved, undefined);
});
