#nullable disable
// 移植自 RogueCleaner（https://github.com/aakk007/RogueCleaner），MIT License，Copyright (c) 2026 aakk007
// 原版为 .NET Framework 4.x WinForms；此处为 WinUI 3 移植，逻辑保持一致。


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TubaWinUi3.Services.RogueCleaner
{

    internal sealed class VendorEvidence
    {
        public readonly List<string> HumanTexts = new List<string>();
        public readonly List<string> Publishers = new List<string>();
        public readonly List<string> ProductNames = new List<string>();
        public readonly List<string> TechnicalIdentifiers = new List<string>();
        public readonly List<string> Commands = new List<string>();
        public readonly List<string> FilePaths = new List<string>();
        public readonly List<string> MsiProductCodes = new List<string>();
        public readonly List<string> OpaqueValues = new List<string>();

        public VendorEvidence AddHuman(params string[] values) { Add(HumanTexts, values); return this; }
        public VendorEvidence AddPublisher(params string[] values) { Add(Publishers, values); return this; }
        public VendorEvidence AddProduct(params string[] values) { Add(ProductNames, values); return this; }
        public VendorEvidence AddTechnical(params string[] values) { Add(TechnicalIdentifiers, values); return this; }
        public VendorEvidence AddCommand(params string[] values) { Add(Commands, values); return this; }
        public VendorEvidence AddFile(params string[] values) { Add(FilePaths, values); return this; }
        public VendorEvidence AddMsi(params string[] values) { Add(MsiProductCodes, values); return this; }
        public VendorEvidence AddOpaque(params string[] values) { Add(OpaqueValues, values); return this; }

        private static void Add(List<string> target, IEnumerable<string> values)
        {
            if (values == null) return;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!target.Contains(value, StringComparer.OrdinalIgnoreCase)) target.Add(value.Trim());
            }
        }
    }

    internal sealed class VendorIdentityResult
    {
        public string Vendor { get; set; }
        public int Confidence { get; set; }
        public bool Confirmed { get; set; }
        public bool Conflicted { get; set; }
        public string EvidenceSummary { get; set; }
    }

    internal static class RuleCatalog
    {
        private sealed class VendorRule
        {
            public string Name;
            public string Snark;
            public int Boost;
            public bool BehaviorOnly;
            public string[] Patterns;
            public string[] BadComponents;
        }

        private static readonly VendorRule[] Vendors = new VendorRule[]
        {
            new VendorRule { Name = "360 系列", Snark = "右键桌面不够，还想住进开机启动。", Boost = 25, Patterns = new [] { "Qihoo", "Qihu", "奇虎", "360.cn", "360Safe", "360sd", "360rp", "360se", "360Chrome", "360zip", "360Desktop", "360DesktopLite", "360Wallpaper", "360AlbumViewer", "360AI图片", "360AI", "360Pic", "360KanPic", "360Image", "Safe360Ext", "SoftMgrExt", "AblumViewer", "AlbumViewer", "shell360ext", "QHActiveDefense", "ZhuDongFangYu", "QHWatchdog", "QHProtected", "QHWebProtection", "QHSafeTray", "360软件管家", "360安全卫士", "360压缩", "360浏览器", "360极速浏览器", "360看图" }, BadComponents = new [] { "Safe360Ext", "SoftMgrExt", "AblumViewerMenuExt", "AlbumViewerMenuExt", "ShellExt64.dll", "shell360ext64.dll", "360AI图片", "QHWatchdog", "QHProtected" } },
            new VendorRule { Name = "WPS / 金山", Snark = "文档软件顺手也想接管图片、云文档和右键。", Boost = 18, Patterns = new [] { "WPS Office", "WPS.", "WPS_", "WPS-", "Kingsoft", "金山", "Zhuhai Kingsoft", "kwps", "qingshell", "qingnse", "kdesktop", "kdocs", "photolaunch", "wpscloud", "WpsDrive", "WPS.PIC", "WPSPic", "WPSPhoto", "WPS图片", "QingNseContextMenu", "kwpsshellext", "qingshellext", "kdesktopshellext", "qkdesktopshellext", "WPSAI", "WPS AI", "KingsoftAI", "AiWPS", "WPS灵犀", "wpsLingxi", "lingxi", "旺仔", "Wangzai", "wpscenter", "wpsupdate", "WpsUpdateTask", "WPS Office Cloud Service", "wpscloudsvr", "ksomisc" }, BadComponents = new [] { "kwpsshellext", "qingshellext", "QingNseContextMenu", "kdesktopshellext", "qkdesktopshellext", "WPS.PIC", "WPSPic", "photolaunch.exe", "Wangzai", "wpscloudsvr" } },
            new VendorRule { Name = "百度 / 百度网盘", Snark = "网盘不只同步文件，还喜欢同步到右键菜单。", Boost = 18, Patterns = new [] { "Baidu", "百度", "BaiduNetdisk", "BaiduNetdiskUnite", "BaiduNetdiskImageViewer", "BaiduNetdiskImageView", "BaiduNetdiskDesktopSync", "BaiduNetdiskSync", "BaiduNetdiskUtility", "BaiduNetdiskService", "BaiduNetdiskHost", "BaiduYun", "BaiduYunDetect", "YunShell", "YunShellExt", "YunDetectService", "cloudpic", "百度网盘看图", "百度网盘同步", "北京度友" }, BadComponents = new [] { "YunShellExt", "YunShellExplorerCommand", "BaiduNetdiskImageViewer", "BaiduNetdiskImageView", "BaiduNetdiskUtility", "BaiduNetdiskService", "cloudpic.dll", "imageviewer" } },
            new VendorRule { Name = "夸克 / 夸克网盘", Snark = "网盘上传和 PDF 转换也来抢右键，至少别披成迅雷的马甲。", Boost = 18, Patterns = new [] { "QuarkCloudDrive", "QuarkCloudDrive.upload", "QuarkCloudDrive.backup", "QuarkNetdisk", "QuarkDisk", "QuarkPan", "QuarkPDF", "QuarkConvert", "QuarkPC", "quark.cn", "pan.quark.cn", "vt.quark.cn", "quark-pc", "external_rclick", "夸克", "夸克网盘", "夸克浏览器", "上传到夸克网盘", "夸克网盘上传" }, BadComponents = new [] { "QuarkCloudDrive.upload", "QuarkCloudDrive.backup", "QuarkPDF", "QuarkConvert", "quark-pc", "external_rclick", "上传到夸克网盘", "PDF转换", "图片转PDF", "万能转换" } },
            new VendorRule { Name = "搜狗", Snark = "输入法可以输入字，但没必要输入到开机项里。", Boost = 16, Patterns = new [] { "Sogou", "搜狗", "SogouInput", "SogouPY", "SogouExplorer", "SogouCloud", "SogouIme", "SogouImeBroker", "SogouImeMgr", "SogouFlash", "SogouTips", "SogouNews", "SogouPopup", "SogouSvc", "SGImeGuard", "SogouInputPop", "SogouAd", "SogouUpdate", "SogouComMgr", "PinyinUp" }, BadComponents = new [] { "SogouImeBroker", "SogouExplorer", "SogouFlash", "SogouTips", "SogouAd", "SogouInputPop", "SogouPopup", "SogouNews", "SGImeGuard" } },
            new VendorRule { Name = "迅雷", Snark = "下载器最爱给自己安排开机打卡。", Boost = 20, Patterns = new [] { "Xunlei", "Thunder", "迅雷", "Thunder Network", "XLService", "XLServicePlatform", "ThunderPlatform", "ThunderAgent", "ThunderStart", "ThunderBrowser", "XunleiBHO", "XunleiDownload", "XunleiMedia", "Xunlei.XLB", "XLLiveUD", "XLGameBox", "TBCrash", "迅雷下载助手" }, BadComponents = new [] { "XLService", "XLServicePlatform", "ThunderPlatform", "Xunlei.XLB", "ThunderBrowser", "ThunderStart", "XunleiBHO" } },
            new VendorRule { Name = "钉钉", Snark = "办公协作可以，文件右键也要塞上传入口就过界了。", Boost = 14, Patterns = new [] { "DingTalk", "Dingtalk", "dingtalk", "DingDing", "钉钉", "DingTalkShellExt", "DingTalkContextMenu", "DingTalkUpload", "DingTalkDrive", "DingTalkDocs", "DingTalkFile", "DingTalkOffice", "DingTalkLite", "AliDingTalk", "com.alibaba.dingtalk", "上传钉钉并打开", "上传到钉钉", "钉钉并打开", "钉盘" }, BadComponents = new [] { "DingTalkShellExt", "DingTalkContextMenu", "DingTalkUpload", "上传钉钉并打开", "上传到钉钉", "DingTalkDrive" } },
            new VendorRule { Name = "腾讯系", Snark = "聊天归聊天，别顺手接管浏览器和启动项。", Boost = 12, Patterns = new [] { "Tencent", "腾讯", "QQBrowser", "QQPCMgr", "QQPCMGR", "QQProtect", "QQPCRTP", "QQRepair", "QQShellExt", "TXShell", "TIM.exe", "TIM\\", "WeChat", "微信", "企业微信", "WXWork", "TencentDocs", "腾讯文档", "QQLive", "QQMusic", "QBCore", "QBUpdate", "电脑管家" }, BadComponents = new [] { "QQPCMgr", "QQBrowser", "QQProtect", "QQPCRTP", "QQShellExt", "TXShell", "QBUpdate" } },
            new VendorRule { Name = "2345 系列", Snark = "名字像门牌号，行为像钉子户。", Boost = 25, Patterns = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345PicViewer", "2345Kantuwang", "2345Zip", "2345Safe", "2345Protect", "2345Svc", "2345MiniPage", "2345Browser", "2345看图王", "2345好压", "王牌" }, BadComponents = new [] { "2345Explorer", "2345Soft", "2345SoftMgr", "2345Pic", "2345Zip", "2345Protect", "2345MiniPage" } },
            new VendorRule { Name = "猎豹 / 金山毒霸", Snark = "安全软件当然能安全，问题是别把自己藏成常驻钉子。", Boost = 18, Patterns = new [] { "Cheetah", "猎豹", "Liebao", "Kingsoft Internet Security", "金山毒霸", "KSafe", "KSafeSvc", "KWatch", "kismain", "kavsrv", "KSafeTray", "KMailMon", "KSoft" }, BadComponents = new [] { "KSafeSvc", "KWatch", "kavsrv", "KSafeTray", "Cheetah" } },
            new VendorRule { Name = "驱动/硬件检测工具", Snark = "修驱动可以，常驻当监工就过分了。", Boost = 18, Patterns = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "驱动精灵", "驱动人生", "MyDrivers", "DrvMgr", "DGDaemon", "DTLService", "LuDaShi", "鲁大师", "MasterLu", "LdsLite", "LdsSvc", "LdsDaemon", "ComputerZ", "HardwareProtect" }, BadComponents = new [] { "DriverGenius", "DriverLife", "DriveTheLife", "LuDaShi", "MasterLu", "LdsSvc", "LdsDaemon" } },
            new VendorRule { Name = "Bandisoft 看图/压缩工具", Snark = "看图软件也要在右键菜单刷存在感。", Boost = 12, Patterns = new [] { "Bandisoft", "BandiView", "BandiView.exe", "Bandiview", "Honeyview", "HoneyView", "Bandizip", "BandiZip", "BandizipShellext", "BandizipShell", "BandiViewShell", "BandiViewExt", "BandiViewShellExt", "Open with BandiView", "Browse with BandiView", "用 BandiView", "使用 BandiView" }, BadComponents = new [] { "BandiViewShell", "BandiViewExt", "BandiViewShellExt", "BandizipShellext", "BandizipShell" } },
            new VendorRule { Name = "国产压缩/看图工具", Snark = "压缩包还没打开，右键先被挤爆了。", Boost = 12, Patterns = new [] { "KuaiZip", "快压", "Kuaizip", "HaoZip", "好压", "2345Zip", "360压缩", "360zip", "2345Pic", "2345看图王", "XnViewShell", "KanPic", "看图王", "极速看图", "JisuPic" }, BadComponents = new [] { "KuaiZip", "Kuaizip", "HaoZip", "2345Zip", "360zip", "2345Pic" } },
            new VendorRule { Name = "国产浏览器/导航", Snark = "浏览器自己跑就行，别把下载、主页和启动项全包了。", Boost = 16, Patterns = new [] { "SogouExplorer", "搜狗高速浏览器", "QQBrowser", "360se", "360Chrome", "2345Explorer", "2345Browser", "Liebao", "猎豹浏览器", "CheetahBrowser", "Maxthon", "傲游", "UCBrowser", "UCBrowser", "TheWorld", "世界之窗", "BaiduBrowser", "百度浏览器" }, BadComponents = new [] { "SogouExplorer", "QQBrowser", "2345Explorer", "CheetahBrowser", "UCService", "BaiduBrowser" } },
            new VendorRule { Name = "Flash 中国特供组件", Snark = "Flash 都退役了，助手还想在后台上班。", Boost = 22, Patterns = new [] { "FlashHelperService", "Flash Center", "FlashCenter", "Flash大厅", "FlashHelper", "FlashRepair", "FlashService", "flash.cn" }, BadComponents = new [] { "FlashHelperService", "FlashCenter", "FlashHelper" } },
            new VendorRule { Name = "手机助手/设备助手", Snark = "连一次手机，后台服务倒是记住一辈子。", Boost = 12, Patterns = new [] { "i4Tools", "爱思助手", "Aisi", "PP助手", "PPAssistant", "91助手", "91Assistant", "Wandoujia", "豌豆荚", "BaiduMobile", "TencentMobileManager", "应用宝", "HiSuite", "华为手机助手", "MiPhoneAssistant", "小米助手" }, BadComponents = new [] { "i4Tools", "PPAssistant", "91Assistant", "Wandoujia", "TencentMobileManager" } },
            new VendorRule { Name = "国产影音/游戏大厅", Snark = "看个视频玩个游戏，不需要抢文件关联和开机席位。", Boost = 10, Patterns = new [] { "iQIYI", "爱奇艺", "Qiyi", "Youku", "优酷", "Kugou", "酷狗", "Kuwo", "酷我", "PPTV", "暴风", "Baofeng", "QQLive", "TencentVideo", "腾讯视频", "XunleiMedia", "Bilibili", "芒果TV", "MangoTV", "WeGame", "SteamChina" }, BadComponents = new [] { "iQIYI", "Qiyi", "Youku", "Kugou", "Kuwo", "PPTV", "Baofeng", "QQLive", "TencentVideo" } },
            new VendorRule { Name = "PDF/办公捆绑工具", Snark = "读个 PDF，也别顺手接管全系统打开方式。", Boost = 10, Patterns = new [] { "JisuPDF", "极速PDF", "SwiftPDF", "迅捷PDF", "Foxit", "福昕", "CAJViewer", "PDFReader", "PDFSuite", "PDFMaster", "嗨格式", "HiFormat" }, BadComponents = new [] { "JisuPDF", "SwiftPDF", "PDFMaster", "HiFormat" } },
            new VendorRule { Name = "预装管家/厂商助手", Snark = "出厂自带不等于可以偷偷常驻。", Boost = 8, Patterns = new [] { "LenovoUtility", "LenovoVantage", "联想电脑管家", "LenovoPcManager", "Huawei PC Manager", "华为电脑管家", "HonorPCManager", "荣耀电脑管家", "MiService", "小米电脑管家", "MyASUS", "华硕电脑管家", "AcerCare", "Dell SupportAssist" }, BadComponents = new [] { "LenovoPcManager", "Huawei PC Manager", "HonorPCManager", "MiService", "SupportAssist" } },
            new VendorRule { Name = "弹窗广告/推广组件", Snark = "关掉没一会儿又弹，这类小广告最会装死。", Boost = 22, BehaviorOnly = true, Patterns = new [] { "SogouNews", "SogouPopup", "SogouTips", "SogouAd", "SogouInputPop", "2345MiniPage", "MiniNews", "HotNews", "NewsPop", "PopNews", "PopWnd", "AdPop", "AdService", "AdPush", "WpsNotify", "KNotify", "BaiduTips", "BaiduNews", "QQBrowserMini", "KugouTips", "KuwoNews", "QiyiNews", "YoukuNews", "LuDaShiNews", "MasterLuMini", "DriverGeniusNews", "KuaiZipNews", "HaoZipMiniPage", "今日热点", "每日热点", "热点资讯", "迷你页", "推荐弹窗", "广告弹窗" }, BadComponents = new [] { "SogouNews", "SogouPopup", "2345MiniPage", "AdPop", "AdService", "WpsNotify", "BaiduTips", "LuDaShiNews", "KuaiZipNews" } },
            new VendorRule { Name = "守护/自动恢复组件", Snark = "你关它一次，它守护进程能把自己续上三回。", Boost = 20, BehaviorOnly = true, Patterns = new [] { "QHWatchdog", "QHProtected", "QHActiveDefense", "SGImeGuard", "SogouImeBroker", "XLServicePlatform", "ThunderPlatform", "BaiduYunDetect", "YunDetectService", "BaiduNetdiskUtility", "QQProtect", "QQPCRTP", "2345Protect", "2345Svc", "KSafeSvc", "KWatch", "LdsDaemon", "LdsSvc", "FlashHelperService", "FlashCenter", "DriverGeniusDaemon", "DTLService", "LuDaShiDaemon" }, BadComponents = new [] { "QHWatchdog", "QHProtected", "SGImeGuard", "XLServicePlatform", "BaiduYunDetect", "QQProtect", "2345Protect", "KSafeSvc", "LdsDaemon", "FlashHelperService" } }
        };

        private sealed class CandidateScore
        {
            public VendorRule Rule;
            public int Score;
            public bool Strong;
            public readonly HashSet<string> Sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Reasons = new List<string>();
        }

        private sealed class FileIdentity
        {
            public string Path;
            public string Company;
            public string Product;
            public string Description;
            public string Signer;
            public bool SignatureValid;
        }

        private sealed class MsiIdentity
        {
            public string ProductName;
            public string Publisher;
            public string InstallLocation;
            public string LocalPackage;
        }

        private sealed class InstalledOwner
        {
            public string Root;
            public string Publisher;
            public string ProductName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            [MarshalAs(UnmanagedType.LPWStr)] public string UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }

        private static readonly object IdentityCacheGate = new object();
        private static readonly Dictionary<string, FileIdentity> FileIdentityCache = new Dictionary<string, FileIdentity>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MsiIdentity> MsiIdentityCache = new Dictionary<string, MsiIdentity>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<InstalledOwner> InstalledOwners = new List<InstalledOwner>();
        private static bool InstalledOwnersLoaded;
        private static readonly HashSet<string> SystemHosts = new HashSet<string>(new string[]
        {
            "msiexec.exe", "rundll32.exe", "regsvr32.exe", "svchost.exe", "explorer.exe", "cmd.exe",
            "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiGetProductInfo(string product, string property, StringBuilder value, ref int length);

        public static VendorIdentityResult ResolveIdentity(VendorEvidence evidence)
        {
            if (evidence == null) evidence = new VendorEvidence();
            EnrichEvidence(evidence);
            List<CandidateScore> candidates = new List<CandidateScore>();
            foreach (VendorRule rule in Vendors)
            {
                CandidateScore candidate = ScoreRule(rule, evidence);
                if (candidate.Score > 0) candidates.Add(candidate);
            }
            candidates = candidates.Where(delegate(CandidateScore item) { return !item.Rule.BehaviorOnly; }).ToList();
            candidates = candidates.OrderByDescending(delegate(CandidateScore item) { return item.Strong; })
                .ThenByDescending(delegate(CandidateScore item) { return item.Score; }).ToList();
            if (candidates.Count == 0)
            {
                return UnknownIdentity(false, "没有可信厂商证据");
            }

            CandidateScore best = candidates[0];
            bool confirmed = best.Strong || (best.Score >= 70 && best.Sources.Count >= 2);
            CandidateScore conflict = candidates.Skip(1).FirstOrDefault(delegate(CandidateScore item)
            {
                bool otherConfirmed = item.Strong || (item.Score >= 70 && item.Sources.Count >= 2);
                if (!otherConfirmed) return false;
                if (best.Strong && item.Strong) return true;
                return Math.Abs(best.Score - item.Score) < 25;
            });
            if (conflict != null)
            {
                return UnknownIdentity(true, "强证据冲突：" + best.Rule.Name + " / " + conflict.Rule.Name);
            }
            if (!confirmed)
            {
                return UnknownIdentity(false, "证据不足：" + string.Join("，", best.Reasons.Take(3).ToArray()));
            }
            return new VendorIdentityResult
            {
                Vendor = best.Rule.Name,
                Confidence = Math.Min(100, best.Strong ? Math.Max(95, best.Score) : best.Score),
                Confirmed = true,
                Conflicted = false,
                EvidenceSummary = string.Join("，", best.Reasons.Distinct().Take(5).ToArray())
            };
        }

        public static bool HasBadComponent(VendorEvidence evidence, VendorIdentityResult identity)
        {
            if (evidence == null || identity == null || !identity.Confirmed) return false;
            VendorRule rule = Vendors.FirstOrDefault(delegate(VendorRule item) { return item.Name == identity.Vendor; });
            if (rule == null) return false;
            IEnumerable<string> values = evidence.HumanTexts.Concat(evidence.ProductNames).Concat(evidence.TechnicalIdentifiers)
                .Concat(evidence.FilePaths.Select(delegate(string value) { return SafePathFileName(value); }));
            foreach (string value in values)
            {
                foreach (string pattern in rule.BadComponents)
                {
                    if (SafePatternMatch(value, pattern, true)) return true;
                }
            }
            return false;
        }

        public static int VendorBoost(VendorIdentityResult identity, bool badComponent)
        {
            if (identity == null || !identity.Confirmed) return 0;
            VendorRule rule = Vendors.FirstOrDefault(delegate(VendorRule item) { return item.Name == identity.Vendor; });
            if (rule == null) return 0;
            return 35 + rule.Boost + (badComponent ? 30 : 0);
        }

        private static CandidateScore ScoreRule(VendorRule rule, VendorEvidence evidence)
        {
            CandidateScore candidate = new CandidateScore { Rule = rule };
            ScoreValues(candidate, rule, evidence.Publishers, "Publisher", 60, false, false);
            ScoreValues(candidate, rule, evidence.ProductNames, "Product", 45, false, false);
            ScoreValues(candidate, rule, evidence.HumanTexts, "Human", 40, false, false);
            ScoreValues(candidate, rule, evidence.TechnicalIdentifiers, "Technical", 40, true, false);
            ScoreValues(candidate, rule, evidence.FilePaths, "Path", 30, true, false);
            foreach (string path in evidence.FilePaths)
            {
                FileIdentity file = GetFileIdentity(path);
                if (file == null) continue;
                if (file.SignatureValid) ScoreValues(candidate, rule, new string[] { file.Signer }, "Signature:" + file.Path, 100, false, true);
                ScoreValues(candidate, rule, new string[] { file.Company }, "Company:" + file.Path, 60, false, false);
                ScoreValues(candidate, rule, new string[] { file.Product, file.Description }, "FileProduct:" + file.Path, 45, false, false);
            }
            return candidate;
        }

        private static void ScoreValues(CandidateScore candidate, VendorRule rule, IEnumerable<string> values, string source, int score, bool technical, bool strong)
        {
            if (values == null) return;
            int index = 0;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) { index++; continue; }
                string pattern = MatchingPattern(rule, value, technical);
                if (!string.IsNullOrEmpty(pattern))
                {
                    string sourceKey = source + ":" + index;
                    if (candidate.Sources.Add(sourceKey)) candidate.Score += score;
                    candidate.Strong = candidate.Strong || strong;
                    candidate.Reasons.Add(source.Split(':')[0] + "=" + pattern);
                }
                index++;
            }
        }

        private static string MatchingPattern(VendorRule rule, string value, bool technical)
        {
            foreach (string pattern in rule.Patterns)
            {
                if (technical && pattern.Length < 5 && pattern.All(delegate(char c) { return c < 128; })) continue;
                bool distinctive = rule.BadComponents.Any(delegate(string item) { return item.Equals(pattern, StringComparison.OrdinalIgnoreCase); });
                if (SafePatternMatch(value, pattern, technical && !distinctive)) return pattern;
            }
            return null;
        }

        private static bool SafePatternMatch(string text, string pattern, bool technical)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern)) return false;
            int start = 0;
            while (true)
            {
                int index = text.IndexOf(pattern, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool asciiAlphaNumeric = pattern.All(delegate(char c) { return c < 128 && char.IsLetterOrDigit(c); });
                bool boundaryRequired = asciiAlphaNumeric && (technical || pattern.Length <= 4 || pattern.All(char.IsDigit));
                if (!boundaryRequired) return true;
                int end = index + pattern.Length;
                bool leftBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                bool rightBoundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (leftBoundary && rightBoundary) return true;
                start = index + 1;
            }
        }

        private static void EnrichEvidence(VendorEvidence evidence)
        {
            foreach (string command in evidence.Commands.ToArray())
            {
                string file = ExtractTargetFile(command);
                if (!string.IsNullOrEmpty(file)) evidence.AddFile(file);
                string productCode = ExtractProductCode(command);
                if (!string.IsNullOrEmpty(productCode)) evidence.AddMsi(productCode);
            }
            foreach (string value in evidence.MsiProductCodes.ToArray())
            {
                string productCode = ExtractProductCode(value);
                if (string.IsNullOrEmpty(productCode)) continue;
                MsiIdentity msi = GetMsiIdentity(productCode);
                if (msi == null) continue;
                evidence.AddPublisher(msi.Publisher).AddProduct(msi.ProductName).AddFile(msi.LocalPackage);
                if (!string.IsNullOrWhiteSpace(msi.InstallLocation)) evidence.AddFile(msi.InstallLocation);
            }
            foreach (string value in evidence.FilePaths.ToArray())
            {
                string file = NormalizeCandidateFile(value);
                if (!string.IsNullOrEmpty(file)) evidence.AddFile(file);
            }
            EnrichInstalledOwnership(evidence);
        }

        private static void EnrichInstalledOwnership(VendorEvidence evidence)
        {
            EnsureInstalledOwners();
            foreach (string value in evidence.FilePaths.ToArray())
            {
                string normalized;
                try { normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('\"'))).TrimEnd('\\') + "\\"; }
                catch { continue; }
                List<InstalledOwner> owners;
                lock (IdentityCacheGate) owners = InstalledOwners.ToList();
                foreach (InstalledOwner owner in owners)
                {
                    if (!normalized.StartsWith(owner.Root, StringComparison.OrdinalIgnoreCase)) continue;
                    evidence.AddPublisher(owner.Publisher).AddProduct(owner.ProductName);
                }
            }
        }

        private static void EnsureInstalledOwners()
        {
            lock (IdentityCacheGate)
            {
                if (InstalledOwnersLoaded) return;
            }
            List<InstalledOwner> loaded = new List<InstalledOwner>();
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (RegistryView view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (root == null) continue;
                            foreach (string childName in root.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey child = root.OpenSubKey(childName))
                                    {
                                        if (child == null) continue;
                                        string product = Convert.ToString(child.GetValue("DisplayName", ""));
                                        string publisher = Convert.ToString(child.GetValue("Publisher", ""));
                                        string installLocation = Convert.ToString(child.GetValue("InstallLocation", ""));
                                        string displayIcon = Convert.ToString(child.GetValue("DisplayIcon", ""));
                                        string ownerRoot = NormalizeInstallRoot(installLocation, displayIcon);
                                        if (string.IsNullOrEmpty(ownerRoot) || (string.IsNullOrWhiteSpace(product) && string.IsNullOrWhiteSpace(publisher))) continue;
                                        loaded.Add(new InstalledOwner { Root = ownerRoot, Publisher = publisher, ProductName = product });
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            lock (IdentityCacheGate)
            {
                if (InstalledOwnersLoaded) return;
                foreach (InstalledOwner owner in loaded.OrderByDescending(delegate(InstalledOwner item) { return item.Root.Length; }))
                {
                    if (!InstalledOwners.Any(delegate(InstalledOwner item) { return item.Root.Equals(owner.Root, StringComparison.OrdinalIgnoreCase) && item.ProductName.Equals(owner.ProductName, StringComparison.OrdinalIgnoreCase); }))
                        InstalledOwners.Add(owner);
                }
                InstalledOwnersLoaded = true;
            }
        }

        private static string NormalizeInstallRoot(string installLocation, string displayIcon)
        {
            string value = installLocation;
            if (string.IsNullOrWhiteSpace(value))
            {
                string icon = NormalizeCandidateFile(displayIcon);
                if (!string.IsNullOrEmpty(icon)) value = Path.GetDirectoryName(icon);
            }
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('\"'))).TrimEnd('\\') + "\\"; }
            catch { return string.Empty; }
        }

        private static string ExtractTargetFile(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            string first = ExtractFirstPath(expanded);
            string host = SafePathFileName(first);
            if (!SystemHosts.Contains(host)) return first;
            if (host.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) || host.Equals("regsvr32.exe", StringComparison.OrdinalIgnoreCase))
            {
                string remainder = expanded.Substring(Math.Min(expanded.Length, expanded.IndexOf(first, StringComparison.OrdinalIgnoreCase) + first.Length)).Trim().TrimStart(',');
                string target = ExtractFirstPath(remainder);
                int comma = target.IndexOf(',');
                return comma > 0 ? target.Substring(0, comma) : target;
            }
            return string.Empty;
        }

        private static string ExtractFirstPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = value.IndexOf('\"', 1);
                if (close > 1) return value.Substring(1, close - 1);
            }
            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) return value.Substring(0, exe + 4).Trim().Trim('\"');
            int dll = value.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
            if (dll >= 0) return value.Substring(0, dll + 4).Trim().Trim('\"');
            int comma = value.IndexOf(',');
            if (comma > 0) return value.Substring(0, comma).Trim().Trim('\"');
            return value.Trim('\"');
        }

        private static string NormalizeCandidateFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string path = ExtractFirstPath(Environment.ExpandEnvironmentVariables(value));
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                if (Directory.Exists(path)) return string.Empty;
                return Path.GetFullPath(path);
            }
            catch { return string.Empty; }
        }

        private static string SafePathFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try { return Path.GetFileName(value.Trim().Trim('"')); }
            catch { return string.Empty; }
        }

        private static string ExtractProductCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            int start = value.IndexOf('{');
            while (start >= 0)
            {
                int end = value.IndexOf('}', start + 1);
                if (end < 0) return string.Empty;
                string candidate = value.Substring(start, end - start + 1);
                Guid parsed;
                if (Guid.TryParse(candidate, out parsed)) return parsed.ToString("B").ToUpperInvariant();
                start = value.IndexOf('{', end + 1);
            }
            Guid direct;
            return Guid.TryParse(value.Trim(), out direct) ? direct.ToString("B").ToUpperInvariant() : string.Empty;
        }

        private static FileIdentity GetFileIdentity(string value)
        {
            string path = NormalizeCandidateFile(value);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            lock (IdentityCacheGate)
            {
                FileIdentity cached;
                if (FileIdentityCache.TryGetValue(path, out cached)) return cached;
            }
            FileIdentity identity = new FileIdentity { Path = path };
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                identity.Company = version.CompanyName;
                identity.Product = version.ProductName;
                identity.Description = version.FileDescription;
            }
            catch { }
            identity.SignatureValid = IsTrustedFile(path);
            if (identity.SignatureValid)
            {
                try
                {
                    // SYSLIB0057 无功能等价替代：X509CertificateLoader 不能从可执行文件提取 Authenticode 签名证书，故保留旧 API
#pragma warning disable SYSLIB0057
                    using (X509Certificate2 certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    {
                        identity.Signer = certificate.Subject;
                    }
#pragma warning restore SYSLIB0057
                }
                catch { identity.SignatureValid = false; }
            }
            lock (IdentityCacheGate) FileIdentityCache[path] = identity;
            return identity;
        }

        private static bool IsTrustedFile(string path)
        {
            IntPtr filePointer = IntPtr.Zero;
            IntPtr dataPointer = IntPtr.Zero;
            try
            {
                WinTrustFileInfo file = new WinTrustFileInfo
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    FilePath = path,
                    FileHandle = IntPtr.Zero,
                    KnownSubject = IntPtr.Zero
                };
                filePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(file, filePointer, false);
                WinTrustData data = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = 2,
                    RevocationChecks = 0,
                    UnionChoice = 1,
                    FileInfo = filePointer,
                    StateAction = 0,
                    ProviderFlags = 0x00001000,
                    UiContext = 0
                };
                dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData)));
                Marshal.StructureToPtr(data, dataPointer, false);
                return WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, dataPointer) == 0;
            }
            catch { return false; }
            finally
            {
                if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
                if (filePointer != IntPtr.Zero) Marshal.FreeHGlobal(filePointer);
            }
        }

        private static MsiIdentity GetMsiIdentity(string productCode)
        {
            lock (IdentityCacheGate)
            {
                MsiIdentity cached;
                if (MsiIdentityCache.TryGetValue(productCode, out cached)) return cached;
            }
            MsiIdentity identity = new MsiIdentity
            {
                ProductName = MsiProperty(productCode, "ProductName"),
                Publisher = MsiProperty(productCode, "Publisher"),
                InstallLocation = MsiProperty(productCode, "InstallLocation"),
                LocalPackage = MsiProperty(productCode, "LocalPackage")
            };
            lock (IdentityCacheGate) MsiIdentityCache[productCode] = identity;
            return identity;
        }

        private static string MsiProperty(string productCode, string property)
        {
            try
            {
                int length = 0;
                uint first = MsiGetProductInfo(productCode, property, null, ref length);
                if (first != 0 && first != 234) return string.Empty;
                StringBuilder value = new StringBuilder(length + 1);
                uint result = MsiGetProductInfo(productCode, property, value, ref length);
                return result == 0 ? value.ToString() : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static VendorIdentityResult UnknownIdentity(bool conflicted, string reason)
        {
            return new VendorIdentityResult { Vendor = "未知第三方", Confidence = 0, Confirmed = false, Conflicted = conflicted, EvidenceSummary = reason };
        }

        public static List<string> RunIdentitySelfTests()
        {
            List<string> failures = new List<string>();
            AssertUnknown(failures, "Corel GUID 不得命中 2345", new VendorEvidence()
                .AddHuman("CorelDRAW Graphics Suite 2021 - IPM Content BR (x64)")
                .AddPublisher("Corel Corporation").AddMsi("{3D6825D1-5843-4585-B915-A9F234554C2C}")
                .AddOpaque(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{3D6825D1-5843-4585-B915-A9F234554C2C}"));
            AssertUnknown(failures, "裸 2345 不能确认厂商", new VendorEvidence().AddHuman("2345").AddTechnical("A9F234554C2C"));
            AssertUnknown(failures, "系统 MSI 宿主不能成为厂商证据", new VendorEvidence()
                .AddCommand(@"C:\Windows\System32\msiexec.exe /I{3D6825D1-5843-4585-B915-A9F234554C2C}")
                .AddOpaque("{3D6825D1-5843-4585-B915-A9F234554C2C}"));
            AssertUnknown(failures, "Thunderbird 不能命中迅雷", new VendorEvidence()
                .AddHuman("Mozilla Thunderbird").AddTechnical("Thunderbird").AddFile(@"C:\Program Files\Mozilla Thunderbird\thunderbird.exe"));
            AssertUnknown(failures, "普通 TBS/XMP/KAV/CAJ 缩写不能确认厂商", new VendorEvidence()
                .AddHuman("TBS XMP KAV CAJ").AddTechnical("TBS_XMP_KAV_CAJ"));

            VendorIdentityResult sogou = ResolveIdentity(new VendorEvidence().AddHuman("搜狗输入法").AddPublisher("Sogou.com").AddProduct("Sogou Input Method"));
            if (!sogou.Confirmed || sogou.Vendor != "搜狗") failures.Add("明确 Publisher+产品名未识别为搜狗");
            VendorIdentityResult sogouPopup = ResolveIdentity(new VendorEvidence().AddHuman("CodexRogueCleanerTest_SogouInputPop").AddCommand(@"C:\CodexRogueCleanerTest\Sogou\SogouInputPop.exe"));
            if (!sogouPopup.Confirmed || sogouPopup.Vendor != "搜狗") failures.Add("搜狗弹窗组件被通用行为标签阻断厂商识别");
            AssertUnknown(failures, "通用弹窗行为不能冒充厂商", new VendorEvidence().AddHuman("HotNews").AddCommand(@"C:\Unknown\HotNews.exe"));

            VendorIdentityResult conflict = ResolveIdentity(new VendorEvidence()
                .AddPublisher("Sogou.com", "Thunder Network Technologies")
                .AddProduct("Sogou Input Method", "Xunlei Thunder Download"));
            if (!conflict.Conflicted || conflict.Confirmed) failures.Add("相互冲突的强组合证据未被阻断");

            foreach (VendorRule rule in Vendors)
            {
                foreach (string pattern in rule.Patterns)
                {
                    VendorIdentityResult opaque = ResolveIdentity(new VendorEvidence()
                        .AddOpaque("GUID-{A9F" + pattern + "55C2C}", @"HKLM\Software\Classes\" + pattern));
                    if (opaque.Confirmed) failures.Add("不透明字段误命中：" + rule.Name + " / " + pattern);

                    VendorIdentityResult pathOnly = ResolveIdentity(new VendorEvidence()
                        .AddFile(@"C:\Unrelated\A9F" + pattern + @"55C2C\tool.exe"));
                    if (pathOnly.Confirmed) failures.Add("单一路径片段误命中：" + rule.Name + " / " + pattern);
                }
            }
            return failures;
        }

        private static void AssertUnknown(List<string> failures, string name, VendorEvidence evidence)
        {
            VendorIdentityResult result = ResolveIdentity(evidence);
            if (result.Confirmed || result.Vendor != "未知第三方") failures.Add(name + "：实际为 " + result.Vendor + "，" + result.EvidenceSummary);
        }

        public static string ResolveVendor(string text)
        {
            return ResolveIdentity(new VendorEvidence().AddHuman(text)).Vendor;
        }

        public static int VendorBoost(string text)
        {
            VendorIdentityResult identity = ResolveIdentity(new VendorEvidence().AddHuman(text));
            return VendorBoost(identity, false);
        }

        public static bool IsKnownVendor(string text)
        {
            return ResolveIdentity(new VendorEvidence().AddHuman(text)).Confirmed;
        }

        public static bool HasBadComponent(string text)
        {
            VendorEvidence evidence = new VendorEvidence().AddHuman(text).AddTechnical(text);
            VendorIdentityResult identity = ResolveIdentity(evidence);
            return HasBadComponent(evidence, identity);
        }
    }

}
