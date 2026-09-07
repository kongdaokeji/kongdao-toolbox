package com.tubawinui3.installer.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException
import java.util.concurrent.TimeUnit

/**
 * 拉取图吧工具箱官方最新 release。
 *
 * 与主应用 UpdateService 同一思路：GitCode 国内镜像优先，GitHub 兜底。
 */
class ReleaseRepository(
    private val client: OkHttpClient = defaultClient(),
) {

    private val sources = listOf(
        // GitCode 镜像（国内快），仓库曾改过名，两个 owner 都试
        "https://api.gitcode.com/api/v5/repos/luolangaga/tubatool/releases/latest",
        "https://api.gitcode.com/api/v5/repos/gcw_uDDNaqJw/tubatool/releases/latest",
        // GitHub 官方
        "https://api.github.com/repos/luolangaga/tubatool/releases/latest",
    )

    /** 依次尝试各源，返回第一个可用的非 draft release。全部失败则抛 IOException。 */
    suspend fun fetchLatest(): FetchResult = withContext(Dispatchers.IO) {
        for (source in sources) {
            try {
                val dto = fetchOne(source) ?: continue
                if (dto.draft || dto.assets.isEmpty()) continue
                return@withContext FetchResult(dto, sourceName(source))
            } catch (_: Exception) {
                // 换下一个源
            }
        }
        throw IOException("所有下载源均不可用，请检查网络后重试")
    }

    private fun fetchOne(url: String): ReleaseDto? {
        val request = Request.Builder()
            .url(url)
            .header("User-Agent", "TubaWinUi3-Installer")
            .build()
        client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) return null
            val body = response.body?.string() ?: return null
            return json.decodeFromString(ReleaseDto.serializer(), body)
        }
    }

    private fun sourceName(url: String): String =
        if (url.contains("gitcode")) "GitCode 镜像" else "GitHub"

    companion object {
        val json = Json { ignoreUnknownKeys = true }

        fun defaultClient(): OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .build()
    }
}
