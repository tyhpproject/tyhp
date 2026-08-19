package com.tyhp.lang.settings

/**
 * Pure helpers for `tyhp.*` settings. Keep IntelliJ APIs out of this file so unit
 * tests can run as plain JVM tests without `runIde`.
 *
 * Key names match the VS Code contribution points (`tyhp.path`, `tyhp.binary.*`, …).
 */

enum class InstallMode(val value: String) {
    PATH("path"),
    GLOBAL("global"),
    EXTENSION("extension");

    companion object {
        fun parse(value: String?): InstallMode =
            entries.firstOrNull { it.value == value } ?: PATH
    }
}

/** Where to persist `tyhp.path` after a PATH probe or install. */
enum class PathWriteTarget {
    Application,
    Project,
}

/**
 * Snapshot of `tyhp.path` at each scope. A non-null [projectValue] means the
 * project has an override (even if the string is empty), matching VS Code’s
 * `inspect().workspaceValue !== undefined`.
 */
data class InspectedPath(
    val applicationValue: String? = null,
    val projectValue: String? = null,
)

fun isPathUnset(value: String?): Boolean = value == null || value.trim().isEmpty()

fun parseInstallMode(value: String?): InstallMode = InstallMode.parse(value)

/**
 * Effective `tyhp.path`: a project override wins even when it is the empty
 * string (matching VS Code `inspect().workspaceValue !== undefined`).
 */
fun effectiveTyhpPath(inspect: InspectedPath): String {
    val project = inspect.projectValue
    if (project != null) {
        return project.trim()
    }
    return (inspect.applicationValue ?: "").trim()
}

/**
 * User (application) by default unless a project override already exists.
 */
fun pathWriteTarget(inspect: InspectedPath?): PathWriteTarget {
    if (inspect?.projectValue != null) {
        return PathWriteTarget.Project
    }
    return PathWriteTarget.Application
}

fun normalizePinnedVersion(value: String?): String = (value ?: "").trim()

/** GitHub tags are `vX.Y.Z…`; settings may omit the `v`. */
fun normalizeReleaseTag(value: String): String {
    val trimmed = value.trim()
    if (trimmed.isEmpty()) {
        return ""
    }
    return if (trimmed.startsWith("v") || trimmed.startsWith("V")) trimmed else "v$trimmed"
}

fun tagsMatch(a: String, b: String): Boolean {
    val left = normalizeReleaseTag(a)
    val right = normalizeReleaseTag(b)
    if (left.isEmpty() || right.isEmpty()) {
        return false
    }
    return left.equals(right, ignoreCase = true)
}
