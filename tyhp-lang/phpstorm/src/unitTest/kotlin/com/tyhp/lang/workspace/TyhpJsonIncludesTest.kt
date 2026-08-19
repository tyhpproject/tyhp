package com.tyhp.lang.workspace

import kotlin.test.Test
import kotlin.test.assertEquals

class TyhpJsonIncludesTest {
    @Test
    fun `reads include and exclude arrays`() {
        val globs = parseTyhpJsonGlobs(
            """
            {
                "include": ["./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef"],
                "exclude": ["./skip/**"]
            }
            """.trimIndent(),
        )
        assertEquals(
            listOf("./tyhp_src/**/*.tyhp", "../../php-extensions/php8.2.9/**/*.tyhpdef"),
            globs.include,
        )
        assertEquals(listOf("./skip/**"), globs.exclude)
    }

    @Test
    fun `missing include is empty`() {
        assertEquals(emptyList(), parseTyhpJsonGlobs("""{ "exclude": [] }""").include)
    }

    @Test
    fun `invalid JSON is treated as empty include`() {
        assertEquals(emptyList(), parseTyhpJsonGlobs("{ not json").include)
    }

    @Test
    fun `non-object JSON is treated as empty include`() {
        assertEquals(emptyList(), parseTyhpJsonGlobs("[]").include)
        assertEquals(emptyList(), parseTyhpJsonGlobs("null").include)
        assertEquals(emptyList(), parseTyhpJsonGlobs("\"x\"").include)
    }
}
