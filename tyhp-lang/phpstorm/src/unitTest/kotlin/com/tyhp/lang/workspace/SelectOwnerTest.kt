package com.tyhp.lang.workspace

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class SelectOwnerTest {
    @Test
    fun `single candidate is the owner`() {
        val owner = selectOwner(
            "/repo/runtime/packages/core/tyhp_src/Type.tyhp",
            listOf(SimpleOwner("/repo/runtime/packages/core/tyhp.json", "/repo/runtime/packages/core")),
        )
        assertEquals("/repo/runtime/packages/core/tyhp.json", owner?.projectFilePath)
    }

    @Test
    fun `empty candidates yields no owner`() {
        assertNull(selectOwner("/repo/a.tyhp", emptyList()))
    }

    @Test
    fun `nearest ancestor wins`() {
        val owner = selectOwner(
            "/repo/app/pkg/src/Main.tyhp",
            listOf(
                SimpleOwner("/repo/runtime/packages/core/tyhp.json", "/repo/runtime/packages/core"),
                SimpleOwner("/repo/app/pkg/tyhp.json", "/repo/app/pkg"),
                SimpleOwner("/repo/app/tyhp.json", "/repo/app"),
            ),
        )
        assertEquals("/repo/app/pkg/tyhp.json", owner?.projectFilePath)
    }

    @Test
    fun `when no ancestor matches, nearest by path hops wins`() {
        val owner = selectOwner(
            "/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef",
            listOf(
                SimpleOwner("/repo/runtime/packages/core/tyhp.json", "/repo/runtime/packages/core"),
                SimpleOwner("/repo/other/far/away/tyhp.json", "/repo/other/far/away"),
            ),
        )
        assertEquals("/repo/runtime/packages/core/tyhp.json", owner?.projectFilePath)
    }

    @Test
    fun `equal hops shortest path then lexicographic`() {
        val owner = selectOwner(
            "/repo/runtime/php-extensions/php8.2.9/standard/strings.tyhpdef",
            listOf(
                SimpleOwner("/repo/runtime/packages/core/tyhp.json", "/repo/runtime/packages/core"),
                SimpleOwner("/repo/runtime/packages/async/tyhp.json", "/repo/runtime/packages/async"),
                SimpleOwner("/repo/runtime/packages/lambda/tyhp.json", "/repo/runtime/packages/lambda"),
            ),
        )
        assertEquals("/repo/runtime/packages/core/tyhp.json", owner?.projectFilePath)
    }

    @Test
    fun `lexicographic tie-break when path lengths are equal`() {
        val owner = selectOwner(
            "/shared/x.tyhpdef",
            listOf(
                SimpleOwner("/b/tyhp.json", "/b"),
                SimpleOwner("/a/tyhp.json", "/a"),
            ),
        )
        assertEquals("/a/tyhp.json", owner?.projectFilePath)
    }
}
