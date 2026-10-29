package com.tubawinui3.remote.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.doubleOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.longOrNull
import kotlinx.serialization.json.put
import okhttp3.HttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okio.BufferedSink
import okio.source
import java.io.IOException
import java.io.InputStream
import java.util.concurrent.TimeUnit

class ApiException(message: String, val code: Int = 0) : IOException(message)

data class MonitorData(
    val cpuName: String = "", val cpuLoad: Double = -1.0, val cpuTemp: Double = -1.0,
    val cpuClock: Double = -1.0, val cpuPower: Double = -1.0,
    val gpuName: String = "", val gpuLoad: Double = -1.0, val gpuTemp: Double = -1.0,
    val gpuClock: Double = -1.0, val gpuPower: Double = -1.0,
    val gpuVramUsedGB: Double = -1.0, val gpuVramLoad: Double = -1.0,
    val memLoad: Double = -1.0, val memUsedGB: Double = -1.0, val memTotalGB: Double = -1.0,
    val diskReadMBs: Double = -1.0, val diskWriteMBs: Double = -1.0, val diskTemp: Double = -1.0,
    val netUpMBs: Double = -1.0, val netDownMBs: Double = -1.0,
    val batPercent: Double = -1.0, val batCharging: Boolean = false,
    val fps: Double = -1.0, val fpsLow1: Double = -1.0, val frameTimeMs: Double = -1.0,
)

data class HardwareSection(val title: String, val items: List<Pair<String, String>>)

data class ChatMessage(
    val seq: Long, val id: String, val from: String, val type: String,
    val text: String, val fileName: String, val size: Long, val time: Long,
)

data class WingetItem(val id: String, val name: String, val version: String?, val publisher: String?)

data class ExecResult(val exitCode: Int, val output: String)

data class WingetJobState(val done: Boolean, val exitCode: Int, val output: String)

/** 电脑端「连接手机」服务的 HTTP 客户端。所有方法在 IO 线程执行，失败抛出 [ApiException]。 */
class ApiClient(val host: String, val port: Int, var token: String? = null) {
    private val json = Json { ignoreUnknownKeys = true }
    private val http = OkHttpClient.Builder()
        // Never forward tokens or pairing requests to a redirect destination.
        .followRedirects(false)
        .followSslRedirects(false)
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .build()
    private val longHttp = http.newBuilder().readTimeout(650, TimeUnit.SECONDS).build()
    private val transferHttp = http.newBuilder().readTimeout(0, TimeUnit.SECONDS).writeTimeout(0, TimeUnit.SECONDS).build()
    /** 状态探测（在线/离线）：短超时，避免列表刷新被不在线的电脑拖慢。 */
    private val statusHttp = http.newBuilder()
        .connectTimeout(2500, TimeUnit.MILLISECONDS)
        .readTimeout(4000, TimeUnit.MILLISECONDS)
        .build()
    private val jsonType = "application/json; charset=utf-8".toMediaType()

    private fun url(path: String, vararg q: Pair<String, String>): HttpUrl {
        val b = HttpUrl.Builder().scheme("http").host(host).port(port).encodedPath(path)
        q.forEach { b.addQueryParameter(it.first, it.second) }
        return b.build()
    }

    private fun builder(u: HttpUrl) = Request.Builder().url(u).apply { token?.let { header("X-Token", it) } }

    private fun <T> call(client: OkHttpClient, req: Request, parse: (okhttp3.Response) -> T): T =
        try {
            client.newCall(req).execute().use { r ->
                if (!r.isSuccessful) {
                    val msg = runCatching { json.parseToJsonElement(r.body?.string().orEmpty()).jsonObject["error"]?.jsonPrimitive?.contentOrNull }.getOrNull()
                    throw ApiException(msg ?: "请求失败 (${r.code})", r.code)
                }
                parse(r)
            }
        } catch (e: ApiException) {
            throw e
        } catch (e: IOException) {
            throw ApiException("无法连接电脑：${e.message ?: "网络错误"}")
        }

    private fun obj(r: okhttp3.Response): JsonObject = json.parseToJsonElement(r.body!!.string()).jsonObject

    private suspend fun getObj(path: String, vararg q: Pair<String, String>): JsonObject =
        withContext(Dispatchers.IO) { call(http, builder(url(path, *q)).get().build(), { obj(it) }) }

    private suspend fun postObj(client: OkHttpClient, path: String, body: JsonObject): JsonObject =
        withContext(Dispatchers.IO) {
            call(client, builder(url(path)).post(body.toString().toRequestBody(jsonType)).build(), { obj(it) })
        }

    suspend fun ping(): String =
        withContext(Dispatchers.IO) { call(statusHttp, builder(url("/api/ping")).get().build(), { obj(it) }) }["host"]
            ?.jsonPrimitive?.contentOrNull ?: ""

    /** 用配对码换取令牌；成功后 [token] 即被设置。返回电脑名。
     *  首次连接需要在电脑上弹窗授权，等待时间可能较长（最长约 1 分钟），因此用长超时客户端。 */
    suspend fun pair(code: String, deviceName: String): String {
        val o = postObj(longHttp, "/api/pair", buildJsonObject { put("code", code); put("deviceName", deviceName) })
        token = o["token"]?.jsonPrimitive?.contentOrNull?.takeIf { it.isNotBlank() } ?: throw ApiException("配对失败")
        return o["host"]?.jsonPrimitive?.contentOrNull ?: host
    }

    suspend fun info(): JsonObject = getObj("/api/info")

