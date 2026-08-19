/**
 * Single-owner selection when more than one `tyhp.json` include-matches a file.
 * Do not merge two language servers (they are different type worlds).
 *
 * Tie-break:
 * 1. Matching `tyhp.json` that is the nearest ancestor of the file
 * 2. Else nearest `tyhp.json` by path hops
 * 3. Else shortest `tyhp.json` path, then lexicographic
 */

import { isPathInside, pathHops, posixNormalize } from "./pathUtils";

export interface OwnerCandidate {
    readonly projectFilePath: string;
    readonly projectDir: string;
}

function comparePathThenLex(a: OwnerCandidate, b: OwnerCandidate): number {
    const la = a.projectFilePath.length;
    const lb = b.projectFilePath.length;
    if (la !== lb) {
        return la - lb;
    }
    if (a.projectFilePath < b.projectFilePath) {
        return -1;
    }
    if (a.projectFilePath > b.projectFilePath) {
        return 1;
    }
    return 0;
}

function pickBest(candidates: OwnerCandidate[]): OwnerCandidate {
    return [...candidates].sort(comparePathThenLex)[0];
}

function nearestAncestor(filePath: string, candidates: readonly OwnerCandidate[], caseInsensitive: boolean): OwnerCandidate | undefined {
    const ancestors = candidates.filter((c) => isPathInside(c.projectDir, filePath, caseInsensitive));
    if (ancestors.length === 0) {
        return undefined;
    }
    let best = ancestors[0];
    let bestLen = posixNormalize(best.projectDir).length;
    const tied: OwnerCandidate[] = [best];
    for (let i = 1; i < ancestors.length; i++) {
        const cur = ancestors[i];
        const len = posixNormalize(cur.projectDir).length;
        if (len > bestLen) {
            best = cur;
            bestLen = len;
            tied.length = 0;
            tied.push(cur);
        } else if (len === bestLen) {
            tied.push(cur);
        }
    }
    return tied.length === 1 ? tied[0] : pickBest(tied);
}

export function selectOwner(
    filePath: string,
    candidates: readonly OwnerCandidate[],
    caseInsensitive = false
): OwnerCandidate | undefined {
    if (candidates.length === 0) {
        return undefined;
    }
    if (candidates.length === 1) {
        return candidates[0];
    }

    const ancestor = nearestAncestor(filePath, candidates, caseInsensitive);
    if (ancestor) {
        return ancestor;
    }

    let minHops = Number.POSITIVE_INFINITY;
    const nearest: OwnerCandidate[] = [];
    for (const candidate of candidates) {
        const hops = pathHops(candidate.projectDir, filePath, caseInsensitive);
        if (hops < minHops) {
            minHops = hops;
            nearest.length = 0;
            nearest.push(candidate);
        } else if (hops === minHops) {
            nearest.push(candidate);
        }
    }
    return nearest.length === 1 ? nearest[0] : pickBest(nearest);
}
