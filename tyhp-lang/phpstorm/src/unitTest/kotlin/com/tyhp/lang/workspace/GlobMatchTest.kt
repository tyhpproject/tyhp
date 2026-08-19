package com.tyhp.lang.workspace

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class GlobMatchTest {
    @Test
    fun `strips dot-slash prefix`() {
        assertTrue(matchesGlob("tyhp_src/Type.tyhp", "./tyhp_src/**/*.tyhp", false))
        assertTrue(matchesGlob("./tyhp_src/Type.tyhp", "tyhp_src/**/*.tyhp", false))
    }

    @Test
    fun `star-star matches zero directories`() {
        assertTrue(matchesGlob("tyhp_src/Type.tyhp", "tyhp_src/**/*.tyhp", false))
        assertTrue(matchesGlob("tyhp_src/nested/deep/Type.tyhp", "tyhp_src/**/*.tyhp", false))
    }

    @Test
    fun `parent include globs match files outside the project directory`() {
        val relative = "../../php-extensions/php8.2.9/ext/Core.tyhpdef"
        assertTrue(matchesGlob(relative, "../../php-extensions/php8.2.9/**/*.tyhpdef", false))
        assertFalse(matchesGlob(relative, "./tyhp_src/**/*.tyhp", false))
    }

    @Test
    fun `empty include owns nothing`() {
        assertFalse(
            fileMatchesProject(
                "/repo/runtime/packages/core",
                "/repo/runtime/packages/core/tyhp_src/Type.tyhp",
                emptyList(),
                emptyList(),
                false,
            ),
        )
    }

    @Test
    fun `core-style membership including php-extensions tyhpdef`() {
        val include = listOf("./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef")
        assertTrue(
            fileMatchesProject(
                "/repo/runtime/packages/core",
                "/repo/runtime/packages/core/tyhp_src/Type.tyhp",
                include,
                emptyList(),
                false,
            ),
        )
        assertTrue(
            fileMatchesProject(
                "/repo/runtime/packages/core",
                "/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef",
                include,
                emptyList(),
                false,
            ),
        )
        assertFalse(
            fileMatchesProject(
                "/repo/runtime/packages/core",
                "/repo/runtime/packages/core/README.md",
                include,
                emptyList(),
                false,
            ),
        )
    }

    @Test
    fun `exclude wins after include`() {
        assertFalse(
            fileMatchesProject(
                "/app",
                "/app/src/skip.tyhp",
                listOf("./src/**/*.tyhp"),
                listOf("./src/skip.tyhp"),
                false,
            ),
        )
        assertTrue(
            fileMatchesProject(
                "/app",
                "/app/src/keep.tyhp",
                listOf("./src/**/*.tyhp"),
                listOf("./src/skip.tyhp"),
                false,
            ),
        )
    }

    @Test
    fun `windows case-insensitive matching`() {
        assertTrue(matchesGlob("Tyhp_Src/Type.tyhp", "./tyhp_src/**/*.tyhp", true))
        assertFalse(matchesGlob("Tyhp_Src/Type.tyhp", "./tyhp_src/**/*.tyhp", false))
    }
}
