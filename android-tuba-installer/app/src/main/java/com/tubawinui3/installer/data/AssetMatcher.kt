package com.tubawinui3.installer.data

/** 目标电脑的 CPU 架构。 */
enum class CpuArch(val suffix: String, val label: String, val note: String, val recommended: Boolean) {
    X64("x64", "x64（64 位）", "绝大多数电脑都是 64 位，通常选这个", true),
    X86("x86", "x86（32 位）", "仅老旧 32 位电脑需要选择", false),
    ARM64("arm64", "ARM64", "骁龙 X、苹果 M 系列等 ARM 笔记本", false),
}

/**
 * 从 release 资产列表中匹配对应架构的官方安装包（如 TubaWinUi3_Setup_1.5.7_x64.exe）。
 * 名称匹配不区分大小写；找不到返回 null。
 */
object AssetMatcher {

    fun setupAssetFor(assets: List<AssetDto>, arch: CpuArch): AssetDto? {
        val suffix = "_${arch.suffix}.exe"
        return assets.firstOrNull {
            it.name.lowercase().startsWith("tubawinui3_setup_") &&
                it.name.lowercase().endsWith(suffix)
        }
    }
}
