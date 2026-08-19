package com.tyhp.lang.workspace

/** POSIX-style path helpers so membership matches `tyhp build` on every OS. */

fun toPosix(path: String): String = path.replace('\\', '/')

fun stripDotSlash(path: String): String {
    var out = toPosix(path).trim()
    while (out.startsWith("./")) {
        out = out.substring(2)
    }
    return out
}

fun posixNormalize(path: String): String {
    val posix = toPosix(path)
    val absolute = posix.startsWith("/")
    val parts = posix.split('/').filter { it.isNotEmpty() && it != "." }
    val stack = ArrayDeque<String>()
    for (part in parts) {
        if (part == "..") {
            if (stack.isNotEmpty() && stack.last() != "..") {
                stack.removeLast()
            } else if (!absolute) {
                stack.addLast("..")
            }
        } else {
            stack.addLast(part)
        }
    }
    val joined = stack.joinToString("/")
    return if (absolute) "/$joined" else joined.ifEmpty { "." }
}

fun posixDirname(path: String): String {
    val n = toPosix(path).trimEnd('/')
    val i = n.lastIndexOf('/')
    return when {
        i < 0 -> "."
        i == 0 -> "/"
        else -> n.substring(0, i)
    }
}

fun posixBasename(path: String): String {
    val n = toPosix(path).trimEnd('/')
    val i = n.lastIndexOf('/')
    return if (i < 0) n else n.substring(i + 1)
}

private fun splitAbs(path: String): List<String> {
    val n = posixNormalize(path)
    if (n == "/") {
        return emptyList()
    }
    return n.trimStart('/').split('/').filter { it.isNotEmpty() }
}

fun posixRelative(fromDir: String, toPath: String, caseInsensitive: Boolean = false): String {
    val from = splitAbs(fromDir)
    val to = splitAbs(toPath)
    var i = 0
    val n = minOf(from.size, to.size)
    while (i < n) {
        val left = if (caseInsensitive) from[i].lowercase() else from[i]
        val right = if (caseInsensitive) to[i].lowercase() else to[i]
        if (left != right) {
            break
        }
        i += 1
    }
    val ups = List(from.size - i) { ".." }
    val down = to.drop(i)
    val parts = ups + down
    return parts.joinToString("/").ifEmpty { "." }
}

fun pathHops(fromDir: String, toPath: String, caseInsensitive: Boolean = false): Int {
    val rel = posixRelative(fromDir, toPath, caseInsensitive)
    if (rel == "." || rel.isEmpty()) {
        return 0
    }
    return rel.split('/').count { it.isNotEmpty() }
}

fun isPathInside(dir: String, filePath: String, caseInsensitive: Boolean = false): Boolean {
    val rel = posixRelative(dir, filePath, caseInsensitive)
    return rel != ".." && !rel.startsWith("../") && rel != "."
}

fun hasAncestorTyhpJson(
    filePath: String,
    workspaceRoot: String?,
    exists: (String) -> Boolean,
    join: (String, String) -> String = { dir, name -> "${toPosix(dir).trimEnd('/')}/$name" },
): Boolean {
    if (workspaceRoot.isNullOrBlank()) {
        return false
    }
    val stop = posixNormalize(workspaceRoot)
    var dir = posixDirname(filePath)
    val seen = HashSet<String>()
    while (seen.add(dir)) {
        if (exists(join(dir, "tyhp.json"))) {
            return true
        }
        val normalized = posixNormalize(dir)
        if (normalized == stop) {
            break
        }
        if (stop != "/" && !isPathInside(stop, dir) && normalized != stop) {
            break
        }
        val parent = posixDirname(dir)
        if (parent == dir) {
            break
        }
        dir = parent
    }
    return false
}

val INDEX_SKIP_DIR_NAMES: Set<String> = setOf("node_modules", "vendor", ".git", "bin", "obj", "dist", "build")

fun shouldSkipIndexedTyhpJson(filePath: String): Boolean =
    toPosix(filePath).split('/').any { it in INDEX_SKIP_DIR_NAMES }

fun matchingWorkspaceRoot(
    filePath: String,
    workspaceRoots: List<String>,
    caseInsensitive: Boolean = false,
): String? {
    val matches = workspaceRoots.filter { root ->
        val n = posixNormalize(root)
        val file = posixNormalize(filePath)
        if (caseInsensitive) {
            file.lowercase() == n.lowercase() || isPathInside(n, file, true)
        } else {
            file == n || isPathInside(n, file, false)
        }
    }
    return matches.maxByOrNull { it.length }
}
