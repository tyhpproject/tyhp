package com.tyhp.lang.settings

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SettingsCoreTest {
    @Test
    fun `empty and whitespace path values are unset`() {
        assertTrue(isPathUnset(null))
        assertTrue(isPathUnset(""))
        assertTrue(isPathUnset("   "))
        assertFalse(isPathUnset("/usr/local/bin/tyhp"))
    }

    @Test
    fun `path write target is application unless a project override exists`() {
        assertEquals(PathWriteTarget.Application, pathWriteTarget(null))
        assertEquals(PathWriteTarget.Application, pathWriteTarget(InspectedPath()))
        assertEquals(
            PathWriteTarget.Application,
            pathWriteTarget(InspectedPath(applicationValue = "/user/tyhp")),
        )
        assertEquals(
            PathWriteTarget.Project,
            pathWriteTarget(InspectedPath(projectValue = "")),
        )
        assertEquals(
            PathWriteTarget.Project,
            pathWriteTarget(InspectedPath(projectValue = "/ws/tyhp")),
        )
    }

    @Test
    fun `effective path prefers project override including empty string`() {
        assertEquals(
            "/user/tyhp",
            effectiveTyhpPath(InspectedPath(applicationValue = "/user/tyhp")),
        )
        assertEquals(
            "/ws/tyhp",
            effectiveTyhpPath(InspectedPath(applicationValue = "/user/tyhp", projectValue = "/ws/tyhp")),
        )
        assertEquals(
            "",
            effectiveTyhpPath(InspectedPath(applicationValue = "/user/tyhp", projectValue = "  ")),
        )
        assertTrue(isPathUnset(effectiveTyhpPath(InspectedPath(projectValue = ""))))
    }

    @Test
    fun `install mode parsing`() {
        assertEquals(InstallMode.PATH, parseInstallMode("path"))
        assertEquals(InstallMode.GLOBAL, parseInstallMode("global"))
        assertEquals(InstallMode.EXTENSION, parseInstallMode("extension"))
        assertEquals(InstallMode.PATH, parseInstallMode("nope"))
        assertEquals(InstallMode.PATH, parseInstallMode(null))
    }

    @Test
    fun `release tags normalize and compare with optional v prefix`() {
        assertEquals("v805.0.0-alpha.1", normalizeReleaseTag("805.0.0-alpha.1"))
        assertEquals("v805.0.0-alpha.1", normalizeReleaseTag("v805.0.0-alpha.1"))
        assertEquals("", normalizeReleaseTag(""))
        assertTrue(tagsMatch("805.0.0-alpha.1", "v805.0.0-alpha.1"))
        assertFalse(tagsMatch("v805.0.0-alpha.2", "v805.0.0-alpha.1"))
    }
}
