package com.tyhp.lang.run

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class TyhpCliArgsTest {
    @Test
    fun `build argv is build --quiet --tyhp-project file`() {
        assertEquals(
            listOf("build", "--quiet", "--tyhp-project=/ws/tyhp.json"),
            buildTyhpTaskArgs("build", "/ws/tyhp.json"),
        )
    }

    @Test
    fun `lint argv is lint --quiet --format=json --tyhp-project file`() {
        assertEquals(
            listOf("lint", "--quiet", "--format=json", "--tyhp-project=/ws/tyhp.json"),
            buildTyhpTaskArgs("lint", "/ws/tyhp.json"),
        )
    }

    @Test
    fun `omits --tyhp-project when no project file is known`() {
        assertEquals(listOf("build", "--quiet"), buildTyhpTaskArgs("build"))
        assertEquals(listOf("lint", "--quiet", "--format=json"), buildTyhpTaskArgs("lint", "  "))
    }

    @Test
    fun `isTyhpTaskAction accepts only build and lint`() {
        assertTrue(isTyhpTaskAction("build"))
        assertTrue(isTyhpTaskAction("lint"))
        assertFalse(isTyhpTaskAction("init"))
        assertFalse(isTyhpTaskAction("xdebug_proxy"))
        assertFalse(isTyhpTaskAction(null))
    }
}
