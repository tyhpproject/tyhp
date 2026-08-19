package com.tyhp.lang.debug

import java.net.InetSocketAddress
import java.net.Socket

const val PROXY_LISTEN_ADDRESS = "127.0.0.1"

/** True when `host:port` accepts a TCP connection within [timeoutMs]. */
fun probeTcpPort(host: String, port: Int, timeoutMs: Int): Boolean {
    if (port <= 0) {
        return false
    }
    Socket().use { socket ->
        return try {
            socket.connect(InetSocketAddress(host, port), timeoutMs)
            true
        } catch (_: Exception) {
            false
        }
    }
}

fun sleepMs(ms: Long) {
    Thread.sleep(ms)
}
