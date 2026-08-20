package com.tyhp.lang.workspace

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class ProjectIndexTest {
    private val coreJson =
        """{"include":["./tyhp_src/**/*.tyhp","../../php-extensions/php8.2.9/**/*.tyhpdef"],"exclude":[]}"""
    private val rootJson = """{"include":[],"exclude":[]}"""
    private val appJson = """{"include":["./src/**/*.tyhp"],"exclude":[]}"""

    @Test
    fun `root tyhp json with empty include owns nothing`() {
        val index = ProjectIndex(
            listOf(
                indexedProjectFromJson("/repo/tyhp.json", rootJson),
                indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", coreJson),
            ),
            false,
        )
        assertNull(index.ownerOf("/repo/README.tyhp"))
        assertEquals("core", index.ownerOf("/repo/runtime/packages/core/tyhp_src/Type.tyhp")?.projectName)
    }

    @Test
    fun `invalid JSON project owns nothing`() {
        val index = ProjectIndex(
            listOf(indexedProjectFromJson("/repo/broken/tyhp.json", "{ not json")),
            false,
        )
        assertNull(index.ownerOf("/repo/broken/src/a.tyhp"))
    }

    @Test
    fun `non-ancestor include still owns the file`() {
        val index = ProjectIndex(
            listOf(indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", coreJson)),
            false,
        )
        assertEquals(
            "core",
            index.ownerOf("/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef")?.projectName,
        )
    }

    @Test
    fun `does not merge two matching projects`() {
        val asyncJson =
            """{"include":["./tyhp_src/**/*.tyhp","../../php-extensions/php8.2.9/**/*.tyhpdef"]}"""
        val index = ProjectIndex(
            listOf(
                indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", coreJson),
                indexedProjectFromJson("/repo/runtime/packages/async/tyhp.json", asyncJson),
            ),
            false,
        )
        assertEquals(
            "core",
            index.ownerOf("/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef")?.projectName,
        )
    }

    @Test
    fun `app src file is not owned by core`() {
        val index = ProjectIndex(
            listOf(
                indexedProjectFromJson("/repo/runtime/packages/core/tyhp.json", coreJson),
                indexedProjectFromJson("/repo/app/tyhp.json", appJson),
            ),
            false,
        )
        assertEquals("app", index.ownerOf("/repo/app/src/Main.tyhp")?.projectName)
        assertEquals("core", index.ownerOf("/repo/runtime/packages/core/tyhp_src/Type.tyhp")?.projectName)
    }
}
