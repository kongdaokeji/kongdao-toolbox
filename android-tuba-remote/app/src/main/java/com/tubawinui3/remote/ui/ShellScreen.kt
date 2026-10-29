package com.tubawinui3.remote.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
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
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import com.tubawinui3.remote.data.ApiClient
import com.tubawinui3.remote.data.ApiException
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ShellScreen(client: ApiClient, onBack: () -> Unit) {
    var command by rememberSaveable { mutableStateOf("") }
    var output by rememberSaveable { mutableStateOf("") }
    var running by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()

    Scaffold(topBar = {
        TopAppBar(title = { Text("PowerShell 终端") }, navigationIcon = { TextButton(onClick = onBack) { Text("返回") } })
    }) { pad ->
        Column(Modifier.fillMaxSize().padding(pad).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = command, onValueChange = { command = it },
                label = { Text("PowerShell 命令") }, modifier = Modifier.fillMaxWidth(), minLines = 2, maxLines = 6,
                textStyle = MaterialTheme.typography.bodyMedium.copy(fontFamily = FontFamily.Monospace),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(enabled = !running && command.isNotBlank(), onClick = {
                    running = true
                    output = "运行中…"
                    scope.launch {
                        output = try {
                            val r = client.exec(command)
                            r.output.ifBlank { "（无输出）" } + "\n[退出码 ${r.exitCode}]"
                        } catch (e: ApiException) {
                            "错误：${e.message}"
                        } finally {
                            running = false
                        }
                    }
                }) { Text("执行") }
                TextButton(onClick = { output = "" }) { Text("清空输出") }
            }
            Text(
                "命令以管理员权限在电脑上执行，请谨慎操作。",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error,
            )
            Text(
                output,
                modifier = Modifier.fillMaxWidth().weight(1f).verticalScroll(rememberScrollState()),
                style = MaterialTheme.typography.bodySmall.copy(fontFamily = FontFamily.Monospace),
            )
        }
    }
}
