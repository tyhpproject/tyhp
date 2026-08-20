package com.tyhp.lang.workspace

private val REGEX_ESCAPE = Regex("""[.+^${'$'}{}()|\[\]\\]""")

fun globToRegExp(glob: String, caseInsensitive: Boolean): Regex {
    val pattern = stripDotSlash(glob)
    val regex = StringBuilder("^")
    var i = 0
    while (i < pattern.length) {
        val c = pattern[i]
        if (c == '*' && i + 1 < pattern.length && pattern[i + 1] == '*') {
            val after = pattern.getOrNull(i + 2)
            if (after == '/' || after == null) {
                if (after == '/') {
                    regex.append("(?:.*/)?")
                    i += 3
                } else {
                    regex.append(".*")
                    i += 2
                }
            } else {
                regex.append(".*")
                i += 2
            }
        } else if (c == '*') {
            regex.append("[^/]*")
            i += 1
        } else if (c == '?') {
            regex.append("[^/]")
            i += 1
        } else {
            regex.append(REGEX_ESCAPE.replace(c.toString(), "\\\\$0"))
            i += 1
        }
    }
    regex.append('$')
    val options = if (caseInsensitive) setOf(RegexOption.IGNORE_CASE) else emptySet()
    return Regex(regex.toString(), options)
}

fun matchesGlob(relativePath: String, glob: String, caseInsensitive: Boolean): Boolean {
    val rel = stripDotSlash(toPosix(relativePath))
    return globToRegExp(glob, caseInsensitive).matches(rel)
}

fun fileMatchesProject(
    projectDir: String,
    filePath: String,
    include: List<String>,
    exclude: List<String>,
    caseInsensitive: Boolean,
): Boolean {
    if (include.isEmpty()) {
        return false
    }
    val relative = posixRelative(projectDir, filePath, caseInsensitive)
    if (relative == ".") {
        return false
    }
    val included = include.any { matchesGlob(relative, it, caseInsensitive) }
    if (!included) {
        return false
    }
    return exclude.none { matchesGlob(relative, it, caseInsensitive) }
}
