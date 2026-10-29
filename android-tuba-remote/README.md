# 空岛图吧工具箱手机助手（android-tuba-remote）

原生 Android（Kotlin + Jetpack Compose + Material 3）。通过局域网连接电脑端「内置工具 → 连接手机」：

- 局域网发现：广播 UDP 18764，UTF-8 `TUBALINK_DISCOVER_V1`；仅接受 `type=tubalink, version=1` 回复，HTTP 端口来自回复，IP 只取 UDP 来源（忽略正文 host）。每次约 3 秒，可停止，离开列表取消；未发现可用地址＋连接码手动兜底。
- 自动连接：成功配对后本机保存地址、令牌与配对码；下次启动优先令牌，仅 HTTP 401 才用保存码重新配对（网络失败不发送码）。旧数据无保存码时请手动输入一次。删除设备会清掉凭据，禁用云备份与设备迁移中的偏好设置。
- 安全：发现报文没有稳定身份，不能凭同名电脑迁移 secret；IP 或端口变化需手动输入码确认。协议是明文 HTTP，只应在可信局域网使用；电脑刷新/撤销配对码后应重新输入。
- 连接方式：扫二维码（`tubalink://IP:端口?code=配对码`）/ 手动输入 IP + 6 位配对码
- 主界面：电脑配置（硬件信息，同电脑端「硬件信息」）+ 电脑屏幕截图（手动刷新）
- 小工具：实时硬件监控（自选监控项与曲线图，含 FPS）、PowerShell 终端、winget 应用安装、传输助手（文字 / 图片 / 文件互传）

协议为电脑端 `PhoneLinkService` 提供的 HTTP + JSON 接口（`X-Token` 鉴权，配对码换取令牌）。仅限可信局域网使用。

构建：`./gradlew assembleDebug test`（需要 Android SDK；CI：`.github/workflows/android-build.yml`）。
