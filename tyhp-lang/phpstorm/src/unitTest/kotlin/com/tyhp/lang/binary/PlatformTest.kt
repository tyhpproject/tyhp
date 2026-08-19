package com.tyhp.lang.binary

import java.nio.file.Path
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class PlatformTest {
    @Test
    fun `release asset names match scripts release sh`() {
        assertEquals(
            "tyhp-osx-arm64",
            releaseAssetName(HostPlatform(OsId.OSX, ArchId.ARM64), AssetVariant.SELF_CONTAINED),
        )
        assertEquals(
            "tyhp-osx-x64-fxdependent",
            releaseAssetName(HostPlatform(OsId.OSX, ArchId.X64), AssetVariant.FRAMEWORK_DEPENDENT),
        )
        assertEquals(
            "tyhp-linux-x64",
            releaseAssetName(HostPlatform(OsId.LINUX, ArchId.X64), AssetVariant.SELF_CONTAINED),
        )
        assertEquals(
            "tyhp-linux-arm64-fxdependent",
            releaseAssetName(HostPlatform(OsId.LINUX, ArchId.ARM64), AssetVariant.FRAMEWORK_DEPENDENT),
        )
        assertEquals(
            "tyhp-win-x64.exe",
            releaseAssetName(HostPlatform(OsId.WIN, ArchId.X64), AssetVariant.SELF_CONTAINED),
        )
        assertEquals(
            "tyhp-win-x64-fxdependent.exe",
            releaseAssetName(HostPlatform(OsId.WIN, ArchId.X64), AssetVariant.FRAMEWORK_DEPENDENT),
        )
    }

    @Test
    fun `unsupported platforms throw a clear error`() {
        assertFailsWith<UnsupportedPlatformError> { detectHostPlatform("FreeBSD", "amd64") }
        assertFailsWith<UnsupportedPlatformError> { detectHostPlatform("Windows 11", "aarch64") }
        assertFailsWith<UnsupportedPlatformError> { detectHostPlatform("Linux", "x86") }
    }

    @Test
    fun `global install locations match official install scripts`() {
        assertEquals(
            Path.of("/Users/me", ".local", "bin"),
            globalInstallDir(HostPlatform(OsId.OSX, ArchId.ARM64), "/Users/me"),
        )
        assertEquals(
            Path.of("/home/me", ".local", "bin", "tyhp"),
            globalInstallPath(HostPlatform(OsId.LINUX, ArchId.X64), "/home/me"),
        )
        assertEquals(
            Path.of("C:\\Users\\me\\AppData\\Local", "Programs", "tyhp"),
            globalInstallDir(
                HostPlatform(OsId.WIN, ArchId.X64),
                "C:\\Users\\me",
                "C:\\Users\\me\\AppData\\Local",
            ),
        )
        assertEquals("tyhp.exe", installedBinaryFileName(HostPlatform(OsId.WIN, ArchId.X64)))
    }

    @Test
    fun `plugin-only always uses self-contained global follows NET 9`() {
        assertEquals(AssetVariant.SELF_CONTAINED, chooseAssetVariant("extension", true))
        assertEquals(AssetVariant.SELF_CONTAINED, chooseAssetVariant("extension", false))
        assertEquals(AssetVariant.FRAMEWORK_DEPENDENT, chooseAssetVariant("global", true))
        assertEquals(AssetVariant.SELF_CONTAINED, chooseAssetVariant("global", false))
    }

    @Test
    fun `plugin storage path is under config tyhp-lang cli`() {
        val p = pluginInstallPath("/tmp/tyhp-lang", HostPlatform(OsId.OSX, ArchId.ARM64))
        assertEquals(Path.of("/tmp/tyhp-lang", "cli", "tyhp"), p)
    }

    @Test
    fun `isManagedInstallPath detects drift between tyhp path and the plugin install`() {
        val platform = HostPlatform(OsId.OSX, ArchId.ARM64)
        val managed = pluginInstallPath("/tmp/tyhp-lang", platform).toString()

        assertTrue(isManagedInstallPath(managed, "/tmp/tyhp-lang", platform))
        assertTrue(
            isManagedInstallPath(
                Path.of(managed, "..", "tyhp").toString(),
                "/tmp/tyhp-lang",
                platform,
            ),
        )
        assertFalse(isManagedInstallPath("/opt/custom/tyhp", "/tmp/tyhp-lang", platform))
        assertFalse(isManagedInstallPath("", "/tmp/tyhp-lang", platform))
        assertFalse(isManagedInstallPath("   ", "/tmp/tyhp-lang", platform))
    }
}
