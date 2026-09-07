package com.tubawinui3.installer

import com.tubawinui3.installer.data.ReleaseDto
import com.tubawinui3.installer.data.ReleaseRepository
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ReleaseJsonTest {

    // 与 GitHub / GitCode(AtomGit) 官方响应结构一致的样例
    private val sampleJson = """
        {
          "tag_name": "v1.5.7",
          "draft": false,
          "prerelease": false,
          "name": "图吧工具箱WinUI3 v1.5.7",
          "assets": [
            {
              "name": "TubaWinUi3_Setup_1.5.7_x64.exe",
              "browser_download_url": "https://github.com/luolangaga/tubatool/releases/download/v1.5.7/TubaWinUi3_Setup_1.5.7_x64.exe",
              "size": 321456789,
              "content_type": "application/x-msdownload"
            },
            {
              "name": "TubaWinUi3_Portable_1.5.7_x64.zip",
              "browser_download_url": "https://github.com/luolangaga/tubatool/releases/download/v1.5.7/TubaWinUi3_Portable_1.5.7_x64.zip",
              "size": 298000000,
              "content_type": "application/zip"
            }
          ]
        }
    """.trimIndent()

    @Test
    fun `解析 release JSON 得到版本与资产`() {
        val json = Json { ignoreUnknownKeys = true }
        val dto = json.decodeFromString(ReleaseDto.serializer(), sampleJson)

        assertEquals("v1.5.7", dto.tagName)
        assertFalse(dto.draft)
        assertEquals(2, dto.assets.size)

        val setup = dto.assets.first { it.name.endsWith("_x64.exe") }
        assertEquals(321456789L, setup.size)
        assertTrue(setup.browserDownloadUrl.contains("download/v1.5.7/TubaWinUi3_Setup_1.5.7_x64.exe"))
    }

    @Test
    fun `draft release 应被识别`() {
        val json = Json { ignoreUnknownKeys = true }
        val draft = json.decodeFromString(ReleaseDto.serializer(), sampleJson.replace("\"draft\": false", "\"draft\": true"))
        assertTrue(draft.draft)
    }

    @Test
    fun `默认 JSON 配置忽略未知字段`() {
        val dto = ReleaseRepository.json.decodeFromString(ReleaseDto.serializer(), sampleJson)
        assertEquals("v1.5.7", dto.tagName)
        assertEquals("TubaWinUi3_Setup_1.5.7_x64.exe", dto.assets.first().name)
    }
}
