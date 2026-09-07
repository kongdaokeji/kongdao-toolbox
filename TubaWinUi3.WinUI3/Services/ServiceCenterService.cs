namespace TubaWinUi3.Services;

public sealed class ServiceCenterBrand
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ServiceUrl { get; init; } = "";
    public string? LogoUrl { get; init; }
    public bool HasLaptop { get; init; }
    public bool HasDesktop { get; init; }
    public bool HasAccessory { get; init; }
}

public static class ServiceCenterService
{
    public static IReadOnlyList<ServiceCenterBrand> GetAllBrands() => _brands;

    public static IReadOnlyList<ServiceCenterBrand> GetLaptopBrands()
        => _brands.Where(b => b.HasLaptop).ToList();

    public static IReadOnlyList<ServiceCenterBrand> GetDesktopBrands()
        => _brands.Where(b => b.HasDesktop).ToList();

    public static IReadOnlyList<ServiceCenterBrand> GetAccessoryBrands()
        => _brands.Where(b => b.HasAccessory).ToList();

    private static readonly List<ServiceCenterBrand> _brands =
    [
        // 笔记本+台式机品牌（同时有笔记本和台式机产品线）
        new ServiceCenterBrand
        {
            Id = "lenovo",
            Name = "联想 Lenovo",
            ServiceUrl = "https://newsupport.lenovo.com.cn/",
            LogoUrl = "https://www.lenovo.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "huawei",
            Name = "华为 HUAWEI",
            ServiceUrl = "https://consumer.huawei.com/cn/support/service-center/",
            LogoUrl = "https://consumer.huawei.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "honor",
            Name = "荣耀 HONOR",
            ServiceUrl = "https://www.honor.com/cn/support/",
            LogoUrl = "https://www.honor.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "xiaomi",
            Name = "小米 Xiaomi",
            ServiceUrl = "https://www.mi.com/service",
            LogoUrl = "https://www.mi.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "asus",
            Name = "华硕 ASUS",
            ServiceUrl = "https://www.asus.com.cn/support/",
            LogoUrl = "https://www.asus.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "dell",
            Name = "戴尔 Dell",
            ServiceUrl = "https://www.dell.com/support/diagnose/zh-cn/servicecenter",
            LogoUrl = "https://www.dell.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "hp",
            Name = "惠普 HP",
            ServiceUrl = "https://support.hp.com/cn-zh",
            LogoUrl = "https://support.hp.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "acer",
            Name = "宏碁 Acer",
            ServiceUrl = "https://www.acer.com.cn/myhelp.html?type=1&serverid=15",
            LogoUrl = "https://www.acer.com.cn/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "msi",
            Name = "微星 MSI",
            ServiceUrl = "https://www.msi.cn/support",
            LogoUrl = "https://www.msi.cn/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "apple",
            Name = "苹果 Apple",
            ServiceUrl = "https://locate.apple.com/cn/zh/",
            LogoUrl = "https://www.apple.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "samsung",
            Name = "三星 Samsung",
            ServiceUrl = "https://www.samsung.com.cn/support/repair-service/",
            LogoUrl = "https://www.samsung.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "thtf",
            Name = "清华同方",
            ServiceUrl = "http://csm.thtfpc.com.cn/",
            LogoUrl = "http://www.thtf.com.cn/favicon.ico",
            HasLaptop = true,
            HasDesktop = true,
            HasAccessory = false
        },

        // 仅笔记本品牌
        new ServiceCenterBrand
        {
            Id = "mechrevo",
            Name = "机械革命",
            ServiceUrl = "https://www.mechrevo.com/cn/outlets",
            LogoUrl = "https://www.mechrevo.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "machenike",
            Name = "机械师",
            ServiceUrl = "https://www.machenike.com/offline/afterservice",
            LogoUrl = "https://www.machenike.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "thunderobot",
            Name = "雷神",
            ServiceUrl = "https://www.thunderobot.com/service_station",
            LogoUrl = "https://www.thunderobot.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "hasee",
            Name = "神舟 Hasee",
            ServiceUrl = "http://www.hasee.com/after/serve_branch",
            LogoUrl = "http://www.hasee.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "terransforce",
            Name = "未来人类",
            ServiceUrl = "https://www.terransforce.com/",
            LogoUrl = "https://www.terransforce.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "wukong",
            Name = "吾空",
            ServiceUrl = "http://www.wooking.com.cn/about/services",
            LogoUrl = "http://www.wooking.com.cn/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "vaio",
            Name = "VAIO",
            ServiceUrl = "http://www.vaio-china.com/support/",
            LogoUrl = "http://www.vaio-china.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "lg",
            Name = "LG gram",
            ServiceUrl = "https://www.lg.com/cn/support/locate-repair-center",
            LogoUrl = "https://www.lg.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "surface",
            Name = "Microsoft Surface",
            ServiceUrl = "https://support.microsoft.com/zh-cn/surface/hardware-warranty/",
            LogoUrl = "https://www.microsoft.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "razer",
            Name = "雷蛇 Razer",
            ServiceUrl = "https://mysupport.razer.com/",
            LogoUrl = "https://www.razer.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },
        new ServiceCenterBrand
        {
            Id = "alienware",
            Name = "Alienware 外星人",
            ServiceUrl = "https://www.dell.com/support/diagnose/zh-cn/servicecenter",
            LogoUrl = "https://www.dell.com/favicon.ico",
            HasLaptop = true,
            HasDesktop = false,
            HasAccessory = false
        },

        // 配件/外设品牌
        new ServiceCenterBrand
        {
            Id = "intel",
            Name = "Intel 英特尔",
            ServiceUrl = "https://www.intel.cn/content/www/cn/zh/support/detect.html",
            LogoUrl = "https://www.intel.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "amd",
            Name = "AMD 超威",
            ServiceUrl = "https://www.amd.com/zh-cn/support/download/drivers.html",
            LogoUrl = "https://www.amd.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "nvidia",
            Name = "NVIDIA 英伟达",
            ServiceUrl = "https://www.nvidia.cn/support/",
            LogoUrl = "https://www.nvidia.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "kingston",
            Name = "Kingston 金士顿",
            ServiceUrl = "https://www.kingston.com/cn/support/china/rma",
            LogoUrl = "https://www.kingston.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "wd",
            Name = "WD 西部数据",
            ServiceUrl = "https://support-cn.wd.com/",
            LogoUrl = "https://www.westerndigital.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "seagate",
            Name = "Seagate 希捷",
            ServiceUrl = "https://www.seagate.com/cn/zh/support/",
            LogoUrl = "https://www.seagate.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "crucial",
            Name = "Crucial 英睿达",
            ServiceUrl = "https://www.crucial.cn/support",
            LogoUrl = "https://www.crucial.cn/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "corsair",
            Name = "Corsair 美商海盗船",
            ServiceUrl = "https://help.corsair.com/hc/zh-cn",
            LogoUrl = "https://www.corsair.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "gskill",
            Name = "G.Skill 芝奇",
            ServiceUrl = "https://www.gskill.com/cn/techsupport",
            LogoUrl = "https://www.gskill.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "logitech",
            Name = "Logitech 罗技",
            ServiceUrl = "https://support.logi.com/hc/zh-cn",
            LogoUrl = "https://www.logitech.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "steelseries",
            Name = "SteelSeries 赛睿",
            ServiceUrl = "https://steelseries.com/zh-cn/gg/engine/download",
            LogoUrl = "https://steelseries.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "nzxt",
            Name = "NZXT 恩杰",
            ServiceUrl = "https://nzxt.com/zh-hans-intl/pages/support",
            LogoUrl = "https://nzxt.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "coolermaster",
            Name = "Cooler Master 酷冷至尊",
            ServiceUrl = "https://www.coolermaster.com.cn/",
            LogoUrl = "https://www.coolermaster.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "bequiet",
            Name = "be quiet! 德商必酷",
            ServiceUrl = "https://www.bequiet.com/cn/",
            LogoUrl = "https://www.bequiet.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "seasonic",
            Name = "Seasonic 海韵",
            ServiceUrl = "https://seasonic.com/zh/consumer-product-support-or-sales/",
            LogoUrl = "https://seasonic.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "aoc",
            Name = "AOC 冠捷",
            ServiceUrl = "https://aocmonitor.com.cn/varranty",
            LogoUrl = "https://www.aocmonitor.com.cn/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "benq",
            Name = "BenQ 明基",
            ServiceUrl = "https://www.benq.com.cn/zh-cn/support.html",
            LogoUrl = "https://www.benq.com.cn/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "viewsonic",
            Name = "ViewSonic 优派",
            ServiceUrl = "https://www.viewsonic.com.cn/support/",
            LogoUrl = "https://www.viewsonic.com.cn/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        },
        new ServiceCenterBrand
        {
            Id = "creative",
            Name = "Creative 创新",
            ServiceUrl = "https://cn.creative.com/help/",
            LogoUrl = "https://cn.creative.com/favicon.ico",
            HasLaptop = false,
            HasDesktop = false,
            HasAccessory = true
        }
    ];
}