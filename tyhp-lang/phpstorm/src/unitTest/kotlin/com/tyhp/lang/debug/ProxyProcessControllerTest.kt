package com.tyhp.lang.debug

import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ProxyProcessControllerTest {
    private fun createHarness(
        listenPort: Int? = null,
        failListen: String? = null,
    ): Harness {
        val spawned = CopyOnWriteArrayList<FakeSpawnedProxy>()
        val states = CopyOnWriteArrayList<ProxyRunState>()
        val controller = ProxyProcessController(
            object : ProxyProcessHooks {
                override fun spawn(command: String, args: List<String>, cwd: String?): SpawnedProxy {
                    val child = FakeSpawnedProxy()
                    spawned.add(child)
                    return child
                }

                override fun waitForListening(port: Int, child: SpawnedProxy, abort: AtomicBoolean): Int {
                    if (abort.get()) {
                        throw IllegalStateException("aborted")
                    }
                    if (failListen != null) {
                        throw IllegalStateException(failListen)
                    }
                    return listenPort ?: port
                }

                override fun stopProcess(child: SpawnedProxy) {
                    child.destroyTerm()
                }

                override fun onState(state: ProxyRunState) {
                    states.add(state)
                }
            },
        )
        return Harness(controller, spawned, states)
    }

    @Test
    fun `start then listening reaches running`() {
        val harness = createHarness()
        harness.controller.start(
            ProxyStartRequest(
                command = "/bin/tyhp",
                args = listOf("xdebug_proxy", "--ide-port=9003", "--xdebug-port=9004"),
                idePort = 9003,
            ),
        ).get(5, TimeUnit.SECONDS)
        assertEquals(ProxyRunState.RUNNING, harness.controller.currentState)
        assertEquals(1, harness.spawned.size)
        assertFalse(harness.spawned[0].killed)
        harness.controller.shutdownExecutor()
    }

    @Test
    fun `start while running is a no-op and does not spawn a second process`() {
        val harness = createHarness()
        val request = ProxyStartRequest(command = "/bin/tyhp", args = listOf("xdebug_proxy"), idePort = 9003)
        harness.controller.start(request).get(5, TimeUnit.SECONDS)
        harness.controller.start(request).get(5, TimeUnit.SECONDS)
        assertEquals(1, harness.spawned.size)
        assertEquals(ProxyRunState.RUNNING, harness.controller.currentState)
        harness.controller.shutdownExecutor()
    }

    @Test
    fun `stop signals SIGTERM via the manager stop path and reaches stopped`() {
        val harness = createHarness()
        harness.controller.start(
            ProxyStartRequest(command = "/bin/tyhp", args = listOf("xdebug_proxy"), idePort = 9003),
        ).get(5, TimeUnit.SECONDS)
        harness.controller.stop().get(5, TimeUnit.SECONDS)
        assertEquals(ProxyRunState.STOPPED, harness.controller.currentState)
        assertTrue(harness.spawned[0].killed)
        assertEquals("SIGTERM", harness.spawned[0].lastSignal)
        harness.controller.shutdownExecutor()
    }

    @Test
    fun `restart stops then starts a new process`() {
        val harness = createHarness()
        val request = ProxyStartRequest(command = "/bin/tyhp", args = listOf("xdebug_proxy"), idePort = 9003)
        harness.controller.start(request).get(5, TimeUnit.SECONDS)
        harness.controller.restart(request).get(5, TimeUnit.SECONDS)
        assertEquals(ProxyRunState.RUNNING, harness.controller.currentState)
        assertEquals(2, harness.spawned.size)
        assertTrue(harness.spawned[0].killed)
        assertFalse(harness.spawned[1].killed)
        harness.controller.shutdownExecutor()
    }

    @Test
    fun `listen failure leaves error state and stops the child`() {
        val harness = createHarness(failListen = "port in use")
        val error = assertFailsWith<Exception> {
            harness.controller.start(
                ProxyStartRequest(command = "/bin/tyhp", args = listOf("xdebug_proxy"), idePort = 9003),
            ).get(5, TimeUnit.SECONDS)
        }
        assertTrue(error.message?.contains("port in use") == true || error.cause?.message?.contains("port in use") == true)
        assertEquals(ProxyRunState.ERROR, harness.controller.currentState)
        assertTrue(harness.spawned[0].killed)
        harness.controller.shutdownExecutor()
    }

    @Test
    fun `unexpected exit from running becomes error`() {
        val harness = createHarness()
        harness.controller.start(
            ProxyStartRequest(command = "/bin/tyhp", args = listOf("xdebug_proxy"), idePort = 9003),
        ).get(5, TimeUnit.SECONDS)
        harness.spawned[0].exit(1)
        assertEquals(ProxyRunState.ERROR, harness.controller.currentState)
        harness.controller.shutdownExecutor()
    }

    @Test
    fun `stopSpawnedProxy uses SIGTERM and waits for exit`() {
        val child = FakeSpawnedProxy()
        stopSpawnedProxy(child)
        assertTrue(child.killed)
        assertEquals("SIGTERM", child.lastSignal)
        assertEquals(0, child.exitCode)
    }

    private data class Harness(
        val controller: ProxyProcessController,
        val spawned: List<FakeSpawnedProxy>,
        val states: List<ProxyRunState>,
    )
}
