package com.tyhp.lang.lsp

import kotlin.test.Test
import kotlin.test.assertEquals

class LanguageServerArgsTest {
    @Test
    fun `default argv is language_server --quiet --stdio`() {
        assertEquals(
            listOf(LANGUAGE_SERVER_ACTION, "--quiet", "--stdio"),
            buildLanguageServerArgs(),
        )
    }

    @Test
    fun `passes --tyhp-project as an inline value flag when a project file is known`() {
        assertEquals(
            listOf(
                LANGUAGE_SERVER_ACTION,
                "--quiet",
                "--stdio",
                "--tyhp-project=/ws/tyhp.json",
            ),
            buildLanguageServerArgs(LanguageServerArgOptions(projectFilePath = "/ws/tyhp.json")),
        )
    }

    @Test
    fun `omits --tyhp-project when the path is empty or whitespace`() {
        assertEquals(
            listOf(LANGUAGE_SERVER_ACTION, "--quiet", "--stdio"),
            buildLanguageServerArgs(LanguageServerArgOptions(projectFilePath = "  ")),
        )
    }

    @Test
    fun `appends extra args after the subcommand and does not duplicate language_server`() {
        assertEquals(
            listOf(
                LANGUAGE_SERVER_ACTION,
                "--quiet",
                "--stdio",
                "--tyhp-project=/p/tyhp.json",
                "--locale=en-US",
            ),
            buildLanguageServerArgs(
                LanguageServerArgOptions(
                    extraArgs = listOf(LANGUAGE_SERVER_ACTION, "--locale=en-US"),
                    projectFilePath = "/p/tyhp.json",
                ),
            ),
        )
    }

    @Test
    fun `does not add built-in flags that extra args already supply`() {
        assertEquals(
            listOf(
                LANGUAGE_SERVER_ACTION,
                "--quiet",
                "--stdio",
                "--tyhp-project",
                "/other/tyhp.json",
            ),
            buildLanguageServerArgs(
                LanguageServerArgOptions(
                    extraArgs = listOf("--quiet", "--stdio", "--tyhp-project", "/other/tyhp.json"),
                    projectFilePath = "/ws/tyhp.json",
                ),
            ),
        )
    }

    @Test
    fun `honors -q as the short quiet alias`() {
        assertEquals(
            listOf(LANGUAGE_SERVER_ACTION, "--stdio", "-q"),
            buildLanguageServerArgs(LanguageServerArgOptions(extraArgs = listOf("-q"))),
        )
    }

    @Test
    fun `can suppress quiet and stdio when explicitly disabled`() {
        assertEquals(
            listOf(LANGUAGE_SERVER_ACTION),
            buildLanguageServerArgs(LanguageServerArgOptions(quiet = false, stdio = false)),
        )
    }
}
