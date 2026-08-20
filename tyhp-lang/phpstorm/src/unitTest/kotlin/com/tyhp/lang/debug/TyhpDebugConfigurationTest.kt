package com.tyhp.lang.debug

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class TyhpDebugConfigurationTest {
    @Test
    fun `PHP Remote Debug plan points PhpStorm at the proxy IDE port`() {
        val launch = resolveProxyLaunch(
            ExplicitProxySettings(),
            TyhpJsonProjectSnapshot(
                generateSourcemap = true,
                xdebugProxy = TyhpJsonProxySection(idePort = 9003, xdebugPort = 9004, ideKey = "tyhp"),
            ),
        )
        val plan = phpRemoteDebugPlan(launch)
        assertEquals(TYHP_PHP_DEBUG_CONFIG_NAME, plan.configurationName)
        assertEquals(9003, plan.debugPort)
        assertEquals(9004, plan.xdebugClientPort)
        assertEquals("tyhp", plan.ideKey)
        assertContains(plan.phpIniSnippet, "xdebug.client_port = 9004")
        assertContains(plan.phpIniSnippet, "xdebug.idekey = tyhp")
        assertTrue(plan.setupSteps.any { it.contains("Debug port") && it.contains("9003") })
        assertTrue(plan.setupSteps.any { it.contains("client_port") && it.contains("9004") })
        assertContains(phpRemoteDebugSummary(plan), "9003")
        assertContains(phpRemoteDebugSummary(plan), "9004")
    }

    @Test
    fun `omitted idekey is any and php ini has no idekey line`() {
        val launch = resolveProxyLaunch(ExplicitProxySettings(), TyhpJsonProjectSnapshot(generateSourcemap = true))
        val plan = phpRemoteDebugPlan(launch)
        assertNull(plan.ideKey)
        assertTrue(!plan.phpIniSnippet.contains("idekey"))
        assertContains(phpRemoteDebugSummary(plan), "(any)")
    }
}
