/**
 * In-memory index of discovered `tyhp.json` projects. VS Code-free: given a
 * list of projects and a file path, return the single owner (or none).
 */

import { fileMatchesProject } from "./globMatch";
import { posixBasename, posixDirname, toPosix } from "./pathUtils";
import { OwnerCandidate, selectOwner } from "./selectOwner";
import { parseTyhpJsonGlobs } from "./tyhpJsonIncludes";

export interface IndexedProject extends OwnerCandidate {
    readonly projectName: string;
    readonly include: readonly string[];
    readonly exclude: readonly string[];
}

export interface ProjectIndexSnapshot {
    readonly projectFilePath: string | undefined;
    readonly projectDir: string | undefined;
    readonly projectName: string | undefined;
}

export function snapshotFromOwner(owner: IndexedProject | undefined): ProjectIndexSnapshot {
    if (!owner) {
        return {
            projectFilePath: undefined,
            projectDir: undefined,
            projectName: undefined,
        };
    }
    return {
        projectFilePath: owner.projectFilePath,
        projectDir: owner.projectDir,
        projectName: owner.projectName,
    };
}

export function indexedProjectFromJson(projectFilePath: string, raw: string): IndexedProject {
    const globs = parseTyhpJsonGlobs(raw);
    const projectDir = posixDirname(toPosix(projectFilePath));
    return {
        projectFilePath,
        projectDir,
        projectName: posixBasename(projectDir),
        include: globs.include,
        exclude: globs.exclude,
    };
}

export class ProjectIndex {
    constructor(
        readonly projects: readonly IndexedProject[],
        readonly caseInsensitive: boolean
    ) {}

    ownerOf(filePath: string): IndexedProject | undefined {
        const posixFile = toPosix(filePath);
        const matches = this.projects.filter((project) =>
            fileMatchesProject({
                projectDir: project.projectDir,
                filePath: posixFile,
                include: project.include,
                exclude: project.exclude,
                caseInsensitive: this.caseInsensitive,
            })
        );
        return selectOwner(posixFile, matches, this.caseInsensitive) as IndexedProject | undefined;
    }
}

export const EMPTY_PROJECT_INDEX = new ProjectIndex([], false);
