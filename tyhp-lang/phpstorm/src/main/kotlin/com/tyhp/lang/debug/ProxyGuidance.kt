package com.tyhp.lang.debug

/**
 * User-facing guidance for debug misconfiguration. Links to Story 17/18 docs
 * instead of re-implementing sourcemaps.
 */

const val SOURCEMAP_DOCS_URL =
    "https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_sourcemapGeneration.md"

const val XDEBUG_PROXY_DOCS_URL =
    "https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_xdebugProxy.md"

const val TYHP_PHP_DEBUG_CONFIG_NAME = "Listen for Tyhp (XDebug proxy)"

const val PHP_REMOTE_DEBUG_TYPE_ID = "PhpRemoteDebugRunConfigurationType"

fun isTyhpPhpRemoteDebugConfig(name: String?, typeId: String?): Boolean {
    if (name.isNullOrBlank() || !name.contains("tyhp", ignoreCase = true)) {
        return false
    }
    if (typeId.isNullOrBlank()) {
        return true
    }
    return typeId.equals(PHP_REMOTE_DEBUG_TYPE_ID, ignoreCase = true) ||
        typeId.contains("RemoteDebug", ignoreCase = true) ||
        typeId.contains("php", ignoreCase = true)
}

fun phpRemoteDebugMissingGuidance(): String {
    return (
        "PhpStorm’s built-in PHP Remote Debug (XDebug) is the DBGp client for Tyhp debugging. " +
            "Create a PHP Remote Debug configuration that listens on the proxy IDE port. " +
            "See $XDEBUG_PROXY_DOCS_URL"
        )
}

fun proxyDownGuidance(idePort: Int): String {
    return (
        "The Tyhp XDebug proxy is not listening on IDE port $idePort. " +
            "Run Tools → Tyhp → Start XDebug Proxy (or the status bar), then start debugging. " +
            "Docs: $XDEBUG_PROXY_DOCS_URL"
        )
}

data class SourcemapGuidanceOptions(
    val generateSourcemap: Boolean,
    val mapCount: Int? = null,
    val sourceMapDir: String? = null,
    val outputPath: String? = null,
)

fun sourcemapGuidance(options: SourcemapGuidanceOptions): String? {
    if (!options.generateSourcemap) {
        return (
            "`build.generateSourcemap` is not enabled in `tyhp.json`. Set it to true, run `tyhp build`, " +
                "then start the proxy so breakpoints map to .tyhp sources. Docs: $SOURCEMAP_DOCS_URL"
            )
    }
    if (options.mapCount == 0) {
        val where = options.sourceMapDir ?: options.outputPath ?: "the project output directory"
        return (
            "No `.php.map` files were found in $where. Build the project with sourcemaps enabled " +
                "(`tyhp build`) before debugging .tyhp files. Docs: $SOURCEMAP_DOCS_URL"
            )
    }
    return null
}

fun proxyStartFailedGuidance(detail: String): String {
    return "Tyhp XDebug proxy failed to start: $detail. Check the Tyhp XDebug Proxy tool window. Docs: $XDEBUG_PROXY_DOCS_URL"
}

fun phpIniClientPortGuidance(xdebugPort: Int): String {
    return (
        "Point XDebug `client_port` at the proxy XDebug port $xdebugPort " +
            "(not the IDE / PhpStorm debug port). Docs: $XDEBUG_PROXY_DOCS_URL"
        )
}
