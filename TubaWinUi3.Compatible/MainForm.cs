using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;
using Panel = System.Windows.Forms.Panel;
using ReaLTaiizor.Controls;
using ReaLTaiizor.Forms;
using TubaWinUi3.Compatible.Forms;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible
{
    /// <summary>主窗体：Crown 窗口边框 + 顶栏（搜索/主题）+ 侧栏导航 + 内容区。</summary>
    public class MainForm : CrownForm
    {
        private Panel _topBar;
        private Panel _sidebar;
        private Panel _content;
        private Label _titleLabel;
        private CrownTextBox _searchBox;
        private Label _searchPlaceholder;
        private CrownButton _themeButton;
        private Panel _navHost;
        private readonly List<NavItem> _navs = new List<NavItem>();
        private ToolListPage _toolListPage;
        private HardwarePage _hardwarePage;
        private int _selectedIdx = -1;

        const int TOP_H = 56;
        const int SIDE_W = 208;

        public MainForm()
        {
            Text = "图吧工具箱 · 兼容版";
            Size = new Size(1200, 800);
            MinimumSize = new Size(920, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Palette.Dark.Background;

            ThemeService.Init();
            ThemeService.ThemeChanged += OnThemeChanged;

            InitUI();
        }

        private void InitUI()
        {
            // === 顶栏 ===
            _topBar = new Panel();
            _topBar.BackColor = ThemeService.Colors.Chrome;
            Controls.Add(_topBar);

            var logoDot = new Label();
            logoDot.Text = "●";
            logoDot.ForeColor = ThemeService.Colors.Accent;
            logoDot.Font = ThemeService.UiFont(7f);
            logoDot.BackColor = Color.Transparent;
            logoDot.TextAlign = ContentAlignment.MiddleLeft;
            logoDot.Name = "logoDot";
            _topBar.Controls.Add(logoDot);

            _titleLabel = new Label();
            _titleLabel.Text = "图吧工具箱";
            _titleLabel.Font = ThemeService.UiFont(12.5f, bold: true);
            _titleLabel.ForeColor = ThemeService.Colors.TextPrimary;
            _titleLabel.BackColor = Color.Transparent;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _topBar.Controls.Add(_titleLabel);

            var subLabel = new Label();
            subLabel.Text = "兼容版";
            subLabel.Font = ThemeService.UiFont(8.5f);
            subLabel.ForeColor = ThemeService.Colors.TextMuted;
            subLabel.BackColor = Color.Transparent;
            subLabel.TextAlign = ContentAlignment.BottomLeft;
            subLabel.Padding = new Padding(0, 0, 0, 11);
            subLabel.Name = "subLabel";
            _topBar.Controls.Add(subLabel);

            _searchBox = new CrownTextBox();
            _searchBox.TextChanged += (s, e) =>
            {
                _searchPlaceholder.Visible = _searchBox.Text.Length == 0;
                if (_toolListPage != null)
                {
                    _toolListPage.Search(_searchBox.Text.Trim());
                    if (_selectedIdx != 0) SelectNav(0);
                }
            };
            _searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    _searchBox.Text = "";
                    e.SuppressKeyPress = true;
                }
            };
            _topBar.Controls.Add(_searchBox);

            _searchPlaceholder = new Label();
            _searchPlaceholder.Text = "  搜索工具…";
            _searchPlaceholder.Font = ThemeService.UiFont(9f);
            _searchPlaceholder.ForeColor = ThemeService.Colors.TextMuted;
            _searchPlaceholder.BackColor = Color.Transparent;
            _searchPlaceholder.Name = "searchPlaceholder";
            _searchBox.Controls.Add(_searchPlaceholder);

            _themeButton = new CrownButton();
            _themeButton.Text = ThemeService.IsDark ? "浅色" : "深色";
            _themeButton.ButtonStyle = ReaLTaiizor.Enum.Crown.ButtonStyle.Flat;
            _themeButton.Cursor = Cursors.Hand;
            _themeButton.Click += (s, e) => ThemeService.Toggle();
            _topBar.Controls.Add(_themeButton);

            // === 侧栏 ===
            _sidebar = new Panel();
            _sidebar.BackColor = ThemeService.Colors.Surface;
            Controls.Add(_sidebar);

            _navHost = new Panel();
            _navHost.BackColor = _sidebar.BackColor;
            _sidebar.Controls.Add(_navHost);

            AddNav("全部工具", 0, "\uE71D");
            AddNav("硬件信息", 1, "\uE770");

            // 分类分隔标题
            var sep = new Label();
            sep.Text = "工具分类";
            sep.Font = ThemeService.UiFont(8f);
            sep.ForeColor = ThemeService.Colors.TextMuted;
            sep.Size = new Size(SIDE_W - 16, 30);
            sep.TextAlign = ContentAlignment.MiddleLeft;
            sep.Padding = new Padding(16, 0, 0, 0);
            sep.BackColor = Color.Transparent;
            sep.Name = "catSep";
            _navHost.Controls.Add(sep);

            int idx = 2;
            foreach (var cat in ToolCatalog.GetCategories())
                AddNav(cat, idx++, GlyphForCategory(cat));

            // === 内容区 ===
            _content = new Panel();
            _content.BackColor = ThemeService.Colors.Background;
            Controls.Add(_content);

            _toolListPage = new ToolListPage();
            _hardwarePage = new HardwarePage();

            DoLayout();
            ApplyThemeToChrome();
            SelectNav(0);
        }

        private static string GlyphForCategory(string category)
        {
            switch (category)
            {
                case "显卡工具": return "\uE950";
                case "处理器工具": return "\uE756";
                case "显示器工具": return "\uE7F4";
                case "主板工具": return "\uE9D9";
                case "内存工具": return "\uE8BD";
                case "硬盘工具": return "\uE8B7";
                case "烤鸡工具": return "\uE91A";
                case "其他工具": return "\uE8EF";
                default: return "\uE8EF";
            }
        }

        private NavItem AddNav(string text, int idx, string glyph = "\uE8EF")
        {
            var item = new NavItem { Text = text, Index = idx };
            item.WithIcon(glyph);
            item.Clicked += (s, e) => SelectNav(idx);
            _navHost.Controls.Add(item);
            _navs.Add(item);
            return item;
        }

        private void SelectNav(int idx)
        {
            if (_selectedIdx != idx)
            {
                foreach (var nav in _navs)
                    nav.Selected = nav.Index == idx;
            }
            _selectedIdx = idx;

            _content.Controls.Clear();
            if (idx == 1)
            {
                _hardwarePage.EnsureLoaded();
                _content.Controls.Add(_hardwarePage);
            }
            else
            {
                string category = null;
                if (idx >= 2)
                {
                    var cats = ToolCatalog.GetCategories();
                    var catNavs = _navs.FindAll(n => n.Index >= 2);
                    int ci = idx - 2;
                    if (ci < catNavs.Count)
                    {
                        foreach (var c in cats)
                            if (catNavs[ci].Text == c) { category = c; break; }
                    }
                }
                _toolListPage.SetCategory(category);
                _content.Controls.Add(_toolListPage);
            }

            DoLayout();
        }

        private void OnThemeChanged()
        {
            ApplyThemeToChrome();
            if (_toolListPage != null) _toolListPage.ApplyTheme(ThemeService.IsDark);
            if (_hardwarePage != null) _hardwarePage.ApplyTheme(ThemeService.IsDark);
        }

        private void ApplyThemeToChrome()
        {
            var c = ThemeService.Colors;
            BackColor = c.Background;
            _topBar.BackColor = c.Chrome;
            _titleLabel.ForeColor = c.TextPrimary;
            _sidebar.BackColor = c.Surface;
            _navHost.BackColor = c.Surface;
            _content.BackColor = c.Background;
            _themeButton.Text = ThemeService.IsDark ? "浅色" : "深色";
            foreach (var l in _topBar.Controls.OfType<Label>())
            {
                switch (l.Name)
                {
                    case "logoDot": l.ForeColor = c.Accent; break;
                    case "subLabel": l.ForeColor = c.TextMuted; break;
                }
            }
            foreach (var l in _navHost.Controls.OfType<Label>())
            {
                if (l.Name == "catSep") l.ForeColor = c.TextMuted;
            }
        }

        private void DoLayout()
        {
            if (_topBar == null || _content == null) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            _topBar.SetBounds(0, 0, w, TOP_H);
            _titleLabel.SetBounds(18, 0, 150, TOP_H);
            foreach (var l in _topBar.Controls.OfType<Label>())
            {
                if (l.Name == "logoDot") l.SetBounds(10, 20, 10, 14);
                else if (l.Name == "subLabel") l.SetBounds(124, 0, 48, TOP_H);
            }
            _themeButton.SetBounds(w - 92, 14, 72, 28);
            _searchBox.SetBounds(w - 372, 13, 268, 30);
            _searchPlaceholder.SetBounds(10, 7, 240, 16);

            _sidebar.SetBounds(0, TOP_H, SIDE_W, h - TOP_H);
            _navHost.SetBounds(0, 0, SIDE_W, h - TOP_H);

            // 导航项纵向排布：全部工具/硬件信息固定，分隔标题，其后为分类
            int top = 8;
            foreach (var nav in _navs)
            {
                if (nav.Index == 2)
                {
                    foreach (var l in _navHost.Controls.OfType<Label>())
                        if (l.Name == "catSep") l.SetBounds(0, top, SIDE_W, 30);
                    top += 30;
                }
                nav.SetBounds(8, top, SIDE_W - 16, 36);
                top += 36;
            }

            int cx = SIDE_W;
            int cy = TOP_H;
            int cw = w - SIDE_W;
            int ch = h - TOP_H;
            _content.SetBounds(cx, cy, cw, ch);

            foreach (Control c in _content.Controls)
                c.SetBounds(0, 0, cw, ch);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _searchBox.Focus();
        }

        /// <summary>侧栏导航项：悬浮提亮、选中强调条 + 淡色底。</summary>
        private sealed class NavItem : Control
        {
            private bool _hover;
            private bool _selected;
            private string _glyph = "\uE8EF";

            public int Index { get; set; }
            public event EventHandler Clicked;

            public string Glyph { get { return _glyph; } }

            public void WithIcon(string glyph) { _glyph = glyph; }

            public bool Selected
            {
                get { return _selected; }
                set { _selected = value; Invalidate(); }
            }

            public NavItem()
            {
                Font = ThemeService.UiFont(9.5f);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                var handler = Clicked;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var c = ThemeService.Colors;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var bg = _selected ? c.AccentSoft : (_hover ? c.SurfaceHover : c.Surface);
                using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 6))
                using (var brush = new SolidBrush(bg))
                    g.FillPath(brush, path);

                if (_selected)
                {
                    using (var bar = new SolidBrush(c.Accent))
                    using (var barPath = RoundedRect(new Rectangle(2, 8, 3, Height - 16), 2))
                        g.FillPath(bar, barPath);
                }

                var fg = _selected ? c.Accent : (_hover ? c.TextPrimary : c.TextSecondary);
                using (var fIcon = new Font("Segoe MDL2 Assets", 10f))
                using (var fText = new Font(Font.FontFamily, 9.5f, _selected ? FontStyle.Bold : FontStyle.Regular))
                using (var bIcon = new SolidBrush(_selected ? c.Accent : c.TextMuted))
                using (var bText = new SolidBrush(fg))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString(_glyph, fIcon, bIcon, 11, (Height - 16) / 2f);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    g.DrawString(Text, fText, bText, 34, (Height - 15) / 2f);
                }
            }
        }

        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}