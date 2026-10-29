package com.tubawinui3.remote

import com.tubawinui3.remote.data.ConnectLink
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ConnectLinkTest {
    @Test
    fun parsesQrPayload() {
        val t = ConnectLink.parse("tubalink://192.168.1.5:18766?code=123456&name=MY%20PC")!!
        assertEquals("192.168.1.5", t.host)
        assertEquals(18766, t.port)
        assertEquals("123456", t.code)
        assertEquals("MY PC", t.name)
    }

    @Test
    fun parsesBareIpWithDefaultPort() {
        val t = ConnectLink.parse(" 10.0.0.2 ")!!
        assertEquals(18765, t.port)
        assertNull(t.code)
    }

    @Test
    fun parsesIpWithPort() {
        assertEquals(8000, ConnectLink.parse("10.0.0.2:8000")!!.port)
    }

    @Test
    fun rejectsGarbage() {
        assertNull(ConnectLink.parse(""))
        assertNull(ConnectLink.parse("10.0.0.2:99999"))
        assertNull(ConnectLink.parse("a b"))
        assertNull(ConnectLink.parse("https://x.com/path"))
    }
}
