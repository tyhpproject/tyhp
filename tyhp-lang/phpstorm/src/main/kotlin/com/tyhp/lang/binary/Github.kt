package com.tyhp.lang.binary

import com.tyhp.lang.settings.normalizeReleaseTag
import java.net.URLEncoder
import java.nio.charset.StandardCharsets

const val GITHUB_REPO = "tyhpproject/tyhp"
const val CHECKSUMS_ASSET = "checksums.txt"

data class GithubReleaseAsset(
    val name: String,
    val browserDownloadUrl: String,
    val size: Long,
)

data class GithubRelease(
    val tagName: String,
    val draft: Boolean,
    val prerelease: Boolean,
    val publishedAt: String,
    val assets: List<GithubReleaseAsset>,
    val message: String? = null,
)

fun releasesListUrl(repo: String = GITHUB_REPO): String =
    "https://api.github.com/repos/$repo/releases?per_page=20"

fun releaseByTagUrl(tag: String, repo: String = GITHUB_REPO): String {
    val encoded = URLEncoder.encode(normalizeReleaseTag(tag), StandardCharsets.UTF_8)
    return "https://api.github.com/repos/$repo/releases/tags/$encoded"
}

/** First non-draft release, including prereleases. `/releases/latest` hides prereleases. */
fun selectLatestRelease(releases: List<GithubRelease>): GithubRelease? =
    releases.firstOrNull { !it.draft }

fun findAsset(release: GithubRelease, name: String): GithubReleaseAsset? =
    release.assets.firstOrNull { it.name == name }

fun requireAsset(release: GithubRelease, name: String): GithubReleaseAsset {
    val asset = findAsset(release, name)
    if (asset == null || asset.browserDownloadUrl.isEmpty()) {
        throw IllegalStateException(
            "GitHub release ${release.tagName} has no asset named `$name`. " +
                "See the plugin README for the expected asset names. If this tag predates compiler binaries, pick another tag or install from PATH.",
        )
    }
    return asset
}

fun parseGithubReleaseList(json: String): List<GithubRelease> {
    val parsed = parseJson(json)
    val list = parsed as? List<*>
        ?: throw IllegalStateException("GitHub API returned a non-array listing releases")
    return list.map { parseReleaseObject(asObject(it, "release")) }
}

fun parseGithubRelease(json: String): GithubRelease {
    val parsed = parseJson(json)
    val obj = asObject(parsed, "release")
    return parseReleaseObject(obj)
}

private fun parseReleaseObject(obj: Map<String, Any?>): GithubRelease {
    val assetsRaw = obj["assets"] as? List<*> ?: emptyList<Any?>()
    val assets = assetsRaw.map { asset ->
        val a = asObject(asset, "asset")
        GithubReleaseAsset(
            name = a["name"] as? String ?: "",
            browserDownloadUrl = a["browser_download_url"] as? String ?: "",
            size = jsonLong(a["size"]),
        )
    }
    return GithubRelease(
        tagName = obj["tag_name"] as? String ?: "",
        draft = obj["draft"] as? Boolean ?: false,
        prerelease = obj["prerelease"] as? Boolean ?: false,
        publishedAt = obj["published_at"] as? String ?: "",
        assets = assets,
        message = obj["message"] as? String,
    )
}

private fun asObject(value: Any?, label: String): Map<String, Any?> {
    @Suppress("UNCHECKED_CAST")
    return value as? Map<String, Any?>
        ?: throw IllegalStateException("GitHub API $label was not a JSON object")
}

private fun jsonLong(value: Any?): Long = when (value) {
    null -> 0L
    is Number -> value.toLong()
    is String -> value.toLongOrNull() ?: 0L
    else -> 0L
}
