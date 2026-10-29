package com.tubawinui3.remote

import com.tubawinui3.remote.data.*
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test

class DiscoveryTest {
    private val reply = """{"type":"tubalink","version":1,"name":"电脑","port":23456,"host":"evil.invalid"}"""

    @Test fun sourceAddressAndActualPortAreUsed() {
        val target = DiscoveryProtocol.parse(reply, "192.168.1.7")!!
        assertEquals("192.168.1.7", target.host)
        assertEquals(23456, target.port)
        assertEquals("电脑", target.name)
        assertNull(target.code)
        assertEquals(18764, DiscoveryProtocol.PORT)
        assertEquals("TUBALINK_DISCOVER_V1", DiscoveryProtocol.REQUEST)
    }

    @Test fun rejectsMalformedOrUnsupportedPackets() {
        listOf("{}", "garbage", reply.replace("tubalink", "other"),
            reply.replace(""""version":1""", """"version":2"""),
            reply.replace("23456", "0"), reply.replace("23456", "65536"),
            reply.replace("23456", "1.5"), reply.replace("电脑", "")).forEach {
            assertNull(DiscoveryProtocol.parse(it, "192.168.1.7"))
        }
    }

    @Test fun sameNameNeverMovesSecrets() {
        val saved = SavedPc("192.168.1.2", name = "电脑", token = "token", pairingCode = "123456")
        assertNull(DiscoveryProtocol.savedAt(ConnectTarget("192.168.1.3", name = "电脑"), listOf(saved)))
        assertNull(DiscoveryProtocol.savedAt(ConnectTarget(saved.host, 18766, name = "电脑"), listOf(saved)))
        assertNull(DiscoveryProtocol.savedAt(ConnectTarget(saved.host, name = "另一台"), listOf(saved)))
        assertEquals(saved, DiscoveryProtocol.savedAt(ConnectTarget(saved.host, name = saved.name), listOf(saved)))
    }

    @Test fun validTokenDoesNotSendCode() = runBlocking {
        var pairs = 0
        val token = SavedReconnect.token(SavedPc("host", token = "valid", pairingCode = "123456"),
            { assertEquals("valid", it) }, { pairs++; "unexpected" })
        assertEquals("valid", token)
        assertEquals(0, pairs)
    }

    @Test fun unauthorizedTokenPairsOnceAndReturnsNewToken() = runBlocking {
        val calls = mutableListOf<String>()
        val token = SavedReconnect.token(SavedPc("host", token = "old", pairingCode = "123456"),
            { calls += "token"; throw ApiException("unauthorized", 401) },
            { assertEquals("123456", it); calls += "pair"; "new" })
        assertEquals(listOf("token", "pair"), calls)
        assertEquals("new", token)
    }

    @Test fun networkServerAndCancellationErrorsDoNotSendCode() = runBlocking {
        for (failure in listOf(ApiException("network"), ApiException("server", 500), CancellationException())) {
            var pairs = 0
            try {
                SavedReconnect.token(SavedPc("host", token = "old", pairingCode = "123456"),
                    { throw failure }, { pairs++; "bad" })
                fail("Must propagate failure")
            } catch (e: Exception) { assertSame(failure, e) }
            assertEquals(0, pairs)
        }
    }

    @Test fun legacyMissingCodeRequiresManualPairing() = runBlocking {
        val pc = PcStore.decode("""[{"host":"192.168.1.2","token":"old"}]""").single()
        assertNull(pc.pairingCode)
        assertNull(SavedReconnect.token(pc, { throw ApiException("unauthorized", 401) }, { error("No saved code") }))
    }

    @Test fun invalidSavedCodeIsNotRetried() = runBlocking {
        var pairs = 0
        try {
            SavedReconnect.token(SavedPc("host", token = "old", pairingCode = "123456"),
                { throw ApiException("unauthorized", 401) },
                { pairs++; throw ApiException("revoked", 403) })
            fail("Must propagate invalid code")
        } catch (e: ApiException) { assertEquals(403, e.code) }
        assertEquals(1, pairs)
    }

    @Test fun noCredentialsRequiresManualCodeWithoutRequests() = runBlocking {
        assertNull(SavedReconnect.token(SavedPc("host"), { error("No token") }, { error("No code") }))
    }

    @Test fun secretsAreNotInDiagnosticStrings() {
        val pc = SavedPc("host", token = "sensitive-token", pairingCode = "123456")
        assertFalse(pc.toString().contains("sensitive-token"))
        assertFalse(pc.toString().contains("123456"))
        assertFalse(ConnectTarget("host", code = "123456").toString().contains("123456"))
    }

    @Test fun codeSurvivesRoundTripMergeAndIsRemovedWithDevice() {
        val pc = SavedPc("host", pairingCode = "123456")
        assertEquals(pc, PcStore.decode(PcStore.encode(listOf(pc))).single())
        assertEquals("123456", PcStore.upsert(listOf(pc), SavedPc("host", token = "new")).single().pairingCode)
        assertTrue(PcStore.remove(listOf(pc), pc.id).isEmpty())
    }
}
