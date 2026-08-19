package com.tyhp.lang.workspace

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class PathUtilsTest {
    @Test
    fun `skips tyhp json under ignored directories`() {
        assertTrue(shouldSkipIndexedTyhpJson("/ws/node_modules/pkg/tyhp.json"))
        assertTrue(shouldSkipIndexedTyhpJson("/ws/vendor/foo/tyhp.json"))
        assertTrue(shouldSkipIndexedTyhpJson("/ws/.git/tyhp.json"))
        assertFalse(shouldSkipIndexedTyhpJson("/ws/runtime/packages/core/tyhp.json"))
    }

    @Test
    fun `hasAncestorTyhpJson walks up to the workspace root`() {
        val files = setOf("/ws/runtime/packages/core/tyhp.json", "/ws/tyhp.json")
        assertTrue(
            hasAncestorTyhpJson(
                "/ws/runtime/packages/core/tyhp_src/Type.tyhp",
                "/ws",
                exists = { files.contains(it) },
            ),
        )
        assertTrue(hasAncestorTyhpJson("/ws/orphan/src/a.tyhp", "/ws", exists = { files.contains(it) }))
        assertFalse(hasAncestorTyhpJson("/ws/orphan/src/a.tyhp", "/ws", exists = { false }))
    }

    @Test
    fun `matchingWorkspaceRoot prefers the longest containing folder`() {
        assertEquals("/ws/app", matchingWorkspaceRoot("/ws/app/src/a.tyhp", listOf("/ws", "/ws/app")))
        assertNull(matchingWorkspaceRoot("/other/a.tyhp", listOf("/ws")))
    }
}
