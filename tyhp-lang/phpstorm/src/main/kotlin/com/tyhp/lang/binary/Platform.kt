package com.tyhp.lang.binary

import java.nio.file.Path

enum class OsId { OSX, LINUX, WIN }

enum class ArchId { X64, ARM64 }

enum class AssetVariant { SELF_CONTAINED, FRAMEWORK_DEPENDENT }

data class HostPlatform(
    val os: OsId,
    val arch: ArchId,
) {
    val osToken: String
        get() = when (os) {
            OsId.OSX -> "osx"
            OsId.LINUX -> "linux"
            OsId.WIN -> "win"
        }

    val archToken: String
        get() = when (arch) {
            ArchId.X64 -> "x64"
            ArchId.ARM64 -> "arm64"
        }
}

class UnsupportedPlatformError(
    val osName: String,
    val osArch: String,
) : RuntimeException(
    "Tyhp CLI has no GitHub Release asset for $osName/$osArch. " +
        "Supported assets: tyhp-osx-arm64, tyhp-osx-x64, tyhp-linux-x64, tyhp-linux-arm64, tyhp-win-x64.exe " +
        "(and matching -fxdependent variants).",
)

/**
 * GitHub Release asset names from `scripts/release.sh` EXPECTED_ASSETS.
 * Self-contained: `tyhp-{os}-{arch}` (Windows adds `.exe`).
 * Framework-dependent: same with `-fxdependent` before the optional `.exe`.
 */
fun releaseAssetName(platform: HostPlatform, variant: AssetVariant): String {
    val id = "${platform.osToken}-${platform.archToken}"
    val suffix = if (variant == AssetVariant.FRAMEWORK_DEPENDENT) "-fxdependent" else ""
    return if (platform.os == OsId.WIN) {
        "tyhp-$id$suffix.exe"
    } else {
        "tyhp-$id$suffix"
    }
}

fun installedBinaryFileName(platform: HostPlatform): String =
    if (platform.os == OsId.WIN) "tyhp.exe" else "tyhp"

fun pathProbeNames(platform: HostPlatform): List<String> =
    if (platform.os == OsId.WIN) listOf("tyhp.exe", "tyhp") else listOf("tyhp")

fun detectHostPlatform(
    osName: String = System.getProperty("os.name") ?: "",
    osArch: String = System.getProperty("os.arch") ?: "",
): HostPlatform {
    val osId = when {
        osName.contains("mac", ignoreCase = true) || osName.contains("darwin", ignoreCase = true) -> OsId.OSX
        osName.contains("linux", ignoreCase = true) -> OsId.LINUX
        osName.contains("win", ignoreCase = true) -> OsId.WIN
        else -> throw UnsupportedPlatformError(osName, osArch)
    }
    val arch = when (osArch.lowercase()) {
        "x64", "amd64", "x86_64" -> ArchId.X64
        "arm64", "aarch64" -> ArchId.ARM64
        else -> throw UnsupportedPlatformError(osName, osArch)
    }
    if (osId == OsId.WIN && arch == ArchId.ARM64) {
        throw UnsupportedPlatformError(osName, osArch)
    }
    return HostPlatform(osId, arch)
}

/** Matches `scripts/install.sh` (`$HOME/.local/bin`) and `scripts/install.ps1` (`%LOCALAPPDATA%\Programs\tyhp`). */
fun globalInstallDir(
    platform: HostPlatform,
    homedir: String,
    localAppData: String? = null,
): Path {
    if (platform.os == OsId.WIN) {
        val root = if (!localAppData.isNullOrBlank()) {
            localAppData
        } else {
            Path.of(homedir, "AppData", "Local").toString()
        }
        return Path.of(root, "Programs", "tyhp")
    }
    return Path.of(homedir, ".local", "bin")
}

fun globalInstallPath(
    platform: HostPlatform,
    homedir: String,
    localAppData: String? = null,
): Path = globalInstallDir(platform, homedir, localAppData).resolve(installedBinaryFileName(platform))

fun pluginInstallDir(pluginStorageFsPath: String): Path = Path.of(pluginStorageFsPath, "cli")

fun pluginInstallPath(pluginStorageFsPath: String, platform: HostPlatform): Path =
    pluginInstallDir(pluginStorageFsPath).resolve(installedBinaryFileName(platform))

/**
 * Whether a configured `tyhp.path` currently resolves to this plugin's managed
 * install location. `tyhp.binary.installMode` can drift from `tyhp.path` when a
 * user edits the path by hand (bypassing Install / Update CLI or a PATH re-probe);
 * auto-update must not treat a stale `installMode: "extension"` as license to
 * overwrite a path the user pointed elsewhere. Empty/unset paths are never managed.
 */
fun isManagedInstallPath(
    configuredPath: String,
    pluginStorageFsPath: String,
    platform: HostPlatform,
): Boolean {
    val trimmed = configuredPath.trim()
    if (trimmed.isEmpty()) {
        return false
    }
    val configured = Path.of(trimmed).toAbsolutePath().normalize()
    val managed = pluginInstallPath(pluginStorageFsPath, platform).toAbsolutePath().normalize()
    return configured == managed
}

/**
 * Plugin-only installs always use the self-contained asset so the IDE does not
 * depend on a machine-wide .NET 9 runtime. Global installs match `scripts/install.sh`:
 * framework-dependent when .NET 9 is present, otherwise self-contained.
 */
fun chooseAssetVariant(mode: String, hasDotNet9: Boolean): AssetVariant {
    if (mode == "extension") {
        return AssetVariant.SELF_CONTAINED
    }
    return if (hasDotNet9) AssetVariant.FRAMEWORK_DEPENDENT else AssetVariant.SELF_CONTAINED
}
