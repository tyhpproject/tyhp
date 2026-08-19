package com.tyhp.lang.binary

import com.tyhp.lang.settings.InstallMode
import com.tyhp.lang.settings.parseInstallMode
import java.nio.file.Files
import java.nio.file.Path

const val PLUGIN_INSTALLER_ID = "com.tyhp.lang"

data class InstallMetadata(
    val installedBy: String,
    val version: String,
    val mode: InstallMode,
    val assetName: String,
    val installedAt: String,
)

fun metadataPath(cliDir: Path): Path = cliDir.resolve("metadata.json")

fun readInstallMetadata(cliDir: Path): InstallMetadata? {
    val file = metadataPath(cliDir)
    return try {
        if (!Files.isRegularFile(file)) {
            return null
        }
        parseInstallMetadata(Files.readString(file))
    } catch (_: Exception) {
        null
    }
}

fun writeInstallMetadata(cliDir: Path, metadata: InstallMetadata) {
    Files.createDirectories(cliDir)
    Files.writeString(metadataPath(cliDir), "${toJson(metadata)}\n")
}

fun deleteInstallMetadata(cliDir: Path) {
    try {
        Files.deleteIfExists(metadataPath(cliDir))
    } catch (_: Exception) {
        // ignore
    }
}

fun isPluginOwnedInstall(metadata: InstallMetadata?): Boolean =
    metadata != null &&
        metadata.installedBy == PLUGIN_INSTALLER_ID &&
        metadata.mode == InstallMode.EXTENSION

fun parseInstallMetadata(json: String): InstallMetadata? {
    val parsed = parseJson(json) as? Map<*, *> ?: return null
    val installedBy = parsed["installedBy"] as? String ?: return null
    val version = parsed["version"] as? String ?: return null
    val mode = parseInstallMode(parsed["mode"] as? String)
    val assetName = parsed["assetName"] as? String ?: ""
    val installedAt = parsed["installedAt"] as? String ?: ""
    return InstallMetadata(
        installedBy = installedBy,
        version = version,
        mode = mode,
        assetName = assetName,
        installedAt = installedAt,
    )
}

fun toJson(metadata: InstallMetadata): String {
    return buildString {
        append("{\n")
        append("  \"installedBy\": ${jsonQuote(metadata.installedBy)},\n")
        append("  \"version\": ${jsonQuote(metadata.version)},\n")
        append("  \"mode\": ${jsonQuote(metadata.mode.value)},\n")
        append("  \"assetName\": ${jsonQuote(metadata.assetName)},\n")
        append("  \"installedAt\": ${jsonQuote(metadata.installedAt)}\n")
        append("}")
    }
}

private fun jsonQuote(value: String): String {
    val escaped = value
        .replace("\\", "\\\\")
        .replace("\"", "\\\"")
        .replace("\n", "\\n")
        .replace("\r", "\\r")
        .replace("\t", "\\t")
    return "\"$escaped\""
}
