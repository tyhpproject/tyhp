package com.tyhp.lang.debug

/**
 * Argv and port/sourcemap resolution for `tyhp xdebug_proxy`.
 *
 * Flags match `DisplayHelp.XDebugProxyHelp` / `docs/content/cli_xdebugProxy.md`:
 * `--ide-port`, `--xdebug-port`, `--sourcemap-dir`, `--ide-key`, plus global
 * `--tyhp-project`. Do not invent extra switches.
 *
 * Package.json / settings UI defaults must not shadow `tyhp.json`: only
 * **explicitly stored** settings win, then `tyhp.json` `xdebugProxy`, then
 * Story 18 CLI defaults 9003 / 9004.
 */

const val XDEBUG_PROXY_ACTION = "xdebug_proxy"

/** Story 18 / CLI default for the IDE (DBGp client) listen port. */
const val DEFAULT_IDE_PORT = 9003

/** Story 18 / CLI default for the XDebug engine listen port. */
const val DEFAULT_XDEBUG_PORT = 9004

enum class ConfigValueSource {
    SETTINGS,
    TYHP_JSON,
    DEFAULT,
    OMITTED,
}

/**
 * `tyhp.xdebugProxy.*` values the user actually stored (not UI placeholders).
 * Unset fields mean “not explicit”.
 */
data class ExplicitProxySettings(
    val idePort: Int? = null,
    val xdebugPort: Int? = null,
    val sourceMapDir: String? = null,
    val ideKey: String? = null,
)

data class TyhpJsonProxySection(
    val idePort: Int? = null,
    val xdebugPort: Int? = null,
    val sourceMapDir: String? = null,
    val ideKey: String? = null,
)

data class TyhpJsonProjectSnapshot(
    val xdebugProxy: TyhpJsonProxySection? = null,
    val generateSourcemap: Boolean = false,
    val outputPath: String? = null,
)

data class ResolvedProxyLaunch(
    val idePort: Int,
    val xdebugPort: Int,
    val sourceMapDir: String? = null,
    val ideKey: String? = null,
    val generateSourcemap: Boolean = false,
    val outputPath: String? = null,
    val idePortSource: ConfigValueSource = ConfigValueSource.DEFAULT,
    val xdebugPortSource: ConfigValueSource = ConfigValueSource.DEFAULT,
    val sourceMapDirSource: ConfigValueSource = ConfigValueSource.OMITTED,
    val ideKeySource: ConfigValueSource = ConfigValueSource.OMITTED,
)

data class XdebugProxyArgOptions(
    val projectFilePath: String? = null,
    val idePort: Int? = null,
    val xdebugPort: Int? = null,
    val sourceMapDir: String? = null,
    val ideKey: String? = null,
)

data class PickedValue<T>(
    val value: T,
    val source: ConfigValueSource,
)

/**
 * Settings win when explicitly set; otherwise `tyhp.json` `xdebugProxy`;
 * otherwise Story 18 defaults (9003 / 9004). `sourceMapDir` / `ideKey` are
 * omitted from argv when neither source provides them so the CLI can use
 * `output.path` / accept any idekey.
 */
fun resolveProxyLaunch(
    settings: ExplicitProxySettings,
    project: TyhpJsonProjectSnapshot? = null,
): ResolvedProxyLaunch {
    val json = project?.xdebugProxy
    val ide = pickPort(settings.idePort, json?.idePort, DEFAULT_IDE_PORT)
    val xdebug = pickPort(settings.xdebugPort, json?.xdebugPort, DEFAULT_XDEBUG_PORT)
    val sourceMapDir = pickOptionalString(settings.sourceMapDir, json?.sourceMapDir)
    val ideKey = pickOptionalString(settings.ideKey, json?.ideKey)
    return ResolvedProxyLaunch(
        idePort = ide.value,
        xdebugPort = xdebug.value,
        sourceMapDir = sourceMapDir.value,
        ideKey = ideKey.value,
        generateSourcemap = project?.generateSourcemap == true,
        outputPath = nonEmpty(project?.outputPath),
        idePortSource = ide.source,
        xdebugPortSource = xdebug.source,
        sourceMapDirSource = sourceMapDir.source,
        ideKeySource = ideKey.source,
    )
}

