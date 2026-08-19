package com.tyhp.lang.debug

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ProxyGuidanceTest {
    @Test
    fun `Tyhp remote debug configs mention tyhp`() {
        assertTrue(isTyhpPhpRemoteDebugConfig(TYHP_PHP_DEBUG_CONFIG_NAME, PHP_REMOTE_DEBUG_TYPE_ID))
        assertFalse(isTyhpPhpRemoteDebugConfig("Listen for Xdebug", PHP_REMOTE_DEBUG_TYPE_ID))
        assertFalse(isTyhpPhpRemoteDebugConfig("Listen for Tyhp", "NodeJSConfigurationType"))
    }

    @Test
    fun `missing PHP Remote Debug guidance names PhpStorm XDebug`() {
        assertContains(phpRemoteDebugMissingGuidance(), "PHP Remote Debug")
        assertContains(phpRemoteDebugMissingGuidance(), XDEBUG_PROXY_DOCS_URL)
    }

    @Test
    fun `proxy-down guidance names the IDE port and start command`() {
        val text = proxyDownGuidance(9003)
        assertContains(text, "9003")
        assertContains(text, "Start XDebug Proxy")
    }

    @Test
    fun `sourcemap guidance when generateSourcemap is off`() {
        val text = sourcemapGuidance(SourcemapGuidanceOptions(generateSourcemap = false))
        assertNotNull(text)
        assertContains(text, "generateSourcemap")
        assertContains(text, SOURCEMAP_DOCS_URL)
    }

    @Test
    fun `sourcemap guidance when maps are missing after a build flag is on`() {
        val text = sourcemapGuidance(
            SourcemapGuidanceOptions(
                generateSourcemap = true,
                mapCount = 0,
                sourceMapDir = "./build/",
            ),
        )
        assertNotNull(text)
        assertContains(text, ".php.map")
        assertContains(text, "build")
    }

    @Test
    fun `no sourcemap guidance when maps are present`() {
        assertNull(sourcemapGuidance(SourcemapGuidanceOptions(generateSourcemap = true, mapCount = 3)))
    }
}
