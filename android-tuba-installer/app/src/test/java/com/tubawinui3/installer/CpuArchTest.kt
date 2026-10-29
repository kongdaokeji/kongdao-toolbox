package com.tubawinui3.installer

import com.tubawinui3.installer.data.CpuArch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class CpuArchTest {

    @Test
    fun `默认架构是 x64`() {
        val state = AppUiState()
        assertEquals(CpuArch.X64, state.arch)
    }

    @Test
    fun `x64 标记为推荐且说明包含 64 位提示`() {
        assertTrue(CpuArch.X64.recommended)
        assertTrue(CpuArch.X64.note.contains("64 位"))
        assertFalse(CpuArch.X86.recommended)
    }

    @Test
    fun `架构后缀用于资产匹配`() {
        assertEquals("x64", CpuArch.X64.suffix)
        assertEquals("x86", CpuArch.X86.suffix)
        assertEquals("arm64", CpuArch.ARM64.suffix)
    }
}
