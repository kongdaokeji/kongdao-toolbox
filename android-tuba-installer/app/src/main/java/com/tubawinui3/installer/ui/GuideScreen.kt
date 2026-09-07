package com.tubawinui3.installer.ui

import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/**
 * 安装引导：连接手机 → 打开 Download 文件夹 → 拷贝到桌面 → 双击安装。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GuideScreen(
    fileName: String,
    onBack: () -> Unit,
) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("安装引导") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "返回")
                    }
                },
            )
        },
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
        ) {
            StepCard(
                step = 1,
                title = "用数据线连接手机和电脑",
                detail = "手机弹出「USB 用途」提示时，选择「传输文件 / MTP」；若没弹窗，在手机顶部通知栏点「正在通过 USB 充电」→ 选「传输文件」。",
            )
            StepCard(
                step = 2,
                title = "在电脑上打开手机存储",
                detail = "电脑上打开「此电脑（我的电脑）」→ 找到手机图标并双击 → 进入「内部共享存储」→ 打开「Download」（下载）文件夹。",
            )
            StepCard(
                step = 3,
                title = "把安装包拖到桌面",
                detail = "找到安装包文件，把它拖到电脑桌面。（文件较大，拷贝约需半分钟）",
                fileName = fileName,
            )
            StepCard(
                step = 4,
                title = "双击运行，完成安装",
                detail = "双击安装包：若弹出「用户账户控制」请点「是」，然后按向导点「下一步」直到完成。安装后桌面上即可打开图吧工具箱。",
            )

            Spacer(Modifier.height(12.dp))
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
            ) {
                Column(modifier = Modifier.padding(12.dp)) {
                    Text("温馨提示", fontWeight = FontWeight.Bold, style = MaterialTheme.typography.labelLarge)
                    Text(
                        "· 安装包自带 .NET 运行环境，无需联网即可安装\n" +
                            "· 支持 Windows 10 1809 及以上版本（Win10 / Win11）\n" +
                            "· 安装包约几百 MB，属正常现象\n" +
                            "· 若手机无法被电脑识别，检查数据线是否为「仅充电」线",
                        style = MaterialTheme.typography.bodySmall,
                        modifier = Modifier.padding(top = 4.dp),
                    )
                }
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}

@Composable
private fun StepCard(step: Int, title: String, detail: String, fileName: String? = null) {
    Card(modifier = Modifier
        .fillMaxWidth()
        .padding(bottom = 10.dp)) {
        Row(
            modifier = Modifier.padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                "$step",
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.Bold,
                color = MaterialTheme.colorScheme.primary,
            )
            Column {
                Text(title, fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleSmall)
                Text(
                    detail,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 4.dp),
                )
                if (fileName != null) {
                    FileNameRow(fileName)
                }
            }
        }
    }
}

@Composable
private fun FileNameRow(fileName: String) {
    val clipboard = LocalClipboardManager.current
    val copied = remember { mutableStateOf(false) }
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier.padding(top = 6.dp),
    ) {
        Card(
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
        ) {
            Text(
                fileName,
                style = MaterialTheme.typography.bodySmall,
                fontFamily = androidx.compose.ui.text.font.FontFamily.Monospace,
                modifier = Modifier.padding(horizontal = 8.dp, vertical = 4.dp),
            )
        }
        TextButton(onClick = {
            clipboard.setText(AnnotatedString(fileName))
            copied.value = true
        }) {
            Text(if (copied.value) "已复制" else "复制文件名")
        }
    }
}
