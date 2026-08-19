package com.tyhp.lang.workspace

interface OwnerCandidate {
    val projectFilePath: String
    val projectDir: String
}

data class SimpleOwner(
    override val projectFilePath: String,
    override val projectDir: String,
) : OwnerCandidate

private fun comparePathThenLex(a: OwnerCandidate, b: OwnerCandidate): Int {
    val byLength = a.projectFilePath.length.compareTo(b.projectFilePath.length)
    if (byLength != 0) {
        return byLength
    }
    return a.projectFilePath.compareTo(b.projectFilePath)
}

private fun pickBest(candidates: List<OwnerCandidate>): OwnerCandidate =
    candidates.minWith(Comparator(::comparePathThenLex))

private fun nearestAncestor(
    filePath: String,
    candidates: List<OwnerCandidate>,
    caseInsensitive: Boolean,
): OwnerCandidate? {
    val ancestors = candidates.filter { isPathInside(it.projectDir, filePath, caseInsensitive) }
    if (ancestors.isEmpty()) {
        return null
    }
    val longest = ancestors.maxOf { posixNormalize(it.projectDir).length }
    val tied = ancestors.filter { posixNormalize(it.projectDir).length == longest }
    return if (tied.size == 1) tied[0] else pickBest(tied)
}

fun selectOwner(
    filePath: String,
    candidates: List<OwnerCandidate>,
    caseInsensitive: Boolean = false,
): OwnerCandidate? {
    if (candidates.isEmpty()) {
        return null
    }
    if (candidates.size == 1) {
        return candidates[0]
    }
    nearestAncestor(filePath, candidates, caseInsensitive)?.let { return it }

    var minHops = Int.MAX_VALUE
    val nearest = ArrayList<OwnerCandidate>()
    for (candidate in candidates) {
        val hops = pathHops(candidate.projectDir, filePath, caseInsensitive)
        if (hops < minHops) {
            minHops = hops
            nearest.clear()
            nearest.add(candidate)
        } else if (hops == minHops) {
            nearest.add(candidate)
        }
    }
    return if (nearest.size == 1) nearest[0] else pickBest(nearest)
}
