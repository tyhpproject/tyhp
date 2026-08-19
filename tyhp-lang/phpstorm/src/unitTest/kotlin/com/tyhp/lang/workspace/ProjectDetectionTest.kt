package com.tyhp.lang.workspace

import com.tyhp.lang.lsp.ProjectFileFs
import com.tyhp.lang.lsp.ResolveProjectFileOptions
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class ProjectDetectionTest {
    private fun fsFrom(files: Map<String, String>): ProjectFileFs {
        return object : ProjectFileFs {
            override fun exists(path: String): Boolean = files.containsKey(path)
            override fun isDirectory(path: String): Boolean = files[path] == "dir"
        }
    }

    private fun posix(configured: String, roots: List<String>, files: Map<String, String>): WorkspaceSnapshot {
        return detectWorkspaceProject(
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
    fun `empty tyhp projectPath does not treat content-root tyhp json as the owner`() {
        val snapshot = posix("", listOf("/empty", "/app"), mapOf("/app/tyhp.json" to "file"))
        assertNull(snapshot.projectFilePath)
        assertEquals("not in a Tyhp project", projectStatusLabel(snapshot))
    }

    @Test
    fun `honors tyhp projectPath override as a file path`() {
        val snapshot = posix(
            "/proj/custom.json",
            listOf("/ws"),
            mapOf(
                "/proj/custom.json" to "file",
                "/ws/tyhp.json" to "file",
            ),
        )
        assertEquals("/proj/custom.json", snapshot.projectFilePath)
        assertEquals("proj", snapshot.projectName)
    }

    @Test
    fun `returns no project when tyhp json is absent`() {
        val snapshot = posix("  ", listOf("/ws"), emptyMap())
        assertNull(snapshot.projectFilePath)
        assertEquals("not in a Tyhp project", projectStatusLabel(snapshot))
    }

    @Test
    fun `project display name is the directory containing tyhp json`() {
        val snapshot = snapshotFromProjectFile("/Users/me/code/demo/tyhp.json")
        assertEquals("demo", snapshot.projectName)
        assertEquals("demo", projectStatusLabel(snapshot))
    }

    @Test
    fun `content root prefers the longest root that contains the file`() {
        assertEquals(
            "/ws/app",
            contentRootForPath("/ws/app/src/Main.tyhp", listOf("/ws", "/ws/app")),
        )
        assertEquals("/ws", contentRootForPath(null, listOf("/ws", "/other")))
        assertNull(contentRootForPath(null, emptyList()))
    }
}
