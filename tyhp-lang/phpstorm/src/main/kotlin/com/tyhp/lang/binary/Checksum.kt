package com.tyhp.lang.binary

import java.nio.file.Files
import java.nio.file.Path
import java.security.MessageDigest

class ChecksumError(message: String) : RuntimeException(message)

private val HASH_RE = Regex("""^([a-fA-F0-9]{64})\s+\*?(\S+)\s*$""")

/** Parse GNU `sha256sum` output (`checksums.txt` from `scripts/release.sh`). */
fun parseChecksumFile(contents: String): Map<String, String> {
    val map = LinkedHashMap<String, String>()
    for (rawLine in contents.split(Regex("\\r?\\n"))) {
        val line = rawLine.trim()
        if (line.isEmpty() || line.startsWith("#")) {
            continue
        }
        val match = HASH_RE.matchEntire(line) ?: continue
        map[match.groupValues[2]] = match.groupValues[1].lowercase()
    }
    return map
}

fun expectedChecksum(checksums: Map<String, String>, assetName: String): String {
    return checksums[assetName]
        ?: throw ChecksumError(
            "checksums.txt has no SHA-256 for `$assetName`. Refusing to install an unverified binary.",
        )
}

fun sha256File(filePath: Path): String {
    val digest = MessageDigest.getInstance("SHA-256")
    Files.newInputStream(filePath).use { input ->
        val buffer = ByteArray(8192)
        while (true) {
            val read = input.read(buffer)
            if (read < 0) {
                break
            }
            digest.update(buffer, 0, read)
        }
    }
    return digest.digest().joinToString("") { byte -> "%02x".format(byte) }
}

fun assertChecksum(actualHex: String, expectedHex: String, assetName: String) {
    if (actualHex.lowercase() != expectedHex.lowercase()) {
        throw ChecksumError(
            "SHA-256 mismatch for `$assetName`: expected $expectedHex, got $actualHex. " +
                "The download may be corrupt or tampered with.",
        )
    }
}
