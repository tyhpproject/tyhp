package com.tyhp.lang.lsp

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ProjectFileTest {
    private fun fsFrom(files: Map<String, String>): ProjectFileFs {
        return object : ProjectFileFs {
            override fun exists(path: String): Boolean = files.containsKey(path)
            override fun isDirectory(path: String): Boolean = files[path] == "dir"
        }
    }

    private fun posix(configured: String, roots: List<String>, files: Map<String, String>): String? {
        return resolveTyhpProjectFile(
            ResolveProjectFileOptions(
                configuredPath = configured,
                contentRoots = roots,
                join = { dir, name -> "$dir/$name".replace("//", "/") },
                resolve = { root, rel ->
                    if (rel.startsWith("/")) rel else "$root/$rel".replace("//", "/")
                },
                isAbsolute = { it.startsWith("/") },
                fs = fsFrom(files),
            ),
        )
    }

    @Test
    fun `configured file path wins over content-root tyhp json`() {
        assertEquals(
            "/proj/custom.json",
            posix(
                "/proj/custom.json",
                listOf("/ws"),
                mapOf(
                    "/proj/custom.json" to "file",
                    "/ws/tyhp.json" to "file",
                ),
            ),
        )
    }

    @Test
    fun `configured directory uses tyhp json inside it`() {
        assertEquals(
            "/proj/tyhp.json",
            posix(
                "/proj",
                listOf("/ws"),
                mapOf(
                    "/proj" to "dir",
                    "/proj/tyhp.json" to "file",
                ),
            ),
        )
    }

    @Test
    fun `relative configured path is resolved against the first content root`() {
        assertEquals(
            "/ws/config/tyhp.json",
            posix(
                "config/tyhp.json",
                listOf("/ws", "/other"),
                mapOf("/ws/config/tyhp.json" to "file"),
            ),
        )
    }

    @Test
    fun `missing configured path does not invent a flag value`() {
        assertNull(
            posix(
                "/missing/tyhp.json",
                listOf("/ws"),
                mapOf("/ws/tyhp.json" to "file"),
            ),
        )
    }

    @Test
    fun `empty setting does not fall back to content-root tyhp json`() {
        assertNull(
            posix(
                "",
                listOf("/empty", "/app"),
                mapOf("/app/tyhp.json" to "file"),
            ),
        )
    }

    @Test
    fun `returns null when no project file is known`() {
        assertNull(posix("  ", listOf("/ws"), emptyMap()))
    }

    @Test
    fun `working directory is the project file parent`() {
        assertEquals("/ws", serverWorkingDirectory("/ws/tyhp.json", listOf("/other")))
        assertEquals("/other", serverWorkingDirectory(null, listOf("/other")))
        assertNull(serverWorkingDirectory("  ", emptyList()))
    }

    @Test
    fun `language server key differs when the extra fingerprint differs`() {
        val base = languageServerKey("/bin/tyhp", listOf("language_server"), "/ws", listOf("true", "off"))
        val sameEverythingElse = languageServerKey("/bin/tyhp", listOf("language_server"), "/ws", listOf("true", "off"))
        val diagnosticsToggled = languageServerKey("/bin/tyhp", listOf("language_server"), "/ws", listOf("false", "off"))
        val traceChanged = languageServerKey("/bin/tyhp", listOf("language_server"), "/ws", listOf("true", "verbose"))

        assertEquals(base, sameEverythingElse)
        assertTrue(base != diagnosticsToggled)
        assertTrue(base != traceChanged)
    }

    @Test
    fun `language server key defaults to no extra fingerprint`() {
        assertEquals(
            languageServerKey("/bin/tyhp", listOf("language_server"), "/ws"),
            languageServerKey("/bin/tyhp", listOf("language_server"), "/ws", emptyList()),
        )
    }
}
