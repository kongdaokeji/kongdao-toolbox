package com.tubawinui3.remote.ui

import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import com.tubawinui3.remote.PcStatus
import com.tubawinui3.remote.RemoteViewModel
import com.tubawinui3.remote.data.SavedPc
import kotlinx.coroutines.delay

/** 未连接时的首屏：「我的电脑」列表（点按连接、长按删除、可添加）。 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DeviceListScreen(vm: RemoteViewModel) {
    var showAdd by rememberSaveable { mutableStateOf(false) }
    var deleteTarget by remember { mutableStateOf<SavedPc?>(null) }
    val pairTarget = vm.pairTarget

    DisposableEffect(Unit) {
        vm.startDiscovery()
        onDispose { vm.cancelDiscovery() }
    }

    // 进入列表后持续刷新在线状态（4 秒一轮，各电脑并发探测）
    LaunchedEffect(Unit) {
        while (true) {
            vm.refreshStatuses()
            delay(4000)
        }
    }

    // 列表点按需要配对码（或配对失效）时，自动弹出配对面板
    LaunchedEffect(pairTarget) {
        if (pairTarget != null) showAdd = true
    }

    Scaffold(topBar = { TopAppBar(title = { Text("我的电脑") }) }) { pad ->
        Box(Modifier.fillMaxSize().padding(pad)) {
            LazyColumn(
                Modifier.fillMaxSize(),
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                item {
                    Text("局域网发现", style = MaterialTheme.typography.titleMedium)
                    Text("同一局域网内自动发现电脑。成功配对后保存地址与连接码，下次优先用令牌自动连接；令牌失效才尝试保存的码。仅在可信局域网使用。",
                        style = MaterialTheme.typography.bodySmall)
                    Text("发现不可用时，点添加电脑输入地址＋连接码。地址变化或同名电脑不能证明身份，需手动输入码。",
                        style = MaterialTheme.typography.bodySmall)
                    TextButton(onClick = { if (vm.discovering) vm.cancelDiscovery() else vm.startDiscovery() }) {
                        Text(if (vm.discovering) "停止发现" else "重新发现")
                    }
                    vm.discoveryMessage?.let { Text(it, style = MaterialTheme.typography.bodySmall) }
                }
                items(vm.discovered, key = { "discovered:${it.host}:${it.port}" }) { target ->
                    OutlinedButton(onClick = { vm.selectDiscovered(target) }, enabled = !vm.connecting,
                        modifier = Modifier.fillMaxWidth()) {
                        Column {
                            Text(target.name.orEmpty())
                            Text("${target.host}:${target.port} · 选择连接")
                        }
                    }
                }
                if (vm.devices.isNotEmpty()) item { Text("已保存的电脑", style = MaterialTheme.typography.titleMedium) }
                if (vm.devices.isEmpty()) {
                    item {
                        Text(
                            "还没有添加电脑。\n\n在电脑上打开 空岛图吧工具箱 → 内置工具 → 连接手机，开启服务后点下面的「添加电脑」扫码或输入地址。",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                items(vm.devices, key = { it.id }) { pc ->
                    DeviceCard(
                        pc = pc,
                        status = vm.statuses[pc.id] ?: PcStatus.Unknown,
                        onClick = { vm.requestConnect(pc) },
                        onLongClick = { deleteTarget = pc },
                    )
                }
                item {
                    OutlinedButton(onClick = { showAdd = true }, modifier = Modifier.fillMaxWidth()) {
                        Text("＋ 添加电脑")
                    }
                }
                if (vm.devices.isNotEmpty()) {
                    item {
                        Text(
                            "提示：长按电脑卡片可删除；点按即可连接（已配对的不用再输配对码）。",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                }
                vm.error?.let { message ->
                    item { Text(message, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall) }
                }
            }
            if (vm.connecting) {
                CircularProgressIndicator(Modifier.align(Alignment.Center))
            }
        }
    }

    if (showAdd) {
        AddPcSheet(vm, initial = pairTarget, onDismiss = {
            showAdd = false
            vm.clearPairTarget()
        })
    }

    deleteTarget?.let { pc ->
        AlertDialog(
            onDismissRequest = { deleteTarget = null },
            title = { Text("删除这台电脑？") },
            text = { Text("将移除「${pc.name.ifEmpty { pc.host }}」及其地址、配对码和令牌，需要时可重新添加。") },
            confirmButton = {
                TextButton(onClick = {
                    vm.removeDevice(pc.id)
                    deleteTarget = null
                }) { Text("删除") }
            },
            dismissButton = { TextButton(onClick = { deleteTarget = null }) { Text("取消") } },
        )
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun DeviceCard(pc: SavedPc, status: PcStatus, onClick: () -> Unit, onLongClick: () -> Unit) {
    Card(Modifier.fillMaxWidth().combinedClickable(onClick = onClick, onLongClick = onLongClick)) {
        Row(
            Modifier.padding(16.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Box(Modifier.size(10.dp).clip(CircleShape).background(StatusColor(status)))
            Column(Modifier.weight(1f)) {
                Text(pc.name.ifEmpty { pc.host }, style = MaterialTheme.typography.titleMedium)
                Text(
                    "${pc.host}:${pc.port}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(StatusLabel(status), style = MaterialTheme.typography.labelMedium, color = StatusColor(status))
                Text(
                    if (pc.token != null) "已配对" else "需配对码",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun StatusColor(status: PcStatus): Color = when (status) {
    PcStatus.Online -> Color(0xFF107C10)
    PcStatus.Offline -> MaterialTheme.colorScheme.outline
    PcStatus.Checking -> MaterialTheme.colorScheme.primary
    PcStatus.Unknown -> MaterialTheme.colorScheme.outlineVariant
}

private fun StatusLabel(status: PcStatus): String = when (status) {
    PcStatus.Online -> "在线"
    PcStatus.Offline -> "离线"
    PcStatus.Checking -> "检测中…"
    PcStatus.Unknown -> "未知"
}
