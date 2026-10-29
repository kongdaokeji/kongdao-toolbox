package com.tubawinui3.remote.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.unit.dp
import com.tubawinui3.remote.RemoteViewModel

private enum class Tool(val title: String, val subtitle: String) {
    MONITOR("实时硬件监控", "自选监控项，绘制实时曲线（含 FPS）"),
    SHELL("PowerShell 终端", "在电脑上执行命令并查看输出"),
    WINGET("应用安装", "搜索并通过 winget 在电脑上安装软件"),
    CHAT("传输助手", "与电脑互发文字、图片和文件"),
}

@Composable
fun RemoteApp(vm: RemoteViewModel) {
    var tool by rememberSaveable { mutableStateOf<String?>(null) }
    val client = vm.client

    if (client == null) {
        // 未连接：显示「我的电脑」列表（点按连接，可添加/删除，展示在线状态）
        DeviceListScreen(vm)
        return
    }

    val current = tool?.let { runCatching { Tool.valueOf(it) }.getOrNull() }
    if (current != null) {
        BackHandler { tool = null }
        when (current) {
            Tool.MONITOR -> MonitorScreen(client, onBack = { tool = null })
            Tool.SHELL -> ShellScreen(client, onBack = { tool = null })
            Tool.WINGET -> WingetScreen(client, onBack = { tool = null })
            Tool.CHAT -> ChatScreen(vm, client, onBack = { tool = null })
        }
        return
    }
    Dashboard(vm, onOpen = { tool = it.name })
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun Dashboard(vm: RemoteViewModel, onOpen: (Tool) -> Unit) {
    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text(vm.hostName.ifEmpty { "已连接" }) },
                actions = { TextButton(onClick = { vm.disconnect() }) { Text("断开") } },
            )
        },
    ) { pad ->
        LazyColumn(
            Modifier.fillMaxSize().padding(pad),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item { HardwareCard(vm) }
            item { ScreenshotCard(vm) }
            item { Text("小工具", style = MaterialTheme.typography.titleMedium) }
            Tool.entries.forEach { t ->
                item {
                    Card(Modifier.fillMaxWidth().clickable { onOpen(t) }) {
                        Column(Modifier.padding(16.dp)) {
                            Text(t.title, style = MaterialTheme.typography.titleMedium)
                            Text(t.subtitle, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun HardwareCard(vm: RemoteViewModel) {
    val sections = vm.hardware
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
                Text("电脑配置", style = MaterialTheme.typography.titleMedium)
                TextButton(onClick = { vm.loadHardware() }) { Text("刷新") }
            }
            if (sections == null) {
                if (vm.hardwareError != null) Text(vm.hardwareError!!, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
                else LinearProgressIndicator(Modifier.fillMaxWidth())
                return@Column
            }
            sections.filter { it.items.isNotEmpty() }.forEach { sec ->
                Text(sec.title, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.primary)
                sec.items.forEach { (k, v) ->
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Text(k, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.weight(0.35f))
                        Text(v, style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(0.65f))
                    }
                }
            }
        }
    }
}

@Composable
private fun ScreenshotCard(vm: RemoteViewModel) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.SpaceBetween) {
                Text("电脑屏幕", style = MaterialTheme.typography.titleMedium)
                TextButton(onClick = { vm.refreshScreenshot() }, enabled = !vm.screenshotLoading) {
                    Text(if (vm.screenshotLoading) "刷新中…" else "刷新截图")
                }
            }
            val img = vm.screenshot
            if (img != null) {
                Image(img, contentDescription = "电脑屏幕截图", modifier = Modifier.fillMaxWidth(), contentScale = ContentScale.FillWidth)
            } else if (vm.screenshotLoading) {
                LinearProgressIndicator(Modifier.fillMaxWidth())
            }
            vm.screenshotError?.let { Text(it, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
        }
    }
}
