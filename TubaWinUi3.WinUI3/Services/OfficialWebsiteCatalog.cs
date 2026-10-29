namespace TubaWinUi3.Services;

public sealed record OfficialWebsite(string Name, string Url, string? Description = null)
{
    public string FaviconUrl =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? $"https://{uri.Host}/favicon.ico"
            : "";
}

public sealed record OfficialWebsiteCategory(string Name, string Glyph, IReadOnlyList<OfficialWebsite> Sites);

public static class OfficialWebsiteCatalog
{
    public static IReadOnlyList<OfficialWebsiteCategory> GetCategories() =>
    [
        new("游戏平台", "\uE7FC",
        [
            new("Steam", "https://store.steampowered.com/", "全球最大的 PC 游戏平台"),
            new("Epic Games", "https://www.epicgames.com/store/zh-CN/", "每周免费游戏"),
            new("GOG", "https://www.gog.com/", "无 DRM 数字游戏商店"),
            new("WeGame", "https://www.wegame.com.cn/", "腾讯游戏平台"),
            new("Xbox", "https://www.xbox.com/zh-CN/", "微软游戏主机与订阅服务"),
            new("PlayStation", "https://www.playstation.com/zh-cn/", "索尼游戏主机与商店"),
            new("暴雪战网", "https://www.blizzard.com/zh-cn/", "暴雪游戏官方"),
            new("EA", "https://www.ea.com/zh-cn", "EA 游戏官方"),
            new("Ubisoft 育碧", "https://www.ubisoft.com/zh-cn/", "育碧游戏官方"),
            new("米哈游", "https://www.mihoyo.com/", "《原神》《崩坏》系列开发商"),
            new("网易游戏", "https://game.163.com/", "网易旗下游戏"),
            new("Nintendo 任天堂", "https://www.nintendo.com/", "任天堂官方"),
            new("TapTap", "https://www.taptap.cn/", "游戏社区与下载")
        ]),
        new("游戏加速器", "\uE7A6",
        [
            new("UU 加速器", "https://uu.163.com/", "网易出品的游戏加速器"),
            new("雷神加速器", "https://www.leigod.com/", "雷神游戏加速器"),
            new("迅游加速器", "https://www.xunyou.com/", "迅游网游加速器"),
            new("奇游加速器", "https://www.qiyou.cn/", "奇游手游加速器"),
            new("海豚加速器", "https://www.htjsq.com/", "海豚游戏加速器"),
            new("玲珑加速器", "https://www.lljsq.net/", "玲珑游戏加速器"),
            new("小黑盒加速器", "https://acc.xiaoheihe.cn/", "小黑盒游戏加速器")
        ]),
        new("硬件厂商", "\uE774",
        [
            new("NVIDIA", "https://www.nvidia.cn/", "显卡驱动与 GeForce"),
            new("AMD", "https://www.amd.com/zh-cn", "CPU/GPU 驱动与软件"),
            new("Intel", "https://www.intel.cn/", "处理器与驱动"),
            new("华硕", "https://www.asus.com.cn/", "主板/显卡/笔记本"),
            new("微星", "https://cn.msi.com/", "主板/显卡/笔记本"),
            new("技嘉", "https://www.gigabyte.cn/", "主板/显卡/笔记本"),
            new("华擎", "https://www.asrock.com/", "主板/显卡"),
            new("七彩虹", "https://www.colorful.cn/", "显卡/主板/存储"),
            new("铭瑄", "https://www.maxsun.com.cn/", "显卡/主板"),
            new("影驰", "https://www.szgalaxy.com/", "显卡/SSD"),
            new("索泰", "https://www.zotac.com/cn/", "显卡/迷你主机"),
            new("蓝宝石", "https://www.sapphiretech.com/zh-cn", "A 卡显卡"),
            new("联想", "https://www.lenovo.com.cn/", "笔记本/台式机"),
            new("惠普", "https://www.hp.com/cn-zh/home.html", "笔记本/台式机"),
            new("戴尔", "https://www.dell.com/zh-cn", "笔记本/台式机"),
            new("宏碁", "https://www.acer.com.cn/", "笔记本/台式机"),
            new("神舟", "http://www.hasee.com/", "游戏本/台式机"),
            new("机械革命", "https://www.mechrevo.com/", "游戏本"),
            new("外星人", "https://www.alienware.com.cn/", "高端游戏 PC"),
            new("小米", "https://www.mi.com/", "手机/笔记本/智能硬件"),
            new("荣耀", "https://www.honor.com/cn/", "手机/笔记本/平板"),
            new("三星", "https://www.samsung.com/cn/", "手机/存储/显示器"),
            new("罗技", "https://www.logitech.com.cn/zh-cn", "键鼠/外设"),
            new("雷蛇", "https://cn.razerzone.com/", "游戏外设"),
            new("金士顿", "https://www.kingston.com.cn/", "内存/存储"),
            new("西数", "https://www.westerndigital.com/zh-cn", "机械硬盘/SSD"),
            new("希捷", "https://www.seagate.com/cn/zh/", "机械硬盘/SSD")
        ]),
        new("微软系统", "\uE70F",
        [
            new("Microsoft 官网", "https://www.microsoft.com/zh-cn", "微软中国官网"),
            new("Windows 11 下载", "https://www.microsoft.com/zh-cn/software-download/windows11", "系统镜像官方下载"),
            new("Microsoft Store", "https://apps.microsoft.com/", "微软应用商店"),
            new("Office", "https://www.office.com/", "Office 套件在线版"),
            new(".NET 下载", "https://dotnet.microsoft.com/zh-cn/download", ".NET 运行时与 SDK"),
            new("Visual Studio", "https://visualstudio.microsoft.com/zh-hans/", "微软 IDE"),
            new("VS Code", "https://code.visualstudio.com/", "轻量级代码编辑器")
        ]),
        new("浏览器", "\uE774",
        [
            new("Chrome", "https://www.google.cn/chrome/", "Google 浏览器"),
            new("Edge", "https://www.microsoft.com/zh-cn/edge", "微软 Edge 浏览器"),
            new("Firefox", "https://www.mozilla.org/zh-CN/firefox/", "火狐浏览器"),
            new("夸克", "https://www.quark.cn/", "夸克浏览器"),
            new("360 浏览器", "https://browser.360.cn/", "360 安全浏览器"),
            new("搜狗浏览器", "https://ie.sogou.com/", "搜狗高速浏览器"),
            new("Opera", "https://www.opera.com/zh-cn", "Opera 浏览器")
        ]),
        new("办公效率", "\uE8A5",
        [
            new("WPS", "https://www.wps.cn/", "金山办公套件"),
            new("钉钉", "https://www.dingtalk.com/", "阿里企业协同办公"),
            new("企业微信", "https://work.weixin.qq.com/", "腾讯企业通讯"),
            new("飞书", "https://www.feishu.cn/", "字节跳动协同办公"),
            new("腾讯会议", "https://meeting.tencent.com/", "在线视频会议"),
            new("7-Zip", "https://www.7-zip.org/", "免费压缩软件"),
            new("Notepad++", "https://notepad-plus-plus.org/", "文本编辑器"),
            new("Notion", "https://www.notion.so/", "笔记与知识管理"),
            new("腾讯文档", "https://docs.qq.com/", "在线协作文档"),
            new("迅雷", "https://www.xunlei.com/", "下载工具"),
            new("IDM", "https://www.internetdownloadmanager.com/", "Internet Download Manager"),
            new("百度网盘", "https://pan.baidu.com/", "百度网盘"),
            new("阿里云盘", "https://www.alipan.com/", "阿里云盘"),
            new("夸克网盘", "https://pan.quark.cn/", "夸克网盘")
        ]),
        new("社交娱乐", "\uE8D2",
        [
            new("微信", "https://weixin.qq.com/", "微信 PC 版"),
            new("QQ", "https://im.qq.com/", "QQ 下载"),
            new("哔哩哔哩", "https://www.bilibili.com/", "B 站视频平台"),
            new("微博", "https://weibo.com/", "微博社交平台"),
            new("抖音", "https://www.douyin.com/", "短视频平台"),
            new("快手", "https://www.kuaishou.com/", "短视频平台"),
            new("网易云音乐", "https://music.163.com/", "网易云音乐"),
            new("QQ 音乐", "https://y.qq.com/", "QQ 音乐"),
            new("爱奇艺", "https://www.iqiyi.com/", "在线视频平台"),
            new("腾讯视频", "https://v.qq.com/", "在线视频平台"),
            new("优酷", "https://www.youku.com/", "在线视频平台"),
            new("芒果 TV", "https://www.mgtv.com/", "在线视频平台"),
            new("斗鱼", "https://www.douyu.com/", "游戏直播平台"),
            new("虎牙", "https://www.huya.com/", "游戏直播平台")
        ]),
        new("开发工具", "\uE943",
        [
            new("GitHub", "https://github.com/", "代码托管平台"),
            new("GitLab", "https://about.gitlab.com/", "DevOps 平台"),
            new("Gitee", "https://gitee.com/", "国内代码托管平台"),
            new("Node.js", "https://nodejs.org/zh-cn", "JavaScript 运行时"),
            new("Python", "https://www.python.org/", "Python 官方"),
            new(".NET", "https://dotnet.microsoft.com/zh-cn/", "Microsoft .NET 官方"),
            new("NuGet", "https://www.nuget.org/", ".NET 包管理器"),
            new("JetBrains", "https://www.jetbrains.com/zh-cn/", "IntelliJ/PyCharm 等 IDE"),
            new("Docker", "https://www.docker.com/", "容器化平台"),
            new("VMware", "https://www.vmware.com/", "虚拟机软件"),
            new("WinRAR", "https://www.winrar.com.cn/", "WinRAR 中文官网"),
            new("阿里云", "https://www.aliyun.com/", "阿里云计算平台"),
            new("腾讯云", "https://cloud.tencent.com/", "腾讯云计算平台")
        ]),
        new("检测工具", "\uE9D2",
        [
            new("鲁大师", "https://www.ludashi.com/", "电脑检测与评分"),
            new("CPU-Z", "https://www.cpuid.com/", "处理器信息检测"),
            new("GPU-Z", "https://www.techpowerup.com/gpuz/", "显卡信息检测"),
            new("驱动人生", "https://www.160.com/", "驱动管理"),
            new("驱动精灵", "https://www.drivergenius.com/", "驱动管理")
        ]),
        new("远程工具", "\uE7B5",
        [
            new("UU 远程", "https://uuyc.163.com/", "网易免费远程控制与游戏串流"),
            new("向日葵", "https://sunlogin.oray.com/", "远程桌面控制"),
            new("TeamViewer", "https://www.teamviewer.cn/cn/", "远程控制软件"),
            new("ToDesk", "https://www.todesk.com/", "远程桌面软件"),
            new("RustDesk", "https://rustdesk.com/zh-cn/", "开源远程桌面")
        ])
    ];
}
