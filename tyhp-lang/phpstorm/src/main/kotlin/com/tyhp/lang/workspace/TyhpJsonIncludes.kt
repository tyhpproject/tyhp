package com.tyhp.lang.workspace

data class TyhpJsonGlobs(
    val include: List<String> = emptyList(),
    val exclude: List<String> = emptyList(),
)

/**
 * Reads `include` / `exclude` glob arrays from a `tyhp.json` document.
 * Invalid JSON or a non-object is treated as `include: []` (owns nothing).
 * Platform-JSON-free so unit tests do not need Gson / IntelliJ.
 */
fun parseTyhpJsonGlobs(raw: String): TyhpJsonGlobs {
    val parsed = parseJson(raw) ?: return TyhpJsonGlobs()
    val obj = parsed as? JsonMap ?: return TyhpJsonGlobs()
    return TyhpJsonGlobs(
        include = stringList(obj["include"]),
        exclude = stringList(obj["exclude"]),
    )
}

private fun stringList(value: Any?): List<String> {
    val list = value as? List<*> ?: return emptyList()
    return list.mapNotNull { item ->
        (item as? String)?.trim()?.takeIf { it.isNotEmpty() }
    }
}

private class JsonMap(map: Map<String, Any?>) : LinkedHashMap<String, Any?>(map)

private fun parseJson(raw: String): Any? {
    return try {
        JsonReader(raw).parseValue()
    } catch (_: Throwable) {
        null
    }
}

private class JsonReader(private val text: String) {
    private var i = 0

    fun parseValue(): Any? {
        skipWs()
        if (i >= text.length) {
            throw IllegalArgumentException("empty")
        }
        return when (val c = text[i]) {
            '{' -> parseObject()
            '[' -> parseArray()
            '"' -> parseString()
            't', 'f' -> parseBool()
            'n' -> parseNull()
            '-', in '0'..'9' -> parseNumber()
            else -> throw IllegalArgumentException("unexpected $c")
        }
    }

    private fun parseObject(): JsonMap {
        expect('{')
        val map = LinkedHashMap<String, Any?>()
        skipWs()
        if (peek() == '}') {
            i += 1
            return JsonMap(map)
        }
        while (true) {
            skipWs()
            val key = parseString()
            skipWs()
            expect(':')
            val value = parseValue()
            map[key] = value
            skipWs()
            when (peek()) {
                ',' -> i += 1
                '}' -> {
                    i += 1
                    return JsonMap(map)
                }
                else -> throw IllegalArgumentException("expected comma or }")
            }
        }
    }

    private fun parseArray(): List<Any?> {
        expect('[')
        val list = ArrayList<Any?>()
        skipWs()
        if (peek() == ']') {
            i += 1
            return list
        }
        while (true) {
            list.add(parseValue())
            skipWs()
            when (peek()) {
                ',' -> i += 1
                ']' -> {
                    i += 1
                    return list
                }
                else -> throw IllegalArgumentException("expected comma or ]")
            }
        }
    }

    private fun parseString(): String {
        expect('"')
        val sb = StringBuilder()
        while (i < text.length) {
            val c = text[i]
            i += 1
            when (c) {
                '"' -> return sb.toString()
                '\\' -> {
                    if (i >= text.length) {
                        throw IllegalArgumentException("bad escape")
                    }
                    val e = text[i]
                    i += 1
                    sb.append(
                        when (e) {
                            '"', '\\', '/' -> e
                            'b' -> '\b'
                            'f' -> '\u000c'
                            'n' -> '\n'
                            'r' -> '\r'
                            't' -> '\t'
                            'u' -> {
                                val hex = text.substring(i, i + 4)
                                i += 4
                                hex.toInt(16).toChar()
                            }
                            else -> e
                        },
                    )
                }
                else -> sb.append(c)
            }
        }
        throw IllegalArgumentException("unterminated string")
    }

    private fun parseBool(): Boolean {
        return when {
            text.startsWith("true", i) -> {
                i += 4
                true
            }
            text.startsWith("false", i) -> {
                i += 5
                false
            }
            else -> throw IllegalArgumentException("bad bool")
        }
    }

    private fun parseNull(): Any? {
        if (!text.startsWith("null", i)) {
            throw IllegalArgumentException("bad null")
        }
        i += 4
        return null
    }

    private fun parseNumber(): Number {
        val start = i
        if (peek() == '-') {
            i += 1
        }
        while (peek() in '0'..'9') {
            i += 1
        }
        if (peek() == '.') {
            i += 1
            while (peek() in '0'..'9') {
                i += 1
            }
        }
        val raw = text.substring(start, i)
        return if (raw.contains('.')) raw.toDouble() else raw.toLong()
    }

    private fun skipWs() {
        while (i < text.length && text[i].isWhitespace()) {
            i += 1
        }
    }

    private fun peek(): Char = if (i < text.length) text[i] else '\u0000'

    private fun expect(c: Char) {
        skipWs()
        if (peek() != c) {
            throw IllegalArgumentException("expected $c")
        }
        i += 1
    }
}
