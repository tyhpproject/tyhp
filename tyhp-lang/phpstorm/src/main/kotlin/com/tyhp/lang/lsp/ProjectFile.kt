package com.tyhp.lang.lsp

/**
 * Resolves the `tyhp.json` path passed as `--tyhp-project`.
 * The CLI requires a file (see `CliStartup.TryValidateProjectFile`).
 */

const val TYHP_PROJECT_FILE = "tyhp.json"

interface ProjectFileFs {
    fun exists(path: String): Boolean
    fun isDirectory(path: String): Boolean
}

data class ResolveProjectFileOptions(
    /** `tyhp.projectPath` setting (file or directory; relative or absolute). */
    val configuredPath: String,
    /** Content-root filesystem paths, in IDE order. */
    val contentRoots: List<String>,
    val join: (String, String) -> String,
    val resolve: (String, String) -> String,
    val isAbsolute: (String) -> Boolean,
    val fs: ProjectFileFs,
)

private fun expandConfigured(
    configuredPath: String,
    contentRoots: List<String>,
    resolve: (String, String) -> String,
    isAbsolute: (String) -> Boolean,
): String {
    val trimmed = configuredPath.trim()
    if (isAbsolute(trimmed) || contentRoots.isEmpty()) {
        return trimmed
    }
    return resolve(contentRoots[0], trimmed)
}

private fun projectFileIn(dir: String, join: (String, String) -> String): String =
    join(dir, TYHP_PROJECT_FILE)

/**
 * Resolves `tyhp.projectPath` to a `tyhp.json` **file** for `--tyhp-project`.
 * When the setting is empty, returns `null` — callers must index workspace
 * `tyhp.json` files and match `include`/`exclude` instead of assuming a
 * content-root project.
 */
fun resolveTyhpProjectFile(options: ResolveProjectFileOptions): String? {
    val configured = options.configuredPath.trim()
    if (configured.isEmpty()) {
        return null
    }

    val expanded = expandConfigured(
        configured,
        options.contentRoots,
        options.resolve,
        options.isAbsolute,
    )
    if (options.fs.exists(expanded)) {
        if (options.fs.isDirectory(expanded)) {
            val nested = projectFileIn(expanded, options.join)
            return if (options.fs.exists(nested)) nested else null
        }
        return expanded
    }
    return null
}

fun isForcedProjectPath(configuredPath: String): Boolean = configuredPath.trim().isNotEmpty()

/** Parent directory of [path], accepting `/` or `\\` separators (test-friendly). */
fun parentDirectory(path: String): String? {
    val idx = path.lastIndexOfAny(charArrayOf('/', '\\'))
    if (idx <= 0) {
        return null
    }
    return path.substring(0, idx)
}

/** Working directory for the language-server process. */
fun serverWorkingDirectory(projectFilePath: String?, contentRoots: List<String>): String? {
    val file = projectFilePath?.trim().orEmpty()
    if (file.isNotEmpty()) {
        return parentDirectory(file)
    }
    return contentRoots.firstOrNull()?.trim()?.takeIf { it.isNotEmpty() }
}

fun languageServerKey(
    command: String,
    args: List<String>,
    cwd: String?,
    extra: List<String> = emptyList(),
): String =
    listOf(command, args.joinToString("\u0000"), cwd.orEmpty(), extra.joinToString("\u0000"))
        .joinToString("\u0001")
