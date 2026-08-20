import assert from "node:assert/strict";
import { test } from "node:test";
import {
    detectWorkspaceProject,
    projectStatusLabel,
    snapshotFromProjectFile,
} from "./projectDetection";

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
    join: (a: string, b: string) => `${a.replace(/\/$/, "")}/${b}`,
    resolve: (...parts: string[]) => {
        let out = "";
        for (const part of parts) {
            if (part.startsWith("/")) {
                out = part;
            } else {
                out = `${out.replace(/\/$/, "")}/${part}`;
            }
        }
        return out;
    },
    isAbsolute: (target: string) => target.startsWith("/"),
    dirname: (target: string) => {
        const i = target.lastIndexOf("/");
        return i <= 0 ? "/" : target.slice(0, i);
    },
    basename: (target: string) => {
        const i = target.lastIndexOf("/");
        return i < 0 ? target : target.slice(i + 1);
    },
};

test("empty tyhp.projectPath does not treat workspace-root tyhp.json as the owner", () => {
    const snapshot = detectWorkspaceProject(
        {
            configuredPath: "",
            workspaceRoots: ["/empty", "/app"],
            join: posix.join,
            resolve: posix.resolve,
            isAbsolute: posix.isAbsolute,
            fs: fsFrom({
                "/app/tyhp.json": "file",
            }),
        },
        posix
    );
    assert.equal(snapshot.projectFilePath, undefined);
    assert.equal(projectStatusLabel(snapshot), "not in a Tyhp project");
});

test("honors tyhp.projectPath override as a file path", () => {
    const snapshot = detectWorkspaceProject(
        {
            configuredPath: "/proj/custom.json",
            workspaceRoots: ["/ws"],
            join: posix.join,
            resolve: posix.resolve,
            isAbsolute: posix.isAbsolute,
            fs: fsFrom({
                "/proj/custom.json": "file",
                "/ws/tyhp.json": "file",
            }),
        },
        posix
    );
    assert.equal(snapshot.projectFilePath, "/proj/custom.json");
    assert.equal(snapshot.projectName, "proj");
});

test("returns not in a Tyhp project when tyhp.json is absent", () => {
    const snapshot = detectWorkspaceProject(
        {
            configuredPath: "",
            workspaceRoots: ["/ws"],
            join: posix.join,
            resolve: posix.resolve,
            isAbsolute: posix.isAbsolute,
            fs: fsFrom({}),
        },
        posix
    );
    assert.equal(snapshot.projectFilePath, undefined);
    assert.equal(projectStatusLabel(snapshot), "not in a Tyhp project");
});

test("project display name is the directory containing tyhp.json", () => {
    const snapshot = snapshotFromProjectFile("/Users/me/code/demo/tyhp.json", posix);
    assert.equal(snapshot.projectName, "demo");
    assert.equal(projectStatusLabel(snapshot), "demo");
});
