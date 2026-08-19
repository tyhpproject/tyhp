package com.tyhp.lang.debug

/**
 * Documented / contributed PHP Remote Debug shape. PhpStorm’s built-in XDebug
 * is the DBGp client — this plugin does not reimplement DBGp.
 *
 * - PhpStorm **Debug port** (Settings → PHP → Debug → Xdebug) = proxy **IDE port**
 *   (Story 18 default 9003)
 * - XDebug `client_port` = proxy **XDebug port** (Story 18 default 9004)
 */

data class PhpRemoteDebugPlan(
    val configurationName: String,
    /** PhpStorm Xdebug debug port — the proxy IDE listen port. */
    val debugPort: Int,
    /** XDebug engine `client_port` — the proxy XDebug listen port. */
    val xdebugClientPort: Int,
    val ideKey: String?,
    val phpIniSnippet: String,
    val setupSteps: List<String>,
)

fun phpRemoteDebugPlan(launch: ResolvedProxyLaunch): PhpRemoteDebugPlan {
    val ideKey = launch.ideKey?.trim()?.takeIf { it.isNotEmpty() }
    val phpIni = buildString {
        appendLine("xdebug.mode = debug")
        appendLine("xdebug.client_host = 127.0.0.1")
        append("xdebug.client_port = ${launch.xdebugPort}")
        if (ideKey != null) {
            appendLine()
            append("xdebug.idekey = $ideKey")
        }
    }
    val steps = listOf(
        "Enable `build.generateSourcemap` in `tyhp.json` and run `tyhp build` so `.php.map` files exist. $SOURCEMAP_DOCS_URL",
        "Start the Tyhp XDebug proxy (Tools → Tyhp → Start XDebug Proxy). It listens on IDE port ${launch.idePort} and XDebug port ${launch.xdebugPort}.",
        "Set Settings → PHP → Debug → Xdebug → Debug port to ${launch.idePort} (the proxy IDE port, not $DEFAULT_XDEBUG_PORT).",
        "Create or run a PHP Remote Debug configuration named “$TYHP_PHP_DEBUG_CONFIG_NAME” (PhpStorm’s built-in XDebug; filter/idekey ${ideKey ?: "(any)"}).",
        "Point XDebug `client_port` at ${launch.xdebugPort}. $XDEBUG_PROXY_DOCS_URL",
        "Set a breakpoint in a `.tyhp` file, then run the compiled PHP. The proxy maps hits back to Tyhp sources.",
    )
    return PhpRemoteDebugPlan(
        configurationName = TYHP_PHP_DEBUG_CONFIG_NAME,
        debugPort = launch.idePort,
        xdebugClientPort = launch.xdebugPort,
        ideKey = ideKey,
        phpIniSnippet = phpIni,
        setupSteps = steps,
    )
}

fun phpRemoteDebugSummary(plan: PhpRemoteDebugPlan): String {
    return (
        "PHP Remote Debug “${plan.configurationName}”: PhpStorm debug port ${plan.debugPort} " +
            "(proxy IDE), XDebug client_port ${plan.xdebugClientPort}. " +
            "Idekey: ${plan.ideKey ?: "(any)"}."
        )
}
