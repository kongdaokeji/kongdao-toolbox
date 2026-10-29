package com.tubawinui3.installer.data

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.File
import java.io.IOException
import java.io.OutputStream

sealed interface DownloadState {
    data object Idle : DownloadState
    data class Downloading(val bytesRead: Long, val totalBytes: Long, val speedBps: Long) : DownloadState
    data class Done(val fileName: String, val bytes: Long) : DownloadState
    data class Failed(val message: String) : DownloadState
}

/**
 * 把安装包下载到手机的公共「下载 / Download」文件夹：
 * - Android 10+：MediaStore，无需任何存储权限
 * - Android 9 及以下：传统外部存储，需要 WRITE_EXTERNAL_STORAGE
 */
class DownloadManager(
    private val context: Context,
    private val client: OkHttpClient = ReleaseRepository.defaultClient(),
) {

    private val _state = MutableStateFlow<DownloadState>(DownloadState.Idle)
    val state: StateFlow<DownloadState> = _state.asStateFlow()

    @Volatile
    private var cancelled = false

    suspend fun download(url: String, fileName: String) {
        if (_state.value is DownloadState.Downloading) return
        cancelled = false
        _state.value = DownloadState.Idle
        try {
            val bytes = withContext(Dispatchers.IO) {
                val request = Request.Builder().url(url)
                    .header("User-Agent", "TubaWinUi3-Installer")
                    .build()
                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) {
                        throw IOException("下载失败：HTTP ${response.code}")
                    }
                    val body = response.body ?: throw IOException("下载失败：响应为空")
                    val total = body.contentLength()
                    deleteExisting(fileName)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                        saveViaMediaStore(fileName, body.byteStream(), total)
                    } else {
                        saveLegacy(fileName, body.byteStream(), total)
                    }
                }
            }
            if (cancelled) {
                _state.value = DownloadState.Idle
            } else {
                _state.value = DownloadState.Done(fileName, bytes)
            }
        } catch (e: Exception) {
            if (!cancelled) {
                _state.value = DownloadState.Failed(e.message ?: "下载失败")
            } else {
                _state.value = DownloadState.Idle
            }
        }
    }

    /** 取消下载（已完成的文件不受影响）。 */
    fun cancel() {
        cancelled = true
        _state.value = DownloadState.Idle
    }

    private fun saveViaMediaStore(fileName: String, input: java.io.InputStream, total: Long): Long {
        val resolver = context.contentResolver
        val values = ContentValues().apply {
            put(MediaStore.Downloads.DISPLAY_NAME, fileName)
            put(MediaStore.Downloads.MIME_TYPE, "application/octet-stream")
            put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS)
            put(MediaStore.Downloads.IS_PENDING, 1)
        }
        val uri: Uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
            ?: throw IOException("无法在下载目录创建文件")
        try {
            val written = resolver.openOutputStream(uri)?.use { out ->
                copyStream(input, out, total)
            } ?: throw IOException("无法写入文件")
            values.clear()
            values.put(MediaStore.Downloads.IS_PENDING, 0)
            resolver.update(uri, values, null, null)
            return written
        } catch (e: Exception) {
            resolver.delete(uri, null, null)
            throw e
        }
    }

    private fun saveLegacy(fileName: String, input: java.io.InputStream, total: Long): Long {
        val dir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        if (!dir.exists() && !dir.mkdirs()) throw IOException("无法访问下载目录")
        val file = File(dir, fileName)
        file.outputStream().use { out -> return copyStream(input, out, total) }
    }

    /** 删除 Download 目录下同名旧文件（Android 10+ 按 MediaStore 查询删除）。 */
    private fun deleteExisting(fileName: String) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val resolver = context.contentResolver
            val uri = MediaStore.Downloads.EXTERNAL_CONTENT_URI
            val projection = arrayOf(MediaStore.Downloads._ID)
            resolver.query(
                uri,
                projection,
                "${MediaStore.Downloads.DISPLAY_NAME}=?",
                arrayOf(fileName),
                null,
            )?.use { cursor ->
                while (cursor.moveToNext()) {
                    val id = cursor.getLong(0)
                    resolver.delete(android.content.ContentUris.withAppendedId(uri, id), null, null)
                }
            }
        } else {
            File(
                Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                fileName,
            ).delete()
        }
    }

    /** 流式拷贝并上报进度（每 200ms 一次，含瞬时速度）。 */
    private fun copyStream(input: java.io.InputStream, output: OutputStream, total: Long): Long {
        val buffer = ByteArray(128 * 1024)
        var bytes = 0L
        var lastTime = System.currentTimeMillis()
        var lastBytes = 0L
        while (true) {
            if (cancelled) throw IOException("已取消")
            val read = input.read(buffer)
            if (read < 0) break
            output.write(buffer, 0, read)
            bytes += read
            val now = System.currentTimeMillis()
            if (now - lastTime >= 200) {
                val speed = (bytes - lastBytes) * 1000 / (now - lastTime)
                lastTime = now
                lastBytes = bytes
                _state.value = DownloadState.Downloading(bytes, total, speed)
            }
        }
        output.flush()
        _state.value = DownloadState.Downloading(bytes, total, 0)
        return bytes
    }
}
