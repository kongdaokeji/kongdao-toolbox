package com.tubawinui3.installer.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.tubawinui3.installer.AppUiState
import com.tubawinui3.installer.data.AssetDto
import com.tubawinui3.installer.data.CpuArch
import com.tubawinui3.installer.data.DownloadState
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DownloadScreen(
    state: AppUiState,
    download: DownloadState,
    onArchSelect: (CpuArch) -> Unit,
    onRefresh: () -> Unit,
    onDownload: () -> Unit,
    onCancel: () -> Unit,
    onOpenGuide: () -> Unit,
) {
    val asset = state.release?.let { r ->
        com.tubawinui3.installer.data.AssetMatcher.setupAssetFor(r.assets, state.arch)
    }

    Scaffold(
        topBar = {
            TopAppBar(title = { Text("图吧工具箱安装助手") })
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
        ) {
            Text(
                "选择电脑架构，下载官方安装包，然后在电脑上安装。",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp, bottom = 12.dp),
            )

            // —— 架构选择 ——
            Text("电脑架构", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                CpuArch.entries.forEach { arch ->
                    ArchCard(
                        arch = arch,
                        selected = state.arch == arch,
                        onClick = { onArchSelect(arch) },
                        modifier = Modifier.weight(1f),
                    )
                }
            }
            Text(
                state.arch.note,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 4.dp, bottom = 12.dp),
            )

            // —— 版本信息 ——
            when {
                state.fetching -> LoadingCard("正在获取最新版本…")
                state.fetchError != null -> ErrorCard(state.fetchError!!, onRefresh)
                state.release != null -> ReleaseCard(state, asset)
            }

            Spacer(Modifier.height(12.dp))

            // —— 下载区 ——
            DownloadSection(
                download = download,
                asset = asset,
                fetching = state.fetching,
                onDownload = onDownload,
                onCancel = onCancel,
                onOpenGuide = onOpenGuide,
            )

            Spacer(Modifier.height(12.dp))
            TipCard()
            Spacer(Modifier.height(24.dp))
        }
    }
}

@Composable
private fun ArchCard(arch: CpuArch, selected: Boolean, onClick: () -> Unit, modifier: Modifier = Modifier) {
    val border = if (selected) CardDefaults.outlinedCardBorder() else null
    OutlinedCard(
        onClick = onClick,
        modifier = modifier,
        colors = CardDefaults.outlinedCardColors(
            containerColor = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
        ),
    ) {
        Column(
            modifier = Modifier.padding(vertical = 10.dp, horizontal = 8.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(
                arch.label,
                style = MaterialTheme.typography.labelLarge,
                fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
            )
            if (arch.recommended) {
                Text(
                    "推荐",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary,
                    fontWeight = FontWeight.Bold,
                )
            }
        }
    }
}

@Composable
private fun LoadingCard(text: String) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            CircularProgressIndicator(modifier = Modifier.height(20.dp).padding(0.dp))
            Text(text, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

@Composable
private fun ErrorCard(message: String, onRefresh: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.errorContainer,
        ),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Icon(Icons.Default.Warning, contentDescription = null, tint = MaterialTheme.colorScheme.error)
                Text("获取版本信息失败", fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onErrorContainer)
            }
            Text(
                message,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onErrorContainer,
                modifier = Modifier.padding(top = 4.dp),
            )
            TextButton(onClick = onRefresh, modifier = Modifier.align(Alignment.End)) {
                Icon(Icons.Default.Refresh, contentDescription = null, modifier = Modifier.height(16.dp))
                Spacer(Modifier.padding(2.dp))
                Text("重试")
            }
        }
    }
}

