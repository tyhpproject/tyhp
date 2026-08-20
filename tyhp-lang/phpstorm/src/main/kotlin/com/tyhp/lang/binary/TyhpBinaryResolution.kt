package com.tyhp.lang.binary

import com.intellij.openapi.project.Project
import com.tyhp.lang.settings.InstallMode

enum class BinarySource { SETTING, PATH, NONE }

enum class BinaryStatus { OK, MISSING, INVALID }

/**
 * Result of locating the Tyhp CLI. Phase 11+ (LSP, run configs, XDebug proxy) must call
 * [resolveTyhpBinary] rather than reading `tyhp.path` on their own.
 */
data class TyhpBinaryResolution(
    val status: BinaryStatus,
    val executablePath: String? = null,
    val message: String? = null,
    val source: BinarySource,
    val installMode: InstallMode,
) {
    val isOk: Boolean get() = status == BinaryStatus.OK && !executablePath.isNullOrBlank()
}

internal fun missingResolution(
    message: String,
    installMode: InstallMode = InstallMode.PATH,
): TyhpBinaryResolution {
    return TyhpBinaryResolution(
        status = BinaryStatus.MISSING,
        message = message,
        source = BinarySource.NONE,
        installMode = installMode,
    )
}

internal fun inactiveResolution(): TyhpBinaryResolution =
    missingResolution("Tyhp binary manager is not active.")

fun interface BinaryResolutionListener {
    fun onResolutionChanged(project: Project?, resolution: TyhpBinaryResolution)
}
