package com.tubawinui3.remote.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

/** Discovery data is untrusted, never an identity or authority to send saved credentials. */
@Serializable
private data class DiscoveryReply(val type: String, val version: Int, val name: String, val port: Int)

object DiscoveryProtocol {
    const val PORT = 18764
    const val REQUEST = "TUBALINK_DISCOVER_V1"
    private val json = Json { ignoreUnknownKeys = true }

    fun parse(payload: String, sourceIp: String): ConnectTarget? = runCatching {
        val r = json.decodeFromString<DiscoveryReply>(payload)
        if (r.type != "tubalink" || r.version != 1 || r.port !in 1..65535 ||
            r.name.isBlank() || r.name.length > 256 || sourceIp.isBlank()) return null
        ConnectTarget(sourceIp, r.port, name = r.name)
    }.getOrNull()

    // A matching display name is NOT proof of identity (including a unique name).
    fun savedAt(target: ConnectTarget, saved: List<SavedPc>): SavedPc? =
        saved.firstOrNull {
            it.host == target.host && it.port == target.port &&
                it.name.isNotBlank() && it.name == target.name
        }
}

object LanDiscovery {
    /** One bounded scan, cancel latency <= one 200 ms receive timeout; socket always closed. */
    suspend fun discover(timeoutMillis: Long = 3000): List<ConnectTarget> = withContext(Dispatchers.IO) {
        val found = linkedMapOf<String, ConnectTarget>()
        val deadline = System.nanoTime() + timeoutMillis.coerceIn(1, 10000) * 1_000_000
        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = 200
            val addresses = mutableSetOf(InetAddress.getByName("255.255.255.255"))
            NetworkInterface.getNetworkInterfaces()?.toList()?.filter { it.isUp && !it.isLoopback }
                ?.flatMap { it.interfaceAddresses }?.mapNotNull { it.broadcast }
                ?.let { addresses.addAll(it) }
            val bytes = DiscoveryProtocol.REQUEST.toByteArray(Charsets.UTF_8)
            var sent = false
            for (address in addresses) {
                currentCoroutineContext().ensureActive()
                try {
                    socket.send(DatagramPacket(bytes, bytes.size, address, DiscoveryProtocol.PORT))
                    sent = true
                } catch (_: java.io.IOException) { /* Other interfaces may still work. */ }
            }
            check(sent) { "无法发送局域网发现请求" }
            while (System.nanoTime() < deadline) {
                currentCoroutineContext().ensureActive()
                val packet = DatagramPacket(ByteArray(4096), 4096)
                try {
                    socket.receive(packet)
                    if (packet.address !is Inet4Address || packet.address.isAnyLocalAddress ||
                        packet.address.isMulticastAddress || packet.address.isLoopbackAddress) continue
                    val target = DiscoveryProtocol.parse(
                        String(packet.data, packet.offset, packet.length, Charsets.UTF_8),
                        packet.address.hostAddress ?: continue,
                    ) ?: continue
                    if (found.size < 64) found["${target.host}:${target.port}"] = target
                } catch (_: SocketTimeoutException) { /* Check cancellation and deadline. */ }
            }
        }
        currentCoroutineContext().ensureActive()
        found.values.toList()
    }
}

/** No retry with a code for timeouts, network errors, or non-authentication HTTP errors. */
object SavedReconnect {
    suspend fun token(pc: SavedPc, authenticate: suspend (String) -> Unit, pair: suspend (String) -> String): String? {
        pc.token?.let { token ->
            try {
                authenticate(token)
                return token
            } catch (e: ApiException) {
                if (e.code != 401) throw e
            }
        }
        return pc.pairingCode?.let { pair(it) }
    }
}
