package com.tyhp.lang.binary

import java.nio.file.Files
import java.security.MessageDigest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class ChecksumTest {
    @Test
    fun `parseChecksumFile reads GNU sha256sum lines from scripts release sh`() {
        val hash = "a".repeat(64)
        val contents = "$hash  tyhp-osx-arm64\n${"b".repeat(64)} *tyhp-win-x64.exe\n# comment\n"
        val map = parseChecksumFile(contents)
        assertEquals(hash, map["tyhp-osx-arm64"])
        assertEquals("b".repeat(64), map["tyhp-win-x64.exe"])
        assertEquals(hash, expectedChecksum(map, "tyhp-osx-arm64"))
        assertFailsWith<ChecksumError> { expectedChecksum(map, "missing") }
    }

    @Test
    fun `sha256File matches digest and assertChecksum rejects mismatches`() {
        val dir = Files.createTempDirectory("tyhp-checksum-")
        val file = dir.resolve("blob")
        val payload = "tyhp-cli-fixture".toByteArray()
        Files.write(file, payload)
        try {
            val expected = MessageDigest.getInstance("SHA-256").digest(payload)
                .joinToString("") { "%02x".format(it) }
            val actual = sha256File(file)
            assertEquals(expected, actual)
            assertChecksum(actual, expected, "blob")
            assertFailsWith<ChecksumError> { assertChecksum(actual, "c".repeat(64), "blob") }
        } finally {
            dir.toFile().deleteRecursively()
        }
    }
}
