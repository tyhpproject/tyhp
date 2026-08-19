package com.tyhp.lang.binary

import com.tyhp.lang.settings.InstallMode
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.StandardCopyOption
import java.nio.file.attribute.PosixFilePermission
import java.time.Instant
import java.util.EnumSet

fun interface InstallerLogger {
    fun appendLine(message: String)
}

data class InstallResult(
    val executablePath: String,
    val version: String,
    val assetName: String,
    val mode: String,
    val variant: AssetVariant,
)

fun fetchLatestRelease(): GithubRelease {
    val payload = httpGetJsonText(releasesListUrl())
    val parsed = parseJson(payload)
    if (parsed !is List<*>) {
        val obj = parsed as? Map<*, *>
        val apiMessage = obj?.get("message") as? String
        throw IllegalStateException(
            "GitHub API error listing releases for $GITHUB_REPO${if (!apiMessage.isNullOrBlank()) ": $apiMessage" else ""}. " +
                "Set GITHUB_TOKEN if you are rate-limited, or set `tyhp.path` to a local binary.",
        )
    }
    val releases = parseGithubReleaseList(payload)
    if (releases.isEmpty()) {
        throw IllegalStateException(
            "No GitHub Releases were returned for $GITHUB_REPO. " +
                "Compiler binaries may not be published yet. Install a local `tyhp` on PATH, or set `tyhp.path`.",
        )
    }
    val latest = selectLatestRelease(releases)
    if (latest == null || latest.tagName.isEmpty()) {
        throw IllegalStateException(
            "Unable to determine a GitHub release tag (all listed releases are drafts, or the repo has no public release yet).",
        )
    }
    return latest
}

fun fetchReleaseByTag(tag: String): GithubRelease {
    val payload = httpGetJsonText(releaseByTagUrl(tag))
    val release = parseGithubRelease(payload)
    if (release.tagName.isEmpty()) {
        val apiMessage = release.message
        throw IllegalStateException(
            "GitHub release tag `$tag` was not found on $GITHUB_REPO" +
                "${if (!apiMessage.isNullOrBlank()) " ($apiMessage)" else ""}. Check `tyhp.binary.pinnedVersion`.",
        )
    }
    return release
}

fun hasDotNet9Runtime(): Boolean {
    return try {
        val process = ProcessBuilder("dotnet", "--list-runtimes")
            .redirectErrorStream(true)
            .start()
        val finished = process.waitFor(5, java.util.concurrent.TimeUnit.SECONDS)
        if (!finished) {
            process.destroyForcibly()
            return false
        }
        if (process.exitValue() != 0) {
            return false
        }
        val stdout = process.inputStream.bufferedReader().readText()
        Regex("""Microsoft\.NETCore\.App 9\.""").containsMatchIn(stdout)
    } catch (_: Exception) {
        false
    }
}

class Installer(
    private val pluginStorageFsPath: String,
    private val log: InstallerLogger,
    private val platform: HostPlatform = detectHostPlatform(),
    private val homedir: String = System.getProperty("user.home") ?: "",
    private val localAppData: String? = System.getenv("LOCALAPPDATA"),
) {
    fun install(mode: String, versionTag: String? = null): InstallResult {
        val variant = chooseAssetVariant(mode, hasDotNet9Runtime())
        val assetName = releaseAssetName(platform, variant)
        log.appendLine(
            "Resolving $mode install for ${platform.osToken}-${platform.archToken} ($variant, asset $assetName)…",
        )

        val release = if (!versionTag.isNullOrBlank()) {
            fetchReleaseByTag(versionTag)
        } else {
            fetchLatestRelease()
        }
        val dest = if (mode == "extension") {
            pluginInstallPath(pluginStorageFsPath, platform)
        } else {
            globalInstallPath(platform, homedir, localAppData)
        }

        downloadAndVerify(release, assetName, dest, log)

        val metadata = InstallMetadata(
            installedBy = PLUGIN_INSTALLER_ID,
            version = release.tagName,
            mode = if (mode == "extension") InstallMode.EXTENSION else InstallMode.GLOBAL,
            assetName = assetName,
            installedAt = Instant.now().toString(),
        )
        if (mode == "extension") {
            writeInstallMetadata(dest.parent, metadata)
        }

        log.appendLine("Installed tyhp ${release.tagName} at $dest")
        return InstallResult(
            executablePath = dest.toString(),
            version = release.tagName,
            assetName = assetName,
            mode = mode,
            variant = variant,
        )
    }
}

private fun downloadAndVerify(
    release: GithubRelease,
    assetName: String,
    destPath: Path,
    log: InstallerLogger,
) {
    val asset = requireAsset(release, assetName)
    val checksumsAsset = findAsset(release, CHECKSUMS_ASSET)
        ?: throw IllegalStateException(
            "GitHub release ${release.tagName} has no `$CHECKSUMS_ASSET` asset. " +
                "Refusing to install without a SHA-256 checksum.",
        )
    if (checksumsAsset.browserDownloadUrl.isEmpty()) {
        throw IllegalStateException(
            "GitHub release ${release.tagName} has no `$CHECKSUMS_ASSET` asset. " +
                "Refusing to install without a SHA-256 checksum.",
        )
    }

    log.appendLine("Fetching $CHECKSUMS_ASSET from ${release.tagName}…")
    val checksumText = httpGetText(checksumsAsset.browserDownloadUrl, downloadHeaders(), 30_000)
    val checksums = parseChecksumFile(checksumText)
    val expected = expectedChecksum(checksums, assetName)

    val tmpDir = Files.createTempDirectory("tyhp-cli-")
    val tmpFile = tmpDir.resolve(assetName)
    try {
        log.appendLine("Downloading $assetName (${asset.size} bytes)…")
        httpDownloadFile(asset.browserDownloadUrl, tmpFile)
        val size = Files.size(tmpFile)
        if (size <= 0) {
            throw IllegalStateException("Downloaded artifact `$assetName` is empty.")
        }
        log.appendLine("Verifying SHA-256…")
        val actual = sha256File(tmpFile)
        assertChecksum(actual, expected, assetName)

        Files.createDirectories(destPath.parent)
        Files.copy(tmpFile, destPath, StandardCopyOption.REPLACE_EXISTING)
        makeExecutable(destPath)
    } finally {
        try {
            Files.walk(tmpDir).sorted(Comparator.reverseOrder()).forEach { Files.deleteIfExists(it) }
        } catch (_: Exception) {
            // ignore
        }
    }
}

private fun makeExecutable(path: Path) {
    try {
        val perms = EnumSet.of(
            PosixFilePermission.OWNER_READ,
            PosixFilePermission.OWNER_WRITE,
            PosixFilePermission.OWNER_EXECUTE,
            PosixFilePermission.GROUP_READ,
            PosixFilePermission.GROUP_EXECUTE,
            PosixFilePermission.OTHERS_READ,
            PosixFilePermission.OTHERS_EXECUTE,
        )
        Files.setPosixFilePermissions(path, perms)
    } catch (_: UnsupportedOperationException) {
        // Windows / non-POSIX
    } catch (_: Exception) {
        // ignore
    }
}
