package com.tubawinui3.remote

import com.tubawinui3.remote.data.PcStore
import com.tubawinui3.remote.data.SavedPc
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PcStoreTest {

    @Test
    fun encodeDecode_roundTrip() {
        val list = listOf(
            SavedPc("192.168.1.5", 18765, "台式机", "tok1", 111),
            SavedPc("10.0.0.2", 18766, "笔记本", null, 222),
        )
        val decoded = PcStore.decode(PcStore.encode(list))
        assertEquals(list, decoded)
    }

    @Test
    fun decode_garbage_returnsEmpty() {
        assertEquals(emptyList<SavedPc>(), PcStore.decode("not json"))
        assertEquals(emptyList<SavedPc>(), PcStore.decode(null))
        assertEquals(emptyList<SavedPc>(), PcStore.decode(""))
    }

    @Test
    fun upsert_addsNewAtFront() {
        val a = SavedPc("192.168.1.5", 18765, "A")
        val b = SavedPc("192.168.1.6", 18765, "B")
        val list = PcStore.upsert(PcStore.upsert(emptyList(), a), b)
        assertEquals(listOf("192.168.1.6", "192.168.1.5"), list.map { it.host })
    }

    @Test
    fun upsert_keepsExistingTokenAndName() {
        val old = SavedPc("192.168.1.5", 18765, "旧名字", "tok", 100)
        val incoming = SavedPc("192.168.1.5", 18765, "", null, 200)
        val merged = PcStore.upsert(listOf(old), incoming).single()
        assertEquals("旧名字", merged.name)
        assertEquals("tok", merged.token)
        assertEquals(200, merged.lastConnectedAt)
    }

    @Test
    fun upsert_sameHostDifferentPort_areDifferentDevices() {
        val a = SavedPc("192.168.1.5", 18765, "A")
        val b = SavedPc("192.168.1.5", 18766, "B")
        assertEquals(2, PcStore.upsert(listOf(a), b).size)
    }

    @Test
    fun remove_dropsMatchingId() {
        val a = SavedPc("192.168.1.5", 18765, "A")
        val b = SavedPc("192.168.1.6", 18765, "B")
        val after = PcStore.remove(listOf(a, b), b.id)
        assertEquals(listOf(a), after)
    }

    @Test
    fun touch_updatesTimestampOnly() {
        val a = SavedPc("192.168.1.5", 18765, "A", "tok", 1)
        val b = SavedPc("192.168.1.6", 18765, "B", null, 2)
        val after = PcStore.touch(listOf(a, b), a.id, 999)
        assertEquals(999, after[0].lastConnectedAt)
        assertEquals(2, after[1].lastConnectedAt)
        assertEquals("tok", after[0].token)
    }

    @Test
    fun migrate_legacySingleDevice() {
        val list = PcStore.migrate("10.0.0.9", 18765, "tok")
        assertEquals(1, list.size)
        assertEquals("10.0.0.9", list[0].host)
        assertEquals(18765, list[0].port)
        assertEquals("tok", list[0].token)
        assertTrue(PcStore.migrate(null, 18765, null).isEmpty())
    }

    @Test
    fun id_isHostColonPort() {
        assertEquals("10.0.0.9:18765", SavedPc("10.0.0.9", 18765).id)
    }
}
