package com.tyhp.lang.status

import com.tyhp.lang.binary.BinaryStatus
import com.tyhp.lang.debug.ProxyRunState
import com.tyhp.lang.lsp.LspClientState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class StatusBarModelTest {
    @Test
    fun `healthy project and running LSP`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.RUNNING,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/usr/local/bin/tyhp",
            ),
        )
        assertEquals("Tyhp · demo · ready", view.text)
        assertFalse(view.error)
        assertFalse(view.warning)
        assertTrue(view.tooltip.contains("/usr/local/bin/tyhp"))
        assertTrue(view.tooltip.contains("XDebug proxy: stopped"))
    }

    @Test
    fun `binary missing uses CLI missing copy`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.ERROR,
                binaryStatus = BinaryStatus.MISSING,
                binaryMessage = "Tyhp CLI was not found.",
            ),
        )
        assertEquals("Tyhp · demo · CLI missing", view.text)
        assertTrue(view.error)
        assertTrue(view.tooltip.contains("Tyhp CLI was not found"))
    }

    @Test
    fun `LSP error with a healthy binary`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.ERROR,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/bin/tyhp",
            ),
        )
        assertEquals("Tyhp · demo · LSP error", view.text)
        assertTrue(view.error)
    }

    @Test
    fun `not in a Tyhp project is a warning when LSP is ready`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "not in a Tyhp project",
                lspState = LspClientState.RUNNING,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/bin/tyhp",
            ),
        )
        assertEquals("Tyhp · not in a Tyhp project · ready", view.text)
        assertTrue(view.warning)
        assertFalse(view.error)
    }

    @Test
    fun `starting shows starting copy`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.STARTING,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/bin/tyhp",
            ),
        )
        assertEquals("Tyhp · demo · starting", view.text)
    }

    @Test
    fun `stopped LSP is distinct from error`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.STOPPED,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/bin/tyhp",
            ),
        )
        assertEquals("Tyhp · demo · LSP stopped", view.text)
        assertFalse(view.error)
        assertFalse(view.warning)
    }

    @Test
    fun `invalid binary is shown as CLI missing`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.STOPPED,
                binaryStatus = BinaryStatus.INVALID,
                binaryMessage = "not a file",
            ),
        )
        assertEquals("Tyhp · demo · CLI missing", view.text)
        assertTrue(view.error)
        assertTrue(view.tooltip.contains("not a file"))
    }

    @Test
    fun `running proxy is appended to a healthy status`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.RUNNING,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/bin/tyhp",
                proxyState = ProxyRunState.RUNNING,
                proxyDetail = "IDE 9003 / XDebug 9004",
            ),
        )
        assertEquals("Tyhp · demo · ready · proxy", view.text)
        assertTrue(view.tooltip.contains("XDebug proxy: listening (IDE 9003 / XDebug 9004)"))
    }

    @Test
    fun `proxy error is a warning when LSP is healthy`() {
        val view = formatStatusBar(
            StatusBarInput(
                projectLabel = "demo",
                lspState = LspClientState.RUNNING,
                binaryStatus = BinaryStatus.OK,
                binaryPath = "/bin/tyhp",
                proxyState = ProxyRunState.ERROR,
            ),
        )
        assertEquals("Tyhp · demo · proxy error", view.text)
        assertTrue(view.warning)
        assertFalse(view.error)
    }

    @Test
    fun `status-bar proxy actions cover start stop restart`() {
        assertEquals(listOf("start"), proxyStatusActions(ProxyRunState.STOPPED))
        assertEquals(listOf("stop", "restart"), proxyStatusActions(ProxyRunState.RUNNING))
        assertEquals(listOf("stop", "restart"), proxyStatusActions(ProxyRunState.STARTING))
        assertEquals(listOf("start", "restart"), proxyStatusActions(ProxyRunState.ERROR))
    }
}
