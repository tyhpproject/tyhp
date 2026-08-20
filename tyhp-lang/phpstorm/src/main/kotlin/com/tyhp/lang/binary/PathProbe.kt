package com.tyhp.lang.binary

import java.nio.file.Files
import java.nio.file.Path

fun interface PathProbeFs {
    fun isFile(filePath: Path): Boolean
}

private val defaultFs = PathProbeFs { filePath ->
    try {
        Files.isRegularFile(filePath)
    } catch (_: Exception) {
        false
    }
}

data class PathProbeOptions(
    val pathEnv: String? = null,
    val pathDelimiter: String = System.getProperty("path.separator") ?: ":",
    val platform: HostPlatform? = null,
    val fs: PathProbeFs = defaultFs,
    val homedir: String = System.getProperty("user.home") ?: "",
)

/**
 * Search PATH for a `tyhp` executable. Returns an absolute path, or null.
 */
fun probeTyhpOnPath(options: PathProbeOptions = PathProbeOptions()): String? {
    val platform = options.platform ?: detectHostPlatform()
    val pathEnv = options.pathEnv ?: System.getenv("PATH") ?: System.getenv("Path") ?: ""
    if (pathEnv.trim().isEmpty()) {
        return null
    }
    val names = pathProbeNames(platform)
    for (rawDir in pathEnv.split(options.pathDelimiter)) {
        val trimmed = rawDir.trim().removeSurrounding("\"")
        if (trimmed.isEmpty()) {
            continue
        }
        for (name in names) {
            val candidate = Path.of(trimmed, name).toAbsolutePath().normalize()
            if (options.fs.isFile(candidate)) {
                return candidate.toString()
            }
        }
    }
    return null
}

fun lookUpCommandOnPath(command: String, options: PathProbeOptions = PathProbeOptions()): String? {
    val platform = options.platform ?: detectHostPlatform()
    val pathEnv = options.pathEnv ?: System.getenv("PATH") ?: System.getenv("Path") ?: ""
    val names = if (platform.os == OsId.WIN && !command.endsWith(".exe", ignoreCase = true)) {
        listOf("$command.exe", command)
    } else {
        listOf(command)
    }
    for (rawDir in pathEnv.split(options.pathDelimiter)) {
        val trimmed = rawDir.trim().removeSurrounding("\"")
        if (trimmed.isEmpty()) {
            continue
        }
        for (name in names) {
            val candidate = Path.of(trimmed, name).toAbsolutePath().normalize()
            if (options.fs.isFile(candidate)) {
                return candidate.toString()
            }
        }
    }
    return null
}

fun expandHome(filePath: String, homedir: String): String {
    if (filePath == "~") {
        return homedir
    }
    if (filePath.startsWith("~/") || filePath.startsWith("~\\")) {
        return homedir + filePath.substring(1)
    }
    return filePath
}

data class ExecutableCheck(
    val ok: Boolean,
    val absolutePath: String? = null,
    val message: String? = null,
)

/**
 * Validate a `tyhp.path` value: absolute path, `~/…`, or a command on PATH.
 */
fun validateTyhpPath(configured: String, options: PathProbeOptions = PathProbeOptions()): ExecutableCheck {
    val trimmed = configured.trim()
    if (trimmed.isEmpty()) {
        return ExecutableCheck(ok = false, message = "`tyhp.path` is empty")
    }

    val expanded = expandHome(trimmed, options.homedir)
    val looksLikePath =
        Path.of(expanded).isAbsolute || expanded.contains('/') || expanded.contains('\\')

    var resolved: String?
    if (looksLikePath) {
        resolved = Path.of(expanded).toAbsolutePath().normalize().toString()
        if (!options.fs.isFile(Path.of(resolved))) {
            return ExecutableCheck(
                ok = false,
                absolutePath = resolved,
                message = "Tyhp CLI at `$resolved` is missing or is not a file. Use “Tyhp: Install / Update CLI” or fix `tyhp.path`.",
            )
        }
    } else {
        resolved = lookUpCommandOnPath(trimmed, options)
        if (resolved == null) {
            return ExecutableCheck(
                ok = false,
                message = "Command `$trimmed` from `tyhp.path` was not found on PATH. Use “Tyhp: Install / Update CLI” or set an absolute path.",
            )
        }
    }

    resolved = try {
        val path = Path.of(resolved)
        if (Files.exists(path)) {
            path.toRealPath().toString()
        } else {
            resolved
        }
    } catch (_: Exception) {
        resolved
    }

    return ExecutableCheck(ok = true, absolutePath = resolved)
}
