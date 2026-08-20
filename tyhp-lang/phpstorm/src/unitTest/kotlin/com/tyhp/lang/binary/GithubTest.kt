package com.tyhp.lang.binary

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class GithubTest {
    private fun release(tag: String, draft: Boolean, assets: List<String>): GithubRelease {
        return GithubRelease(
            tagName = tag,
            draft = draft,
            prerelease = tag.contains("alpha"),
            publishedAt = "2026-01-01T00:00:00Z",
            assets = assets.map { name ->
                GithubReleaseAsset(
                    name = name,
                    browserDownloadUrl = "https://github.com/tyhpproject/tyhp/releases/download/$tag/$name",
                    size = 10,
                )
            },
        )
    }

    @Test
    fun `selectLatestRelease skips drafts and keeps prereleases`() {
        val releases = listOf(
            release("v805.0.0-alpha.2", true, listOf("tyhp-osx-arm64")),
            release("v805.0.0-alpha.1", false, listOf("tyhp-osx-arm64")),
        )
        assertEquals("v805.0.0-alpha.1", selectLatestRelease(releases)?.tagName)
    }

    @Test
    fun `requireAsset fails clearly when the platform binary is missing`() {
        val rel = release("v805.0.0-alpha.1", false, listOf("checksums.txt"))
        assertNull(findAsset(rel, "tyhp-osx-arm64"))
        val err = assertFailsWith<IllegalStateException> { requireAsset(rel, "tyhp-osx-arm64") }
        assertTrueMessage(err.message.orEmpty())
        assertEquals("checksums.txt", requireAsset(rel, "checksums.txt").name)
    }

    @Test
    fun `parseGithubReleaseList reads GitHub API JSON including prereleases`() {
        val json = """
            [
              {
                "tag_name": "v805.0.0-alpha.2",
                "draft": true,
                "prerelease": true,
                "published_at": "2026-01-02T00:00:00Z",
                "assets": []
              },
              {
                "tag_name": "v805.0.0-alpha.1",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-01-01T00:00:00Z",
                "assets": [
                  {
                    "name": "tyhp-osx-arm64",
                    "browser_download_url": "https://example.test/tyhp-osx-arm64",
                    "size": 42
                  }
                ]
              }
            ]
        """.trimIndent()
        val releases = parseGithubReleaseList(json)
        assertEquals("v805.0.0-alpha.1", selectLatestRelease(releases)?.tagName)
        assertEquals(42L, findAsset(releases[1], "tyhp-osx-arm64")?.size)
    }

    private fun assertTrueMessage(message: String) {
        kotlin.test.assertTrue(message.contains("no asset named"), message)
    }
}