/**
 * Returns argv for `tyhp xdebug_proxy` (not including the executable).
 *
 * Exact shape:
 * `xdebug_proxy [--tyhp-project=<file>] [--ide-port=<n>] [--xdebug-port=<n>]
 * [--sourcemap-dir=<path>] [--ide-key=<key>]`
 *
 * Ports are always passed once resolved so the IDE connection matches.
 * Sourcemap dir and ide-key are passed only when known.
 */
fun buildXdebugProxyArgs(options: XdebugProxyArgOptions): List<String> {
    val args = mutableListOf(XDEBUG_PROXY_ACTION)
    val project = options.projectFilePath?.trim().orEmpty()
    if (project.isNotEmpty()) {
        args.add("--tyhp-project=$project")
    }
    if (options.idePort != null) {
        args.add("--ide-port=${options.idePort}")
    }
    if (options.xdebugPort != null) {
        args.add("--xdebug-port=${options.xdebugPort}")
    }
    val sourceMapDir = options.sourceMapDir?.trim().orEmpty()
    if (sourceMapDir.isNotEmpty()) {
        args.add("--sourcemap-dir=$sourceMapDir")
    }
    val ideKey = options.ideKey?.trim().orEmpty()
    if (ideKey.isNotEmpty()) {
        args.add("--ide-key=$ideKey")
    }
    return args
}

fun buildXdebugProxyArgsFromLaunch(
    launch: ResolvedProxyLaunch,
    projectFilePath: String? = null,
): List<String> {
    return buildXdebugProxyArgs(
        XdebugProxyArgOptions(
            projectFilePath = projectFilePath,
            idePort = launch.idePort,
            xdebugPort = launch.xdebugPort,
            sourceMapDir = launch.sourceMapDir,
            ideKey = launch.ideKey,
        ),
    )
}

/** Parse `  IDE port:      9003` (and ephemeral bound ports) from CLI banner lines. */
fun parseBoundIdePort(line: String): Int? {
    val match = IDE_PORT_BANNER.find(line) ?: return null
    return parsePortNumber(match.groupValues[1])
}

fun lineWarnsNoSourcemaps(line: String): Boolean =
    line.contains("no sourcemaps found", ignoreCase = true)

fun isValidPort(value: Int): Boolean = value in 0..65535

fun countPhpMapFiles(names: Iterable<String>): Int =
    names.count { it.endsWith(".php.map", ignoreCase = true) }

/**
 * Parse a settings text field. Empty / whitespace is not explicit (falls through
 * to `tyhp.json` then CLI defaults). Invalid numbers are ignored.
 */
fun parseExplicitPortText(text: String?): Int? {
    val trimmed = text?.trim().orEmpty()
    if (trimmed.isEmpty()) {
        return null
    }
    val value = trimmed.toIntOrNull() ?: return null
    return if (isValidPort(value)) value else null
}

fun parseExplicitString(text: String?): String? = nonEmpty(text)

private fun pickPort(
    settingsValue: Int?,
    jsonValue: Int?,
    fallback: Int,
): PickedValue<Int> {
    if (settingsValue != null && isValidPort(settingsValue)) {
        return PickedValue(settingsValue, ConfigValueSource.SETTINGS)
    }
    if (jsonValue != null && isValidPort(jsonValue)) {
        return PickedValue(jsonValue, ConfigValueSource.TYHP_JSON)
    }
    return PickedValue(fallback, ConfigValueSource.DEFAULT)
}

private fun pickOptionalString(
    settingsValue: String?,
    jsonValue: String?,
): PickedValue<String?> {
    val fromSettings = nonEmpty(settingsValue)
    if (fromSettings != null) {
        return PickedValue(fromSettings, ConfigValueSource.SETTINGS)
    }
    val fromJson = nonEmpty(jsonValue)
    if (fromJson != null) {
        return PickedValue(fromJson, ConfigValueSource.TYHP_JSON)
    }
    return PickedValue(null, ConfigValueSource.OMITTED)
}

private fun nonEmpty(value: String?): String? {
    val trimmed = value?.trim().orEmpty()
    return trimmed.ifEmpty { null }
}

private fun parsePortNumber(raw: String): Int? {
    val value = raw.toIntOrNull() ?: return null
    return if (isValidPort(value)) value else null
}

private val IDE_PORT_BANNER = Regex("IDE port:\\s+(\\d+)", RegexOption.IGNORE_CASE)
