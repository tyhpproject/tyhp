package com.tyhp.lang.binary

import com.tyhp.lang.settings.InstallMode
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertTrue

class PolicyTest {
    @Test
    fun `PATH and global installs never auto-update`() {
        val latest = "v805.0.0-alpha.2"
        for (mode in listOf(InstallMode.PATH, InstallMode.GLOBAL)) {
            val decision = decideStartupUpdate(
                UpdatePolicyInput(
                    installMode = mode,
                    installedByPlugin = mode == InstallMode.GLOBAL,
                    autoUpdate = true,
                    pinnedVersion = "",
                    currentVersion = "v805.0.0-alpha.1",
                    latestVersion = latest,
                ),
            )
            assertIs<UpdateDecision.None>(decision)
            assertFalse(
                shouldAutoUpdate(
                    UpdatePolicyInput(
                        installMode = mode,
                        installedByPlugin = true,
                        autoUpdate = true,
                        pinnedVersion = "",
                        currentVersion = "v805.0.0-alpha.1",
                        latestVersion = latest,
                    ),
                ),
            )
        }
    }

    @Test
    fun `plugin-only auto-update requires this plugin owns the binary`() {
        val decision = decideStartupUpdate(
            UpdatePolicyInput(
                installMode = InstallMode.EXTENSION,
                installedByPlugin = false,
                autoUpdate = true,
                pinnedVersion = "",
                currentVersion = "v805.0.0-alpha.1",
                latestVersion = "v805.0.0-alpha.2",
            ),
        )
        assertIs<UpdateDecision.None>(decision)
    }

    @Test
    fun `plugin-only auto-update installs latest when newer and enabled`() {
        val decision = decideStartupUpdate(
            UpdatePolicyInput(
                installMode = InstallMode.EXTENSION,
                installedByPlugin = true,
                autoUpdate = true,
                pinnedVersion = "",
                currentVersion = "v805.0.0-alpha.1",
                latestVersion = "v805.0.0-alpha.2",
            ),
        )
        assertEquals(
            UpdateDecision.Install("v805.0.0-alpha.2", "Newer release v805.0.0-alpha.2 is available"),
            decision,
        )
    }

    @Test
    fun `plugin-only auto-update is skipped when already current or disabled`() {
        val current = decideStartupUpdate(
            UpdatePolicyInput(
                installMode = InstallMode.EXTENSION,
                installedByPlugin = true,
                autoUpdate = true,
                pinnedVersion = "",
                currentVersion = "v805.0.0-alpha.2",
                latestVersion = "v805.0.0-alpha.2",
            ),
        )
        assertIs<UpdateDecision.None>(current)

        val disabled = decideStartupUpdate(
            UpdatePolicyInput(
                installMode = InstallMode.EXTENSION,
                installedByPlugin = true,
                autoUpdate = false,
                pinnedVersion = "",
                currentVersion = "v805.0.0-alpha.1",
                latestVersion = "v805.0.0-alpha.2",
            ),
        )
        assertIs<UpdateDecision.None>(disabled)
    }

    @Test
    fun `pinned version is kept and other latest tags are not auto-installed`() {
        val alreadyPinned = decideStartupUpdate(
            UpdatePolicyInput(
                installMode = InstallMode.EXTENSION,
                installedByPlugin = true,
                autoUpdate = true,
                pinnedVersion = "805.0.0-alpha.1",
                currentVersion = "v805.0.0-alpha.1",
                latestVersion = "v805.0.0-alpha.2",
            ),
        )
        assertIs<UpdateDecision.None>(alreadyPinned)

        val moveToPin = decideStartupUpdate(
            UpdatePolicyInput(
                installMode = InstallMode.EXTENSION,
                installedByPlugin = true,
                autoUpdate = false,
                pinnedVersion = "v805.0.0-alpha.1",
                currentVersion = "v805.0.0-alpha.2",
                latestVersion = "v805.0.0-alpha.3",
            ),
        )
        val install = assertIs<UpdateDecision.Install>(moveToPin)
        assertEquals("v805.0.0-alpha.1", install.version)
    }

    @Test
    fun `explicit install uses pin when set otherwise latest`() {
        val pinned = decideExplicitInstall("805.0.0-alpha.1", "v805.0.0-alpha.9")
        assertEquals("v805.0.0-alpha.1", assertIs<UpdateDecision.Install>(pinned).version)

        val latest = decideExplicitInstall("", "v805.0.0-alpha.9")
        assertEquals("v805.0.0-alpha.9", assertIs<UpdateDecision.Install>(latest).version)

        assertIs<UpdateDecision.None>(decideExplicitInstall("", ""))
    }

    @Test
    fun `stale installMode extension does not auto-update a drifted path`() {
        val platform = HostPlatform(OsId.OSX, ArchId.ARM64)
        val storage = "/tmp/tyhp-lang"
        val managed = pluginInstallPath(storage, platform).toString()

        assertFalse(
            shouldSkipAutoUpdateDueToPathDrift(InstallMode.EXTENSION, managed, storage, platform),
        )
        assertTrue(
            shouldSkipAutoUpdateDueToPathDrift(InstallMode.EXTENSION, "/opt/custom/tyhp", storage, platform),
        )
        assertTrue(
            shouldSkipAutoUpdateDueToPathDrift(InstallMode.EXTENSION, "", storage, platform),
        )
        assertFalse(
            shouldSkipAutoUpdateDueToPathDrift(InstallMode.GLOBAL, "/opt/custom/tyhp", storage, platform),
        )
    }
}
