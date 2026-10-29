using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

/// <summary>
/// 硬件"品牌戏称"彩蛋：把硬件厂商/型号字符串替换成玩家圈戏称（打人硕、牙膏厂…）。
/// 由硬件信息页点击标题触发的「彩蛋模式」手动启用，不涉及任何自动加载。
/// </summary>
public static class BrandNicknameService
{
    private static readonly (string pattern, string replacement)[] NicknameRules =
    [
        (@"华硕\(ASUS\)", "打人硕"),
        (@"ASUS|ASUSTEK", "打人硕"),
        (@"微星\(MSI\)", "军规星"),
        (@"MSI|MICRO[\s\-]?STAR", "军规星"),
        (@"技嘉\(Gigabyte\)", "拒保嘉"),
        (@"GIGABYTE", "拒保嘉"),
        (@"华擎\(ASRock\)", "妖板擎"),
        (@"ASROCK", "妖板擎"),
        (@"七彩虹\(Colorful\)", "凄惨红"),
        (@"COLORFUL", "凄惨红"),
        (@"铭瑄\(Maxsun\)", "丐帮瑄"),
        (@"MAXSUN", "丐帮瑄"),
        (@"盈通\(Yeston\)", "花姑娘通"),
        (@"YESTON", "花姑娘通"),
        (@"影驰\(Galax\)", "花驰"),
        (@"GALAX|GALAXY", "花驰"),
        (@"映泰\(Biostar\)", "映泰(不泰)"),
        (@"BIOSTAR", "映泰(不泰)"),
        (@"梅捷\(Soyo\)", "没捷"),
        (@"SOYO", "没捷"),
        (@"昂达\(Onda\)", "昂达(不达)"),
        (@"ONDA", "昂达(不达)"),
        (@"富士康\(Foxconn\)", "血汗工厂康"),
        (@"FOXCONN", "血汗工厂康"),
        (@"英特尔\(Intel\)", "牙膏厂"),
        (@"INTEL", "牙膏厂"),
        (@"超微\(Supermicro\)", "超微(不微)"),
        (@"SUPERMICRO", "超微(不微)"),
        (@"戴尔\(Dell\)", "人傻钱多戴"),
        (@"DELL", "人傻钱多戴"),
        (@"惠普\(HP\)", "铁板烧普"),
        (@"\bHP\b", "铁板烧普"),
        (@"联想\(Lenovo\)", "美帝良心想"),
        (@"LENOVO", "美帝良心想"),
        (@"宏碁\(Acer\)", "宏碁(不碁)"),
        (@"\bACER\b", "宏碁(不碁)"),
        (@"三星\(Samsung\)", "星巴克"),
        (@"SAMSUNG", "星巴克"),
        (@"苹果\(Apple\)", "水果厂"),
        (@"\bAPPLE\b", "水果厂"),
        (@"华为\(Huawei\)", "菊花厂"),
        (@"HUAWEI", "菊花厂"),
        (@"小米\(Xiaomi\)", "粗粮厂"),
        (@"XIAOMI", "粗粮厂"),
        (@"荣耀", "不知道什么耀"),
        (@"HONOR", "不知道什么耀"),
        (@"金士顿\(Kingston\)", "金士顿(假士顿)"),
        (@"KINGSTON", "金士顿(假士顿)"),
        (@"海盗船\(Corsair\)", "贼船"),
        (@"CORSAIR", "贼船"),
        (@"英睿达\(Crucial\)", "英睿达(不达)"),
        (@"CRUCIAL", "英睿达(不达)"),
        (@"海力士\(SK Hynix\)", "海力士(不力)"),
        (@"HYNIX|SK\s*HYNIX", "海力士(不力)"),
        (@"美光\(Micron\)", "美光(不光)"),
        (@"MICRON", "美光(不光)"),
        (@"威刚\(ADATA\)", "威刚(不刚)"),
        (@"\bADATA\b", "威刚(不刚)"),
        (@"芝奇\(G\.Skill\)", "芝奇(不奇)"),
        (@"G[\.\s]?SKILL", "芝奇(不奇)"),
        (@"十铨\(TeamGroup\)", "十铨(不铨)"),
        (@"TEAM\s*GROUP", "十铨(不铨)"),
        (@"\bEVGA\b", "EVGay"),
        (@"\bNZXT\b", "恩杰(不杰)"),
        (@"京东方\(BOE\)", "京东方(不方)"),
        (@"\bBOE\b", "京东方(不方)"),
        (@"友达\(AU Optronics\)", "友达(不达)"),
        (@"AU\s*OPTRONICS", "友达(不达)"),
        (@"飞利浦\(Philips\)", "飞利浦(不浦)"),
        (@"PHILIPS", "飞利浦(不浦)"),
        (@"优派\(ViewSonic\)", "优派(不派)"),
        (@"VIEWSONIC", "优派(不派)"),
        (@"夏普\(Sharp\)", "夏普(不普)"),
        (@"\bSHARP\b", "夏普(不普)"),
        (@"东芝\(Toshiba\)", "东芝(不芝)"),
        (@"TOSHIBA", "东芝(不芝)"),
        (@"索尼\(Sony\)", "大法"),
        (@"\bSONY\b", "大法"),
        (@"\bAMD\b", "农企"),
        (@"NVIDIA|GEFORCE|RTX|GTX", "老黄家"),
        (@"RADEON", "农企"),
        (@"QUALCOMM", "高通(不高)"),
        (@"SNAPDRAGON", "火龙"),
        (@"ADRENO", "阿德瑞诺"),
    ];

    public static string ApplyNickname(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        foreach (var (pattern, replacement) in NicknameRules)
        {
            try
            {
                if (Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase))
                    value = Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase);
            }
            catch { }
        }

        return value;
    }
}
