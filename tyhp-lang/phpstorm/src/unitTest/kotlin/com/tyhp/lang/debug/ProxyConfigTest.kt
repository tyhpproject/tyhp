package com.tyhp.lang.debug

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ProxyConfigTest {
    @Test
    fun `default argv is xdebug_proxy with resolved default ports`() {
        assertEquals(
            listOf(XDEBUG_PROXY_ACTION, "--ide-port=$DEFAULT_IDE_PORT", "--xdebug-port=$DEFAULT_XDEBUG_PORT"),
            buildXdebugProxyArgs(
                XdebugProxyArgOptions(
                    idePort = DEFAULT_IDE_PORT,
                    xdebugPort = DEFAULT_XDEBUG_PORT,
                ),
            ),
        )
    }

    @Test
    fun `passes --tyhp-project and optional sourcemap and ide-key as inline value flags`() {
        assertEquals(
            listOf(
                XDEBUG_PROXY_ACTION,
                "--tyhp-project=/ws/tyhp.json",
                "--ide-port=9010",
                "--xdebug-port=9011",
                "--sourcemap-dir=./build/",
                "--ide-key=tyhp",
            ),
            buildXdebugProxyArgs(
                XdebugProxyArgOptions(
                    projectFilePath = "/ws/tyhp.json",
                    idePort = 9010,
                    xdebugPort = 9011,
                    sourceMapDir = "./build/",
                    ideKey = "tyhp",
                ),
            ),
        )
    }

    @Test
    fun `omits empty project, sourcemap dir, and ide-key`() {
        assertEquals(
            listOf(XDEBUG_PROXY_ACTION, "--ide-port=9003", "--xdebug-port=9004"),
            buildXdebugProxyArgs(
                XdebugProxyArgOptions(
                    projectFilePath = "  ",
                    idePort = 9003,
                    xdebugPort = 9004,
                    sourceMapDir = "",
                    ideKey = "   ",
                ),
            ),
        )
    }

    @Test
    fun `settings ports win over tyhp json`() {
        val launch = resolveProxyLaunch(
            ExplicitProxySettings(idePort = 9111, xdebugPort = 9222),
            TyhpJsonProjectSnapshot(
                generateSourcemap = true,
                xdebugProxy = TyhpJsonProxySection(
                    idePort = 9005,
                    xdebugPort = 9006,
                    sourceMapDir = "/from-json",
                ),
            ),
        )
        assertEquals(9111, launch.idePort)
        assertEquals(9222, launch.xdebugPort)
        assertEquals(ConfigValueSource.SETTINGS, launch.idePortSource)
        assertEquals(ConfigValueSource.SETTINGS, launch.xdebugPortSource)
        assertEquals("/from-json", launch.sourceMapDir)
        assertEquals(ConfigValueSource.TYHP_JSON, launch.sourceMapDirSource)
        assertTrue(launch.generateSourcemap)
    }

    @Test
    fun `tyhp json ports win when settings are not explicit`() {
        val launch = resolveProxyLaunch(
            ExplicitProxySettings(),
            TyhpJsonProjectSnapshot(
                generateSourcemap = false,
                outputPath = "build/",
                xdebugProxy = TyhpJsonProxySection(idePort = 9010, xdebugPort = 9011, ideKey = "app"),
            ),
        )
        assertEquals(9010, launch.idePort)
        assertEquals(9011, launch.xdebugPort)
        assertEquals(ConfigValueSource.TYHP_JSON, launch.idePortSource)
        assertEquals(ConfigValueSource.TYHP_JSON, launch.xdebugPortSource)
        assertEquals("app", launch.ideKey)
        assertEquals(ConfigValueSource.TYHP_JSON, launch.ideKeySource)
        assertEquals(ConfigValueSource.OMITTED, launch.sourceMapDirSource)
        assertEquals("build/", launch.outputPath)
    }

    @Test
    fun `Story 18 defaults apply when neither settings nor tyhp json set ports`() {
        val launch = resolveProxyLaunch(ExplicitProxySettings(), TyhpJsonProjectSnapshot(generateSourcemap = true))
        assertEquals(DEFAULT_IDE_PORT, launch.idePort)
        assertEquals(DEFAULT_XDEBUG_PORT, launch.xdebugPort)
        assertEquals(ConfigValueSource.DEFAULT, launch.idePortSource)
        assertEquals(ConfigValueSource.DEFAULT, launch.xdebugPortSource)
        assertTrue(launch.generateSourcemap)
    }

    @Test
    fun `settings sourcemap dir wins over tyhp json`() {
        val launch = resolveProxyLaunch(
            ExplicitProxySettings(sourceMapDir = "/from-settings"),
            TyhpJsonProjectSnapshot(
                generateSourcemap = true,
                xdebugProxy = TyhpJsonProxySection(sourceMapDir = "/from-json"),
            ),
        )
        assertEquals("/from-settings", launch.sourceMapDir)
        assertEquals(ConfigValueSource.SETTINGS, launch.sourceMapDirSource)
    }

    @Test
    fun `invalid settings port falls through to tyhp json then default`() {
        val fromJson = resolveProxyLaunch(
            ExplicitProxySettings(idePort = 70000, xdebugPort = -1),
            TyhpJsonProjectSnapshot(
                generateSourcemap = false,
                xdebugProxy = TyhpJsonProxySection(idePort = 9010, xdebugPort = 9011),
            ),
        )
        assertEquals(9010, fromJson.idePort)
        assertEquals(9011, fromJson.xdebugPort)

        val fromDefault = resolveProxyLaunch(
            ExplicitProxySettings(idePort = 70000),
            TyhpJsonProjectSnapshot(generateSourcemap = false),
        )
        assertEquals(DEFAULT_IDE_PORT, fromDefault.idePort)
    }

    @Test
    fun `empty settings text is not explicit so tyhp json is not shadowed`() {
        assertNull(parseExplicitPortText(""))
        assertNull(parseExplicitPortText("   "))
        assertNull(parseExplicitPortText(null))
        assertEquals(9003, parseExplicitPortText("9003"))
        assertNull(parseExplicitPortText("70000"))

        val launch = resolveProxyLaunch(
            ExplicitProxySettings(
                idePort = parseExplicitPortText(""),
                xdebugPort = parseExplicitPortText("  "),
            ),
            TyhpJsonProjectSnapshot(
                generateSourcemap = false,
                xdebugProxy = TyhpJsonProxySection(idePort = 9110, xdebugPort = 9111),
            ),
        )
        assertEquals(9110, launch.idePort)
        assertEquals(9111, launch.xdebugPort)
        assertEquals(ConfigValueSource.TYHP_JSON, launch.idePortSource)
    }

    @Test
    fun `buildXdebugProxyArgsFromLaunch includes resolved ports and omits empty optionals`() {
        val launch = resolveProxyLaunch(ExplicitProxySettings(), TyhpJsonProjectSnapshot(generateSourcemap = true))
        assertEquals(
            listOf(
                XDEBUG_PROXY_ACTION,
                "--tyhp-project=/p/tyhp.json",
                "--ide-port=9003",
                "--xdebug-port=9004",
            ),
            buildXdebugProxyArgsFromLaunch(launch, "/p/tyhp.json"),
        )
    }

    @Test
    fun `parses bound IDE port from the CLI startup banner`() {
        assertEquals(9003, parseBoundIdePort("  IDE port:      9003"))
        assertEquals(0, parseBoundIdePort("  IDE port:      0"))
        assertNull(parseBoundIdePort("  XDebug port:   9004"))
    }

    @Test
    fun `detects the CLI no-sourcemaps warning`() {
        assertTrue(
            lineWarnsNoSourcemaps(
                "No sourcemaps found in `./build/`. Build the project with sourcemap generation enabled first.",
            ),
        )
        assertEquals(false, lineWarnsNoSourcemaps("XDebug Proxy started"))
    }

    @Test
    fun `counts php map files case-insensitively`() {
        assertEquals(2, countPhpMapFiles(listOf("User.php", "User.php.map", "nested/Foo.PHP.MAP", "readme.md")))
    }
}
