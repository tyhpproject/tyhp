package com.tyhp.lang.status

import com.tyhp.lang.binary.BinaryStatus
import com.tyhp.lang.debug.ProxyRunState
import com.tyhp.lang.lsp.LspClientState

data class StatusBarView(
    val text: String,
    val tooltip: String,
    val error: Boolean,
    val warning: Boolean,
)

data class StatusBarInput(
    val projectLabel: String,
    val lspState: LspClientState,
    val binaryStatus: BinaryStatus,
    val binaryPath: String? = null,
    val binaryMessage: String? = null,
    val proxyState: ProxyRunState = ProxyRunState.STOPPED,
    val proxyDetail: String? = null,
)

/**
 * Compact status-bar copy: `Tyhp` + project / “no project” + LSP + binary health
 * + XDebug proxy listening state.
 */
fun formatStatusBar(input: StatusBarInput): StatusBarView {
    val project = input.projectLabel.trim().ifEmpty { "not in a Tyhp project" }
    val lsp = lspLabel(input.lspState)
    val binaryOk = input.binaryStatus == BinaryStatus.OK
    val proxy = proxyLabel(input.proxyState, input.proxyDetail)
    val tooltipLines = listOf(
        "Project: $project",
        "Language server: $lsp",
        "CLI: ${if (binaryOk) input.binaryPath ?: "ok" else input.binaryMessage ?: "missing"}",
        "XDebug proxy: $proxy",
        "Click for Tyhp actions.",
    )
    val tooltip = tooltipLines.joinToString("\n")

    if (!binaryOk) {
        return StatusBarView(
            text = "Tyhp · $project · CLI missing",
            tooltip = tooltip,
            error = true,
            warning = false,
        )
    }

    if (input.lspState == LspClientState.ERROR) {
        return StatusBarView(
            text = "Tyhp · $project · LSP error",
            tooltip = tooltip,
            error = true,
            warning = false,
        )
    }

    if (input.lspState == LspClientState.STARTING) {
        return StatusBarView(
            text = "Tyhp · $project · starting",
            tooltip = tooltip,
            error = false,
            warning = false,
        )
    }

    if (input.lspState == LspClientState.STOPPED) {
        return StatusBarView(
            text = "Tyhp · $project · LSP stopped",
            tooltip = tooltip,
            error = false,
            warning = isMissingProject(project),
        )
    }

    if (input.proxyState == ProxyRunState.ERROR) {
        return StatusBarView(
            text = "Tyhp · $project · proxy error",
            tooltip = tooltip,
            error = false,
            warning = true,
        )
    }

    return StatusBarView(
        text = "Tyhp · $project · $lsp${proxyTextSuffix(input.proxyState)}",
        tooltip = tooltip,
        error = false,
        warning = isMissingProject(project),
    )
}

/** Status-bar popup actions for the current proxy lifecycle state. */
fun proxyStatusActions(state: ProxyRunState): List<String> {
    val actions = mutableListOf<String>()
    if (state != ProxyRunState.RUNNING && state != ProxyRunState.STARTING) {
        actions.add("start")
    }
    if (state == ProxyRunState.RUNNING || state == ProxyRunState.STARTING || state == ProxyRunState.STOPPING) {
        actions.add("stop")
        actions.add("restart")
    } else if (state == ProxyRunState.ERROR) {
        actions.add("restart")
    }
    return actions
}

private fun isMissingProject(label: String): Boolean =
    label == "not in a Tyhp project" || label == "no project"

private fun lspLabel(state: LspClientState): String = when (state) {
    LspClientState.RUNNING -> "ready"
    LspClientState.STARTING -> "starting"
    LspClientState.ERROR -> "error"
    LspClientState.STOPPED -> "stopped"
}

private fun proxyLabel(state: ProxyRunState, detail: String?): String {
    val extra = detail?.trim()?.takeIf { it.isNotEmpty() }?.let { " ($it)" } ?: ""
    return when (state) {
        ProxyRunState.RUNNING -> "listening$extra"
        ProxyRunState.STARTING -> "starting"
        ProxyRunState.STOPPING -> "stopping"
        ProxyRunState.ERROR -> "error"
        ProxyRunState.STOPPED -> "stopped"
    }
}

private fun proxyTextSuffix(state: ProxyRunState): String = when (state) {
    ProxyRunState.RUNNING -> " · proxy"
    ProxyRunState.STARTING, ProxyRunState.STOPPING -> " · proxy…"
    else -> ""
}
