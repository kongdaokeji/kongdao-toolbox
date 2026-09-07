package com.tubawinui3.installer

import com.tubawinui3.installer.data.AssetDto
import com.tubawinui3.installer.data.AssetMatcher
import com.tubawinui3.installer.data.CpuArch
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AssetMatcherTest {

    private val assets = listOf(
        AssetDto(
            name = "TubaWinUi3_Setup_1.5.7_x64.exe",
            browserDownloadUrl = "https://github.com/luolangaga/tubatool/releases/download/v1.5.7/TubaWinUi3_Setup_1.5.7_x64.exe",
            size = 321_000_000,
        ),
        AssetDto(name = "TubaWinUi3_Setup_1.5.7_x86.exe", browserDownloadUrl = "https://x", size = 1),
        AssetDto(name = "TubaWinUi3_Setup_1.5.7_arm64.exe", browserDownloadUrl = "https://x", size = 1),
        AssetDto(name = "TubaWinUi3_Portable_1.5.7_x64.zip", browserDownloadUrl = "https://x", size = 1),
        AssetDto(name = "Tools.zip", browserDownloadUrl = "https://x", size = 1),
    )

    @Test
    fun `x64 匹配到官方安装包而非便携包`() {
        val hit = AssetMatcher.setupAssetFor(assets, CpuArch.X64)
        assertEquals("TubaWinUi3_Setup_1.5.7_x64.exe", hit?.name)
    }

    @Test
    fun `x86 与 arm64 各自匹配对应架构`() {
        assertEquals("TubaWinUi3_Setup_1.5.7_x86.exe", AssetMatcher.setupAssetFor(assets, CpuArch.X86)?.name)
        assertEquals("TubaWinUi3_Setup_1.5.7_arm64.exe", AssetMatcher.setupAssetFor(assets, CpuArch.ARM64)?.name)
    }

    @Test
    fun `名称匹配不区分大小写`() {
        val upper = listOf(AssetDto(name = "TUBAWINUI3_SETUP_2.0.0_X64.EXE", browserDownloadUrl = "https://x", size = 1))
        assertEquals("TUBAWINUI3_SETUP_2.0.0_X64.EXE", AssetMatcher.setupAssetFor(upper, CpuArch.X64)?.name)
    }

    @Test
    fun `没有对应架构安装包时返回 null`() {
        val noX86 = assets.filterNot { it.name.contains("_x86.") }
        assertNull(AssetMatcher.setupAssetFor(noX86, CpuArch.X86))
    }

    @Test
    fun `空资产列表返回 null`() {
        assertNull(AssetMatcher.setupAssetFor(emptyList(), CpuArch.X64))
    }
}
