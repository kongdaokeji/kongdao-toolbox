package com.tubawinui3.installer.data

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * GitHub Releases / GitCode(AtomGit) Releases 的响应结构（字段多余部分自动忽略）。
 */
@Serializable
data class ReleaseDto(
    @SerialName("tag_name") val tagName: String = "",
    val draft: Boolean = false,
    @SerialName("assets") val assets: List<AssetDto> = emptyList(),
)

@Serializable
data class AssetDto(
    val name: String = "",
    @SerialName("browser_download_url") val browserDownloadUrl: String = "",
    val size: Long = 0,
)

/** 一次成功的拉取结果：版本信息 + 来源标签 */
data class FetchResult(
    val release: ReleaseDto,
    val source: String,
)
