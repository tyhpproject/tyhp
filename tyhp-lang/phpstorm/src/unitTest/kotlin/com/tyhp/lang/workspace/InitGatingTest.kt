package com.tyhp.lang.workspace

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class InitGatingTest {
    private val promptable = InitPromptContext(
        isTyhpFile = true,
        hasOwner = false,
        hasAncestorTyhpJson = false,
        hasForcedProject = false,
        hasContentRoot = true,
        dontAskAgain = false,
        promptedThisSession = false,
    )

    @Test
    fun `init argv is init --yes with no tyhp-project`() {
        assertEquals(listOf("init", "--yes"), buildInitArgs())
    }

    @Test
    fun `prompts when a Tyhp file has no owner and no ancestor tyhp json`() {
        assertTrue(shouldPromptInit(promptable))
    }

    @Test
    fun `does not prompt when the file has an include owner`() {
        assertFalse(shouldPromptInit(promptable.copy(hasOwner = true)))
    }

    @Test
    fun `does not prompt when an ancestor tyhp json exists`() {
        assertFalse(shouldPromptInit(promptable.copy(hasAncestorTyhpJson = true)))
    }

    @Test
    fun `does not prompt when tyhp projectPath forces a project`() {
        assertFalse(shouldPromptInit(promptable.copy(hasForcedProject = true)))
    }

    @Test
    fun `does not prompt for non-Tyhp documents`() {
        assertFalse(shouldPromptInit(promptable.copy(isTyhpFile = false)))
    }

    @Test
    fun `does not prompt without a content root`() {
        assertFalse(shouldPromptInit(promptable.copy(hasContentRoot = false)))
    }

    @Test
    fun `does not prompt again this session or after Don't Ask Again`() {
        assertFalse(shouldPromptInit(promptable.copy(promptedThisSession = true)))
        assertFalse(shouldPromptInit(promptable.copy(dontAskAgain = true)))
    }

    @Test
    fun `init error message prefers stderr then stdout then exit code`() {
        assertEquals("boom", initErrorMessage(" boom ", "out", 1))
        assertEquals("out", initErrorMessage("  ", " out ", 2))
        assertEquals("exit code 3", initErrorMessage(null, null, 3))
    }
}
