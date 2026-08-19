package com.tyhp.lang.binary

import com.tyhp.lang.settings.InstallMode
import com.tyhp.lang.settings.normalizePinnedVersion
import com.tyhp.lang.settings.normalizeReleaseTag
import com.tyhp.lang.settings.tagsMatch

data class UpdatePolicyInput(
    val installMode: InstallMode,
    /** True only when metadata says this plugin wrote the binary into plugin storage. */
    val installedByPlugin: Boolean,
    val autoUpdate: Boolean,
    val pinnedVersion: String,
    val currentVersion: String,
    val latestVersion: String,
)

sealed class UpdateDecision {
    data class None(val reason: String) : UpdateDecision()
    data class Install(val version: String, val reason: String) : UpdateDecision()
}

/**
 * Startup / background auto-update. Global and PATH binaries are never overwritten here.
 * Pin wins over auto-update: a non-empty pin installs/keeps that version and will not
 * float to some other latest tag.
 */
fun decideStartupUpdate(input: UpdatePolicyInput): UpdateDecision {
    if (input.installMode != InstallMode.EXTENSION || !input.installedByPlugin) {
        return UpdateDecision.None(
            "Auto-update applies only to a CLI installed by this plugin in plugin-only mode",
        )
    }

    val pin = normalizePinnedVersion(input.pinnedVersion)
    if (pin.isNotEmpty()) {
        val pinTag = normalizeReleaseTag(pin)
        if (tagsMatch(input.currentVersion, pinTag)) {
            return UpdateDecision.None("Pinned version $pinTag is already installed")
        }
        return UpdateDecision.Install(pinTag, "Keeping pinned version $pinTag")
    }

    if (!input.autoUpdate) {
        return UpdateDecision.None("tyhp.binary.autoUpdate is disabled")
    }

    val latest = normalizeReleaseTag(input.latestVersion)
    if (latest.isEmpty()) {
        return UpdateDecision.None("No latest release tag is available")
    }
    if (tagsMatch(input.currentVersion, latest)) {
        return UpdateDecision.None("Already on latest $latest")
    }
    return UpdateDecision.Install(latest, "Newer release $latest is available")
}

/** Explicit “Install / Update CLI”: always allowed; pin selects the tag when set. */
fun decideExplicitInstall(pinnedVersion: String, latestVersion: String): UpdateDecision {
    val pin = normalizePinnedVersion(pinnedVersion)
    if (pin.isNotEmpty()) {
        val pinTag = normalizeReleaseTag(pin)
        return UpdateDecision.Install(pinTag, "Installing pinned version $pinTag")
    }
    val latest = normalizeReleaseTag(latestVersion)
    if (latest.isEmpty()) {
        return UpdateDecision.None("No GitHub release tag is available to install")
    }
    return UpdateDecision.Install(latest, "Installing latest release $latest")
}

fun shouldAutoUpdate(input: UpdatePolicyInput): Boolean =
    decideStartupUpdate(input) is UpdateDecision.Install

/**
 * Drift guard from Phase 3 review: a stale `installMode=extension` must not
 * auto-update when `tyhp.path` no longer points at the plugin-managed install.
 */
fun shouldSkipAutoUpdateDueToPathDrift(
    installMode: InstallMode,
    configuredPath: String,
    pluginStorageFsPath: String,
    platform: HostPlatform,
): Boolean {
    if (installMode != InstallMode.EXTENSION) {
        return false
    }
    return !isManagedInstallPath(configuredPath, pluginStorageFsPath, platform)
}
