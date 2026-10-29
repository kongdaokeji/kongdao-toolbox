using System;
using System.Drawing;

namespace TubaWinUi3.Compatible.Services
{
    /// <summary>界面调色板（深/浅两套），驱动所有自绘控件的颜色；Crown 控件提供窗口边框/滚动条/菜单/对话框。</summary>
    public sealed class Palette
    {
        public Color Background;        // 页面底色
        public Color Surface;           // 卡片/侧栏表面
        public Color SurfaceHover;      // 悬浮提亮
        public Color SurfaceActive;     // 选中状态
        public Color SurfaceSubtle;     // 列表交替行弱底
        public Color Border;            // 细分隔线/边框
        public Color BorderStrong;      // 悬停卡片边框
        public Color TextPrimary;       // 主文字
        public Color TextSecondary;     // 次要文字
        public Color TextMuted;         // 弱化文字
        public Color Accent;            // 强调色
        public Color AccentHover;       // 强调色悬浮
        public Color AccentSoft;        // 强调色淡底（选中导航）
        public Color Success;           // 已验证/成功
        public Color Danger;            // 错误/待下载
        public Color Chrome;            // 顶栏/窗口底色

        public static Palette Dark = new Palette
        {
            Background = Color.FromArgb(21, 22, 25),
            Surface = Color.FromArgb(27, 28, 33),
            SurfaceHover = Color.FromArgb(33, 34, 41),
            SurfaceActive = Color.FromArgb(38, 40, 48),
            SurfaceSubtle = Color.FromArgb(30, 31, 37),
            Border = Color.FromArgb(47, 49, 57),
            BorderStrong = Color.FromArgb(76, 141, 255),
            TextPrimary = Color.FromArgb(232, 233, 237),
            TextSecondary = Color.FromArgb(160, 162, 170),
            TextMuted = Color.FromArgb(110, 112, 120),
            Accent = Color.FromArgb(76, 141, 255),
            AccentHover = Color.FromArgb(120, 170, 255),
            AccentSoft = Color.FromArgb(38, 52, 82),
            Success = Color.FromArgb(58, 199, 140),
            Danger = Color.FromArgb(235, 95, 98),
            Chrome = Color.FromArgb(24, 25, 29)
        };

        public static Palette Light = new Palette
        {
            Background = Color.FromArgb(244, 245, 248),
            Surface = Color.FromArgb(255, 255, 255),
            SurfaceHover = Color.FromArgb(240, 242, 247),
            SurfaceActive = Color.FromArgb(233, 238, 248),
            SurfaceSubtle = Color.FromArgb(248, 249, 251),
            Border = Color.FromArgb(228, 230, 236),
            BorderStrong = Color.FromArgb(59, 111, 216),
            TextPrimary = Color.FromArgb(28, 30, 36),
            TextSecondary = Color.FromArgb(96, 99, 108),
            TextMuted = Color.FromArgb(140, 143, 152),
            Accent = Color.FromArgb(59, 111, 216),
            AccentHover = Color.FromArgb(40, 88, 186),
            AccentSoft = Color.FromArgb(230, 237, 250),
            Success = Color.FromArgb(34, 160, 106),
            Danger = Color.FromArgb(210, 70, 74),
            Chrome = Color.FromArgb(30, 31, 36)
        };
    }

    public static class ThemeService
    {
        public static event Action ThemeChanged;

        private static bool _dark = true;

        public static bool IsDark { get { return _dark; } }

        public static Palette Colors { get { return _dark ? Palette.Dark : Palette.Light; } }

        public static void Init()
        {
            _dark = !AppSettings.GetBool("ThemeLight", false);
        }

        public static void Toggle()
        {
            SetDark(!_dark);
        }

        public static void SetDark(bool dark)
        {
            if (_dark == dark) return;
            _dark = dark;
            try { AppSettings.Set("ThemeLight", !dark); } catch { }
            var handler = ThemeChanged;
            if (handler != null) handler();
        }

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular, bool bold = false)
        {
            if (bold) style = FontStyle.Bold;
            return new Font("Microsoft YaHei UI", size, style);
        }

        public static Color BrandColor(string brandKey)
        {
            if (brandKey == null) return Colors.Accent;
            switch (brandKey)
            {
                case "intel": return Color.FromArgb(0, 130, 210);
                case "amd": return Color.FromArgb(220, 50, 50);
                case "nvidia": return Color.FromArgb(118, 185, 0);
                case "apple": return Color.FromArgb(160, 160, 160);
                case "qualcomm": return Color.FromArgb(90, 90, 220);
                default: return Colors.Accent;
            }
        }
    }
}