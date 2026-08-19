package com.tyhp.lang.binary

/**
 * Minimal JSON reader for GitHub API payloads and install metadata.
 * IntelliJ-free so unit tests do not need the platform test framework.
 */
internal fun parseJson(text: String): Any? = JsonReader(text).parseValue()

private class JsonReader(private val s: String) {
    private var i = 0

    fun parseValue(): Any? {
        skipWs()
        if (i >= s.length) {
            throw IllegalStateException("Unexpected end of JSON")
        }
        return when (val c = s[i]) {
            '{' -> parseObject()
            '[' -> parseArray()
            '"' -> parseString()
            't', 'f' -> parseBoolean()
            'n' -> parseNull()
            else -> if (c == '-' || c in '0'..'9') {
                parseNumber()
            } else {
                throw IllegalStateException("Unexpected JSON at index $i")
            }
        }
    }

    private fun parseObject(): Map<String, Any?> {
        expect('{')
        val fields = LinkedHashMap<String, Any?>()
        skipWs()
        if (peek('}')) {
            expect('}')
            return fields
        }
        while (true) {
            skipWs()
            val key = parseString()
            skipWs()
            expect(':')
            fields[key] = parseValue()
            skipWs()
            if (peek('}')) {
                expect('}')
                return fields
            }
            expect(',')
        }
    }

    private fun parseArray(): List<Any?> {
        expect('[')
        val items = ArrayList<Any?>()
        skipWs()
        if (peek(']')) {
            expect(']')
            return items
        }
        while (true) {
            items.add(parseValue())
            skipWs()
            if (peek(']')) {
                expect(']')
                return items
            }
            expect(',')
        }
    }

    private fun parseString(): String {
        expect('"')
        val out = StringBuilder()
        while (i < s.length) {
            val c = s[i++]
            when (c) {
                '"' -> return out.toString()
                '\\' -> {
                    if (i >= s.length) {
                        throw IllegalStateException("Unterminated escape in JSON string")
                    }
                    when (val e = s[i++]) {
                        '"', '\\', '/' -> out.append(e)
                        'b' -> out.append('\b')
                        'f' -> out.append('\u000c')
                        'n' -> out.append('\n')
                        'r' -> out.append('\r')
                        't' -> out.append('\t')
                        'u' -> {
                            if (i + 4 > s.length) {
                                throw IllegalStateException("Invalid unicode escape")
                            }
                            val hex = s.substring(i, i + 4)
                            out.append(hex.toInt(16).toChar())
                            i += 4
                        }
                        else -> throw IllegalStateException("Invalid escape \\$e")
                    }
                }
                else -> out.append(c)
            }
        }
        throw IllegalStateException("Unterminated JSON string")
    }

    private fun parseBoolean(): Boolean {
        return when {
            match("true") -> true
            match("false") -> false
            else -> throw IllegalStateException("Invalid boolean at index $i")
        }
    }

    private fun parseNull(): Any? {
        if (!match("null")) {
            throw IllegalStateException("Invalid null at index $i")
        }
        return null
    }

    private fun parseNumber(): Number {
        val start = i
        if (i < s.length && s[i] == '-') {
            i++
        }
        while (i < s.length && s[i] in '0'..'9') {
            i++
        }
        if (i < s.length && s[i] == '.') {
            i++
            while (i < s.length && s[i] in '0'..'9') {
                i++
            }
        }
        if (i < s.length && (s[i] == 'e' || s[i] == 'E')) {
            i++
            if (i < s.length && (s[i] == '+' || s[i] == '-')) {
                i++
            }
            while (i < s.length && s[i] in '0'..'9') {
                i++
            }
        }
        val raw = s.substring(start, i)
        return if (raw.contains('.') || raw.contains('e', ignoreCase = true)) {
            raw.toDouble()
        } else {
            raw.toLong()
        }
    }

    private fun skipWs() {
        while (i < s.length && s[i].isWhitespace()) {
            i++
        }
    }

    private fun peek(c: Char): Boolean {
        skipWs()
        return i < s.length && s[i] == c
    }

    private fun expect(c: Char) {
        skipWs()
        if (i >= s.length || s[i] != c) {
            throw IllegalStateException("Expected '$c' at index $i")
        }
        i++
    }

    private fun match(literal: String): Boolean {
        skipWs()
        if (s.startsWith(literal, i)) {
            i += literal.length
            return true
        }
        return false
    }
}
