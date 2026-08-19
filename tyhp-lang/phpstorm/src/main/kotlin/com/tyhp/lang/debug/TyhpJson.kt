package com.tyhp.lang.debug

import com.tyhp.lang.binary.parseJson

/**
 * Reads the `xdebugProxy`, `build.generateSourcemap`, and `output.path`
 * fields from a `tyhp.json` document. Returns `null` when the text is not JSON.
 */
fun parseTyhpJsonProject(raw: String): TyhpJsonProjectSnapshot? {
    val parsed = try {
        parseJson(raw)
    } catch (_: Throwable) {
        return null
    }
    val root = parsed as? Map<*, *> ?: return null
    val build = asObject(root["build"])
    val output = asObject(root["output"])
    val proxyRaw = asObject(root["xdebugProxy"])
    return TyhpJsonProjectSnapshot(
        xdebugProxy = proxyRaw?.let { parseProxySection(it) },
        generateSourcemap = readBool(build?.get("generateSourcemap")) == true,
        outputPath = readString(output?.get("path")),
    )
}

private fun parseProxySection(raw: Map<*, *>): TyhpJsonProxySection {
    return TyhpJsonProxySection(
        idePort = readPort(raw["idePort"]),
        xdebugPort = readPort(raw["xdebugPort"]),
        sourceMapDir = readString(raw["sourceMapDir"]),
        ideKey = readString(raw["ideKey"]),
    )
}

private fun asObject(value: Any?): Map<*, *>? = value as? Map<*, *>

private fun readBool(value: Any?): Boolean? = value as? Boolean

private fun readString(value: Any?): String? {
    if (value !is String) {
        return null
    }
    val trimmed = value.trim()
    return trimmed.ifEmpty { null }
}

private fun readPort(value: Any?): Int? {
    when (value) {
        is Number -> {
            val asDouble = value.toDouble()
            if (asDouble != kotlin.math.floor(asDouble)) {
                return null
            }
            val asInt = value.toInt()
            return if (isValidPort(asInt)) asInt else null
        }
        is String -> {
            val trimmed = value.trim()
            if (trimmed.isEmpty()) {
                return null
            }
            val parsed = trimmed.toIntOrNull() ?: return null
            return if (isValidPort(parsed)) parsed else null
        }
        else -> return null
    }
}