@Composable
private fun ReleaseCard(state: AppUiState, asset: AssetDto?) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("最新版本", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(
                    state.release?.tagName ?: "",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                )
                Surface(
                    color = if (state.source == "GitCode 镜像") Color(0xFF16A34A) else MaterialTheme.colorScheme.secondaryContainer,
                    shape = MaterialTheme.shapes.small,
                ) {
                    Text(
                        state.source ?: "",
                        style = MaterialTheme.typography.labelSmall,
                        color = if (state.source == "GitCode 镜像") Color.White else MaterialTheme.colorScheme.onSecondaryContainer,
                        modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                    )
                }
            }
            if (asset != null) {
                Spacer(Modifier.height(8.dp))
                Text("安装包", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Text(
                    asset.name,
                    style = MaterialTheme.typography.bodyMedium,
                    fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                )
                Text(
                    "大小：${formatSize(asset.size)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 2.dp),
                )
            } else {
                Spacer(Modifier.height(8.dp))
                Text(
                    "该架构暂无官方安装包",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}

@Composable
private fun DownloadSection(
    download: DownloadState,
    asset: AssetDto?,
    fetching: Boolean,
    onDownload: () -> Unit,
    onCancel: () -> Unit,
    onOpenGuide: () -> Unit,
) {
    when (download) {
        is DownloadState.Idle -> {
            Button(
                onClick = onDownload,
                enabled = asset != null && !fetching,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text("下载安装包")
            }
        }

        is DownloadState.Downloading -> {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text("正在下载…", fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(8.dp))
                    if (download.totalBytes > 0) {
                        val progress = download.bytesRead.toFloat() / download.totalBytes.toFloat()
                        LinearProgressIndicator(
                            progress = { progress },
                            modifier = Modifier.fillMaxWidth(),
                        )
                        Spacer(Modifier.height(6.dp))
                        Text(
                            String.format(
                                Locale.getDefault(),
                                "%.1f%% · %s / %s · %s",
                                progress * 100,
                                formatSize(download.bytesRead),
                                formatSize(download.totalBytes),
                                formatSpeed(download.speedBps),
                            ),
                            style = MaterialTheme.typography.bodySmall,
                        )
                    } else {
                        LinearProgressIndicator(modifier = Modifier.fillMaxWidth())
                    }
                    TextButton(onClick = onCancel, modifier = Modifier.align(Alignment.End)) {
                        Text("取消")
                    }
                }
            }
        }

        is DownloadState.Done -> {
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Icon(Icons.Default.CheckCircle, contentDescription = null, tint = MaterialTheme.colorScheme.primary)
                        Text("下载完成", fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onPrimaryContainer)
                    }
                    Text(
                        "已保存到手机「下载 / Download」文件夹：${download.fileName}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onPrimaryContainer,
                        modifier = Modifier.padding(top = 4.dp),
                    )
                    Spacer(Modifier.height(8.dp))
                    Button(onClick = onOpenGuide, modifier = Modifier.fillMaxWidth()) {
                        Text("查看安装引导")
                    }
                    TextButton(onClick = onDownload, modifier = Modifier.align(Alignment.End)) {
                        Text("重新下载")
                    }
                }
            }
        }

        is DownloadState.Failed -> {
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer),
            ) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text("下载失败：${download.message}", color = MaterialTheme.colorScheme.onErrorContainer)
                    Spacer(Modifier.height(4.dp))
                    Text(
                        "请检查网络后重试（GitHub 直连慢时可切换下载源，见设置提示）。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onErrorContainer,
                    )
                    Button(onClick = onDownload, modifier = Modifier.align(Alignment.End)) {
                        Text("重试")
                    }
                }
            }
        }
    }
}

@Composable
private fun TipCard() {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text("小贴士", fontWeight = FontWeight.Bold, style = MaterialTheme.typography.labelLarge)
            Text(
                "安装包保存到手机「下载 / Download」文件夹；连上电脑后按引导拷贝到桌面双击即可安装，安装包自带运行环境、无需联网。",
                style = MaterialTheme.typography.bodySmall,
                modifier = Modifier.padding(top = 4.dp),
            )
        }
    }
}

/** 格式化文件大小：B / KB / MB / GB */
fun formatSize(bytes: Long): String {
    if (bytes <= 0) return "未知"
    val kb = 1024.0
    val mb = kb * 1024
    val gb = mb * 1024
    return when {
        bytes >= gb -> String.format(Locale.getDefault(), "%.2f GB", bytes / gb)
        bytes >= mb -> String.format(Locale.getDefault(), "%.1f MB", bytes / mb)
        bytes >= kb -> String.format(Locale.getDefault(), "%.0f KB", bytes / kb)
        else -> "$bytes B"
    }
}

/** 格式化下载速度：B/s → MB/s */
fun formatSpeed(bps: Long): String {
    if (bps <= 0) return ""
    val kb = 1024.0
    val mb = kb * 1024
    return when {
        bps >= mb -> String.format(Locale.getDefault(), "%.1f MB/s", bps / mb)
        bps >= kb -> String.format(Locale.getDefault(), "%.0f KB/s", bps / kb)
        else -> "$bps B/s"
    }
}
