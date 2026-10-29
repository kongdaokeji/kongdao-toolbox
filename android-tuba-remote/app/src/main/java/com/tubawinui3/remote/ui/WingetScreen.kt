package com.tubawinui3.remote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import com.tubawinui3.remote.data.WingetItem
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WingetScreen(client: ApiClient, onBack: () -> Unit) {
    var query by remember { mutableStateOf("") }
    var searching by remember { mutableStateOf(false) }
    var results by remember { mutableStateOf<List<WingetItem>>(emptyList()) }
    var message by remember { mutableStateOf<String?>(null) }
    var installing by remember { mutableStateOf<String?>(null) }
    var log by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()

    fun install(item: WingetItem) {
        if (installing != null) return
        installing = item.id
        log = "正在创建安装任务…"
        scope.launch {
            try {
                val job = client.wingetInstall(item.id, item.name)
                while (true) {
                    val s = client.wingetJob(job)
                    log = s.output.takeLast(4000)
                    if (s.done) {
                        message = if (s.exitCode == 0) "${item.name} 安装完成" else "${item.name} 安装失败（退出码 ${s.exitCode}）"
                        break
                    }
                    delay(1500)
                }
            } catch (e: ApiException) {
                message = "安装失败：${e.message}"
            } finally {
                installing = null
            }
        }
    }

    Scaffold(topBar = {
        TopAppBar(title = { Text("应用安装（winget）") }, navigationIcon = { TextButton(onClick = onBack) { Text("返回") } })
    }) { pad ->
        Column(Modifier.fillMaxSize().padding(pad).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                OutlinedTextField(
                    value = query, onValueChange = { query = it }, label = { Text("软件名称，如 vscode") },
                    singleLine = true, modifier = Modifier.weight(1f),
                )
                Button(enabled = !searching && query.isNotBlank(), onClick = {
                    searching = true
                    message = null
                    scope.launch {
                        try {
                            results = client.wingetSearch(query.trim())
                            if (results.isEmpty()) message = "没有找到相关软件"
                        } catch (e: ApiException) {
                            message = "搜索失败：${e.message}"
                        } finally {
                            searching = false
                        }
                    }
                }) { Text("搜索") }
            }
            if (searching || installing != null) LinearProgressIndicator(Modifier.fillMaxWidth())
            message?.let { Text(it, color = MaterialTheme.colorScheme.primary) }
            LazyColumn(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                items(results, key = { it.id }) { item ->
                    Card(Modifier.fillMaxWidth()) {
                        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(item.name, style = MaterialTheme.typography.titleSmall)
                                Text(item.id + (item.version?.let { "  v$it" } ?: ""), style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            TextButton(enabled = installing == null, onClick = { install(item) }) {
                                Text(if (installing == item.id) "安装中" else "安装")
                            }
                        }
                    }
                }
            }
            if (log.isNotEmpty()) {
                Text(
                    log, modifier = Modifier.fillMaxWidth().padding(top = 4.dp).verticalScroll(rememberScrollState()).weight(0.6f),
                    style = MaterialTheme.typography.bodySmall.copy(fontFamily = FontFamily.Monospace),
                )
            }
        }
    }
}