    suspend fun hardware(): List<HardwareSection> =
        (getObj("/api/hardware")["sections"] as? JsonArray).orEmpty().map { sec ->
            val o = sec.jsonObject
            HardwareSection(
                o["title"]?.jsonPrimitive?.contentOrNull ?: "",
                (o["items"] as? JsonArray).orEmpty().map {
                    val i = it.jsonObject
                    (i["label"]?.jsonPrimitive?.contentOrNull ?: "") to (i["value"]?.jsonPrimitive?.contentOrNull ?: "")
                },
            )
        }

    suspend fun monitor(withFps: Boolean = false): MonitorData {
        val o = if (withFps) getObj("/api/monitor", "fps" to "1") else getObj("/api/monitor")
        fun d(k: String) = o[k]?.jsonPrimitive?.doubleOrNull ?: -1.0
        fun s(k: String) = o[k]?.jsonPrimitive?.contentOrNull ?: ""
        return MonitorData(
            s("cpuName"), d("cpuLoad"), d("cpuTemp"), d("cpuClock"), d("cpuPower"),
            s("gpuName"), d("gpuLoad"), d("gpuTemp"), d("gpuClock"), d("gpuPower"),
            d("gpuVramUsedGB"), d("gpuVramLoad"),
            d("memLoad"), d("memUsedGB"), d("memTotalGB"),
            d("diskReadMBs"), d("diskWriteMBs"), d("diskTemp"),
            d("netUpMBs"), d("netDownMBs"),
            d("batPercent"), o["batCharging"]?.jsonPrimitive?.contentOrNull == "true",
            d("fps"), d("fpsLow1"), d("frameTimeMs"),
        )
    }

    suspend fun screenshot(width: Int = 1280): ByteArray = withContext(Dispatchers.IO) {
        call(transferHttp, builder(url("/api/screenshot", "width" to width.toString())).get().build()) { it.body!!.bytes() }
    }

    suspend fun exec(command: String, timeoutSec: Int = 60): ExecResult {
        val o = postObj(longHttp, "/api/exec", buildJsonObject { put("command", command); put("timeoutSec", timeoutSec) })
        return ExecResult(o["exitCode"]?.jsonPrimitive?.longOrNull?.toInt() ?: -1, o["output"]?.jsonPrimitive?.contentOrNull ?: "")
    }

    suspend fun wingetSearch(query: String): List<WingetItem> {
        val o = getObj("/api/winget/search", "q" to query)
        o["error"]?.jsonPrimitive?.contentOrNull?.let { throw ApiException(it) }
        return (o["results"] as? JsonArray).orEmpty().map {
            val i = it.jsonObject
            WingetItem(
                i["id"]?.jsonPrimitive?.contentOrNull ?: "",
                i["name"]?.jsonPrimitive?.contentOrNull ?: "",
                i["version"]?.jsonPrimitive?.contentOrNull,
                i["publisher"]?.jsonPrimitive?.contentOrNull,
            )
        }
    }

    suspend fun wingetInstall(id: String, name: String? = null): String =
        postObj(http, "/api/winget/install", buildJsonObject {
            put("id", id)
            if (!name.isNullOrBlank()) put("name", name)
        })["jobId"]?.jsonPrimitive?.contentOrNull
            ?: throw ApiException("安装任务创建失败")

    suspend fun wingetJob(jobId: String): WingetJobState {
        val o = getObj("/api/winget/job", "id" to jobId)
        return WingetJobState(
            o["done"]?.jsonPrimitive?.contentOrNull == "true",
            o["exitCode"]?.jsonPrimitive?.longOrNull?.toInt() ?: -1,
            o["output"]?.jsonPrimitive?.contentOrNull ?: "",
        )
    }

    private fun parseMessage(o: JsonObject) = ChatMessage(
        o["seq"]?.jsonPrimitive?.longOrNull ?: 0, o["id"]?.jsonPrimitive?.contentOrNull ?: "",
        o["from"]?.jsonPrimitive?.contentOrNull ?: "", o["type"]?.jsonPrimitive?.contentOrNull ?: "text",
        o["text"]?.jsonPrimitive?.contentOrNull ?: "", o["fileName"]?.jsonPrimitive?.contentOrNull ?: "",
        o["size"]?.jsonPrimitive?.longOrNull ?: 0, o["time"]?.jsonPrimitive?.longOrNull ?: 0,
    )

    suspend fun chatList(after: Long): List<ChatMessage> =
        (getObj("/api/chat", "after" to after.toString())["messages"] as? JsonArray).orEmpty().map { parseMessage(it.jsonObject) }

    suspend fun chatSendText(text: String): ChatMessage =
        parseMessage(postObj(http, "/api/chat/text", buildJsonObject { put("text", text) }))

    /** 上传文件/图片到电脑，[open] 每次调用返回新的输入流（OkHttp 可能重试）。 */
    suspend fun chatUpload(name: String, isImage: Boolean, length: Long, open: () -> InputStream): ChatMessage =
        withContext(Dispatchers.IO) {
            val body = object : RequestBody() {
                override fun contentType() = "application/octet-stream".toMediaType()
                override fun contentLength() = length
                override fun writeTo(sink: BufferedSink) {
                    open().use { sink.writeAll(it.source()) }
                }
            }
            val u = url("/api/chat/upload", "name" to name, "kind" to if (isImage) "image" else "file")
            parseMessage(call(transferHttp, builder(u).post(body).build(), { obj(it) }))
        }

    /** 下载聊天里的文件，内容写入 [sink]。 */
    suspend fun chatDownload(id: String, sink: java.io.OutputStream) = withContext(Dispatchers.IO) {
        call(transferHttp, builder(url("/api/chat/file/$id")).get().build()) { r -> r.body!!.byteStream().use { it.copyTo(sink) } }
    }
}
