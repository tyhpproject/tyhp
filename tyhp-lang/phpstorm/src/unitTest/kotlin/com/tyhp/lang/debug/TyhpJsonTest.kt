package com.tyhp.lang.debug

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class TyhpJsonTest {
    @Test
    fun `reads xdebugProxy ports, sourcemap dir, ideKey, generateSourcemap, and output path`() {
        val snapshot = parseTyhpJsonProject(
            """
            {
                "build": { "generateSourcemap": true },
                "output": { "path": "out/" },
                "xdebugProxy": {
                    "idePort": 9010,
                    "xdebugPort": 9011,
                    "sourceMapDir": "./maps",
                    "ideKey": "tyhp"
                }
            }
            """.trimIndent(),
        )
        assertEquals(true, snapshot?.generateSourcemap)
        assertEquals("out/", snapshot?.outputPath)
        assertEquals(9010, snapshot?.xdebugProxy?.idePort)
        assertEquals(9011, snapshot?.xdebugProxy?.xdebugPort)
        assertEquals("./maps", snapshot?.xdebugProxy?.sourceMapDir)
        assertEquals("tyhp", snapshot?.xdebugProxy?.ideKey)
    }

    @Test
    fun `generateSourcemap defaults to false when omitted`() {
        val snapshot = parseTyhpJsonProject("""{ "output": { "path": "build/" } }""")
        assertEquals(false, snapshot?.generateSourcemap)
        assertEquals("build/", snapshot?.outputPath)
        assertNull(snapshot?.xdebugProxy)
    }

    @Test
    fun `accepts numeric ports encoded as strings`() {
        val snapshot = parseTyhpJsonProject(
            """{ "xdebugProxy": { "idePort": "9111", "xdebugPort": "9222" } }""",
        )
        assertEquals(9111, snapshot?.xdebugProxy?.idePort)
        assertEquals(9222, snapshot?.xdebugProxy?.xdebugPort)
    }

    @Test
    fun `ignores null sourceMapDir and empty strings`() {
        val snapshot = parseTyhpJsonProject(
            """{ "xdebugProxy": { "sourceMapDir": null, "ideKey": "" } }""",
        )
        assertNull(snapshot?.xdebugProxy?.sourceMapDir)
        assertNull(snapshot?.xdebugProxy?.ideKey)
    }

    @Test
    fun `returns null for invalid JSON`() {
        assertNull(parseTyhpJsonProject("{"))
        assertNull(parseTyhpJsonProject("[]"))
    }
}
