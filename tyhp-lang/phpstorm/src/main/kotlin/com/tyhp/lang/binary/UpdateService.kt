package com.tyhp.lang.binary

import com.tyhp.lang.settings.InstallMode

data class UpdateServiceSettings(
    val installMode: InstallMode,
    val autoUpdate: Boolean,
    val pinnedVersion: String,
)

data class AutoUpdatePlan(
    val shouldInstall: Boolean,
    val version: String? = null,
    val reason: String,
)

fun planAutoUpdate(
    settings: UpdateServiceSettings,
    metadata: InstallMetadata?,
    latestTag: String,
): AutoUpdatePlan {
    val decision = decideStartupUpdate(
        UpdatePolicyInput(
            installMode = settings.installMode,
            installedByPlugin = isPluginOwnedInstall(metadata),
            autoUpdate = settings.autoUpdate,
            pinnedVersion = settings.pinnedVersion,
            currentVersion = metadata?.version ?: "",
            latestVersion = latestTag,
        ),
    )
    return when (decision) {
        is UpdateDecision.Install -> AutoUpdatePlan(true, decision.version, decision.reason)
        is UpdateDecision.None -> AutoUpdatePlan(false, reason = decision.reason)
    }
}

class UpdateService(private val installer: Installer) {
    fun checkAndApply(
        settings: UpdateServiceSettings,
        metadata: InstallMetadata?,
        log: InstallerLogger,
    ): AutoUpdateResult {
        var latestTag = ""
        try {
            val needsLatest =
                settings.installMode == InstallMode.EXTENSION &&
                    isPluginOwnedInstall(metadata) &&
                    settings.pinnedVersion.trim().isEmpty() &&
                    settings.autoUpdate
            if (needsLatest) {
                latestTag = fetchLatestRelease().tagName
            }
        } catch (err: Exception) {
            val message = err.message ?: err.toString()
            log.appendLine("Auto-update check failed: $message")
            return AutoUpdateResult(updated = false, reason = message)
        }

        val plan = planAutoUpdate(settings, metadata, latestTag)
        log.appendLine(plan.reason)
        if (!plan.shouldInstall || plan.version == null) {
            return AutoUpdateResult(updated = false, reason = plan.reason)
        }

        val result = installer.install("extension", plan.version)
        return AutoUpdateResult(updated = true, reason = plan.reason, path = result.executablePath)
    }
}

data class AutoUpdateResult(
    val updated: Boolean,
    val reason: String,
    val path: String? = null,
)
