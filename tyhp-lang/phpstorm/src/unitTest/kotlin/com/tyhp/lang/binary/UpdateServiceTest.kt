package com.tyhp.lang.binary

import com.tyhp.lang.settings.InstallMode
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class UpdateServiceTest {
    private val owned = InstallMetadata(
        installedBy = PLUGIN_INSTALLER_ID,
        version = "v805.0.0-alpha.1",
        mode = InstallMode.EXTENSION,
        assetName = "tyhp-osx-arm64",
        installedAt = "2026-08-18T00:00:00.000Z",
    )

    @Test
    fun `planAutoUpdate only floats latest for plugin-owned installs`() {
        val skipGlobal = planAutoUpdate(
            UpdateServiceSettings(InstallMode.GLOBAL, autoUpdate = true, pinnedVersion = ""),
            owned.copy(mode = InstallMode.GLOBAL),
            "v805.0.0-alpha.2",
        )
        assertFalse(skipGlobal.shouldInstall)

        val skipPath = planAutoUpdate(
            UpdateServiceSettings(InstallMode.PATH, autoUpdate = true, pinnedVersion = ""),
            null,
            "v805.0.0-alpha.2",
        )
        assertFalse(skipPath.shouldInstall)

        val update = planAutoUpdate(
            UpdateServiceSettings(InstallMode.EXTENSION, autoUpdate = true, pinnedVersion = ""),
            owned,
            "v805.0.0-alpha.2",
        )
        assertTrue(update.shouldInstall)
        assertEquals("v805.0.0-alpha.2", update.version)
    }

    @Test
    fun `isPluginOwnedInstall requires our id and extension mode`() {
        assertTrue(isPluginOwnedInstall(owned))
        assertFalse(isPluginOwnedInstall(owned.copy(mode = InstallMode.GLOBAL)))
        assertFalse(isPluginOwnedInstall(owned.copy(installedBy = "other")))
        assertFalse(isPluginOwnedInstall(null))
    }

    @Test
    fun `metadata json round-trips`() {
        val json = toJson(owned)
        val parsed = parseInstallMetadata(json)
        assertEquals(owned, parsed)
    }
}
