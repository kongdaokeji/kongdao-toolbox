using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Panel = System.Windows.Forms.Panel;
using ReaLTaiizor.Controls;
using TubaWinUi3.Compatible.Models;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible.Forms
{
    /// <summary>工具列表页：自绘卡片流（图标/名称/描述/架构徽章/多分类 chips）+ Crown 滚动条 + 架构切换菜单。</summary>
    public class ToolListPage : UserControl
    {
        private TubaScrollView _scrollView;
        private Panel _contentLayer;
        private readonly List<ToolCard> _cards = new List<ToolCard>();
        private Label _statusLabel;
        private Panel _statusBar;
        private Label _emptyLabel;
        private string _category;
        private IReadOnlyList<ToolItem> _tools;
        private bool _dark = true;

        const int CARD_W = 208;
        const int CARD_H = 108;
        const int GAP = 8;
        const int MARGIN = 12;

        public ToolListPage()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = ThemeService.Colors.Background;

            _statusBar = new Panel();
            _statusBar.Height = 26;
            _statusBar.Dock = DockStyle.Bottom;
            Controls.Add(_statusBar);

            _statusLabel = new Label();
            _statusLabel.Font = ThemeService.UiFont(8.5f);
            _statusLabel.ForeColor = ThemeService.Colors.TextMuted;
            _statusLabel.BackColor = Color.Transparent;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Padding = new Padding(12, 0, 0, 0);
            _statusBar.Controls.Add(_statusLabel);

            _scrollView = new TubaScrollView();
            _scrollView.Dock = DockStyle.Fill;
            _scrollView.BackColor = ThemeService.Colors.Background;
            _scrollView.MouseWheel += HandleWheel;
            Controls.Add(_scrollView);

            _contentLayer = new Panel();
            _contentLayer.BackColor = ThemeService.Colors.Background;
            _scrollView.Controls.Add(_contentLayer);

            _emptyLabel = new Label();
            _emptyLabel.Text = "未找到工具";
            _emptyLabel.Font = ThemeService.UiFont(11f);
            _emptyLabel.ForeColor = ThemeService.Colors.TextMuted;
            _emptyLabel.BackColor = Color.Transparent;
            _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            _emptyLabel.Visible = false;
            Controls.Add(_emptyLabel);
        }

        private void HandleWheel(object sender, MouseEventArgs e)
        {
            var vp = _scrollView.Viewport;
            int step = e.Delta > 0 ? -48 : 48;   // +120 上滚 / -120 下滚，每格 48px
            int maxY = Math.Max(0, _contentLayer.Height - _scrollView.Height);
            int y = Math.Max(0, Math.Min(vp.Y + step, maxY));
            _scrollView.VScrollTo(y);
            SyncContent();
        }

        /// <summary>把内容层位置与视图滚动偏移对齐（滚动条拖动 / VScrollTo 均触发）。</summary>
        private void SyncContent()
        {
            if (_contentLayer == null) return;
            _contentLayer.Top = -_scrollView.Viewport.Y;
        }

        public void SetCategory(string cat)
        {
            _category = cat;
            LoadTools();
        }

        private void LoadTools()
        {
            _cards.Clear();
            _contentLayer.Controls.Clear();

            if (_category == null)
                _tools = ToolCatalog.GetAllToolsDeduped();
            else
                _tools = ToolCatalog.GetTools(_category);

            _emptyLabel.Visible = _tools == null || _tools.Count == 0;
            _statusLabel.Text = "  " + (_tools == null || _tools.Count == 0
                ? "未找到工具"
                : ("共 " + _tools.Count + " 个工具" + (_category != null ? "  ·  " + _category : "")));

            if (_tools == null || _tools.Count == 0)
            {
                LayoutLayer();
                return;
            }

            ToolIconService.LoadIcons(_tools);
            foreach (var t in _tools)
            {
                var card = new ToolCard(t) { Parent = _contentLayer };
                card.LaunchRequested += Launch;
                card.MouseWheel += HandleWheel;
                _cards.Add(card);
            }

            LayoutLayer();
        }

        private void LayoutLayer()
        {
            int viewW = Math.Max(_scrollView.ClientSize.Width, 200);
            int x = MARGIN;
            int y = MARGIN;
            foreach (var card in _cards)
            {
                if (x + CARD_W > viewW - MARGIN + 4)
                {
                    x = MARGIN;
                    y += CARD_H + GAP;
                }
                card.SetBounds(x, y, CARD_W, CARD_H);
                x += CARD_W + GAP;
            }
            int totalH = Math.Max(y + CARD_H + MARGIN, _scrollView.ClientSize.Height);
            _contentLayer.SetBounds(0, 0, viewW, totalH);
            _contentLayer.Top = 0;
            _scrollView.ContentSize = new Size(viewW, totalH);
            _scrollView.VScrollTo(0);
            SyncContent();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_contentLayer != null) LayoutLayer();
            _emptyLabel.SetBounds(0, (Height - 60) / 2, Width, 40);
            ApplyTheme(_dark);
        }

        private void Launch(ToolItem tool)
        {
            var path = tool.EffectivePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                CrownMessageBox.ShowInformation("该工具没有可启动的文件。", "无法启动", ReaLTaiizor.Enum.Crown.DialogButton.Ok);
                return;
            }

            if (!File.Exists(path))
            {
                if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
                {
                    CrownMessageBox.ShowInformation(
                        "文件不存在，可在完整版中下载：\n" + tool.DownloadUrl,
                        "无法启动", ReaLTaiizor.Enum.Crown.DialogButton.Ok);
                    return;
                }
                CrownMessageBox.ShowWarning("文件不存在: " + path, "无法启动", ReaLTaiizor.Enum.Crown.DialogButton.Ok);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = tool.EffectiveWorkingDir };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                CrownMessageBox.ShowError("启动失败: " + ex.Message, "错误", ReaLTaiizor.Enum.Crown.DialogButton.Ok);
            }
        }

        public void Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) { LoadTools(); return; }
            _cards.Clear();
            _contentLayer.Controls.Clear();
            var r = ToolCatalog.Search(q);
            _tools = r;
            _emptyLabel.Visible = r == null || r.Count == 0;
            _emptyLabel.Text = "搜索无结果";
            _statusLabel.Text = r == null || r.Count == 0 ? "  搜索无结果" : "  搜索结果: " + r.Count + " 个工具";
            if (r == null || r.Count == 0)
            {
                LayoutLayer();
                return;
            }
            ToolIconService.LoadIcons(r);
            foreach (var t in r)
            {
                var card = new ToolCard(t) { Parent = _contentLayer };
                card.LaunchRequested += Launch;
                card.MouseWheel += HandleWheel;
                _cards.Add(card);
            }
            LayoutLayer();
        }

        public void ApplyTheme(bool dark)
        {
            _dark = dark;
            var c = ThemeService.Colors;
            BackColor = c.Background;
            _scrollView.BackColor = c.Background;
            _contentLayer.BackColor = c.Background;
            _statusBar.BackColor = c.Surface;
            _statusLabel.ForeColor = c.TextMuted;
            _emptyLabel.ForeColor = c.TextMuted;
            foreach (var card in _cards)
                card.Invalidate();
        }

        /// <summary>工具卡片：图标 + 名称 + 描述 + 架构徽章（可点击切换）+ 多分类 chips + 启动按钮。</summary>
        private sealed class ToolCard : Control
        {
            public ToolItem Tool { get; private set; }
            public event Action<ToolItem> LaunchRequested;

            private bool _hover;
            private bool _archHover;
            private bool _btnHover;
            private Image _iconImage;
            private readonly ToolTip _tip;

            public ToolCard(ToolItem tool)
            {
                Tool = tool;
                Text = tool.Name;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                Cursor = Cursors.Hand;
                BackColor = Color.Transparent;
                ForeColor = Color.Transparent;

                var tipText = tool.Name;
                if (!string.IsNullOrEmpty(tool.Description)) tipText += "\n" + tool.Description;
                if (tool.HasAlternateVersions) tipText += "\n点击架构徽章可切换 x64 / ARM64 版本";
                _tip = new ToolTip();
                _tip.SetToolTip(this, tipText);

                if (!string.IsNullOrEmpty(tool.IconPath) && File.Exists(tool.IconPath))
                {
                    try
                    {
                        using (var img = Image.FromFile(tool.IconPath))
                            _iconImage = new Bitmap(img, new Size(28, 28));
                    }
                    catch { }
                }
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _archHover = false; _btnHover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool arch = BadgeBounds.Contains(e.Location);
                bool btn = LaunchButtonBounds().Contains(e.Location);
                if (arch != _archHover || btn != _btnHover)
                {
                    _archHover = arch;
                    _btnHover = btn;
                    Invalidate();
                }
                base.OnMouseMove(e);
            }
            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                var p = PointToClient(Cursor.Position);
                if (BadgeBounds.Contains(p))
                {
                    ShowArchMenu();
                    return;
                }
                if (LaunchButtonBounds().Contains(p))
                {
                    var h = LaunchRequested;
                    if (h != null) h(Tool);
                    return;
                }
                var handler = LaunchRequested;
                if (handler != null) handler(Tool);
            }

            private Rectangle IconBounds { get { return new Rectangle(12, 12, 32, 32); } }

            private Rectangle BadgeBounds { get { return new Rectangle(12, 72, ArchBadgeWidth(), 20); } }

            private Rectangle LaunchButtonBounds()
            {
                return new Rectangle(Width - 58, 70, 46, 24);
            }

            private string ArchText
            {
                get
                {
                    var arch = Tool.SelectedArch != null ? Tool.SelectedArch.Arch : Tool.PrimaryArch;
                    return string.IsNullOrEmpty(arch) ? "默认" : arch;
                }
            }

            private int ArchBadgeWidth()
            {
                using (var f = ThemeService.UiFont(8f))
                using (var g = CreateGraphics())
                {
                    var txt = ArchText;
                    var w = (int)g.MeasureString(txt, f).Width + 18;
                    return Math.Max(40, w);
                }
            }

            private bool HasArchMenu
            {
                get { return Tool.ArchOptions != null && Tool.ArchOptions.Count > 1; }
            }

            private void ShowArchMenu()
            {
                using (var menu = new CrownContextMenuStrip())
                {
                    if (HasArchMenu)
                    {
                        foreach (var opt in Tool.ArchOptions)
                        {
                            var item = (ToolStripMenuItem)menu.Items.Add(opt.DisplayText + (opt.Arch.Length > 0 ? "  ·  " + opt.Name : ""));
                            item.Checked = Tool.SelectedArch != null &&
                                string.Equals(Tool.SelectedArch.Path, opt.Path, StringComparison.OrdinalIgnoreCase);
                            var capture = opt;
                            item.Click += (s, e) =>
                            {
                                Tool.SelectedArch = capture;
                                Invalidate();
                            };
                        }
                        menu.Items.Add(new ToolStripSeparator());
                    }
                    var openFolder = menu.Items.Add("打开所在文件夹");
                    openFolder.Click += (s, e) =>
                    {
                        var dir = Path.GetDirectoryName(Tool.EffectivePath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            try { Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
                        }
                    };
                    var copyPath = menu.Items.Add("复制路径");
                    copyPath.Click += (s, e) =>
                    {
                        try { Clipboard.SetText(Tool.EffectivePath); } catch { }
                    };
                    menu.Show(this, BadgeBounds.X, BadgeBounds.Bottom);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var c = ThemeService.Colors;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                var bg = _hover ? c.SurfaceHover : c.Surface;
                var border = _hover ? c.Accent : c.Border;

                using (var path = MainForm.RoundedRect(rect, 8))
                {
                    using (var b = new SolidBrush(bg))
                        g.FillPath(b, path);
                    using (var p = new Pen(border, _hover ? 1.2f : 1f))
                        g.DrawPath(p, path);
                }

                // 图标
                var iconRect = IconBounds;
                if (_iconImage != null)
                {
                    g.DrawImageUnscaled(_iconImage, iconRect.X + 2, iconRect.Y + 2);
                }
                else
                {
                    using (var tile = MainForm.RoundedRect(new Rectangle(iconRect.X, iconRect.Y, 32, 32), 6))
                    using (var tb = new SolidBrush(c.AccentSoft))
                        g.FillPath(tb, tile);
                    var glyph = !string.IsNullOrEmpty(Tool.IconGlyph)
                        ? Tool.IconGlyph
                        : (Tool.Extension == "待下载" ? "\uE896" : extGlyph(Tool.Extension));
                    using (var f = new Font("Segoe MDL2 Assets", 14f))
                    using (var fb = new SolidBrush(HasArchMenu ? c.Accent : c.TextSecondary))
                    {
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        var sz = g.MeasureString(glyph, f);
                        g.DrawString(glyph, f, fb, iconRect.X + (32 - sz.Width) / 2f, iconRect.Y + (32 - sz.Height) / 2f);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    }
                }

                // 名称
                using (var f = ThemeService.UiFont(9.5f, bold: true))
                using (var b = new SolidBrush(c.TextPrimary))
                {
                    var text = Tool.Name;
                    var maxW = Width - 56;
                    if (g.MeasureString(text, f).Width > maxW)
                    {
                        while (text.Length > 1 && g.MeasureString(text + "…", f).Width > maxW)
                            text = text.Substring(0, text.Length - 1);
                        text += "…";
                    }
                    g.DrawString(text, f, b, 54, 10);
                }

                // 描述
                using (var f = ThemeService.UiFont(8f))
                using (var b = new SolidBrush(c.TextMuted))
                {
                    var desc = !string.IsNullOrEmpty(Tool.Description) ? Tool.Description : Tool.Extension;
                    var maxW = Width - 24;
                    if (g.MeasureString(desc, f).Width > maxW)
                    {
                        while (desc.Length > 1 && g.MeasureString(desc + "…", f).Width > maxW)
                            desc = desc.Substring(0, desc.Length - 1);
                        desc += "…";
                    }
                    g.DrawString(desc, f, b, 12, 46);
                }

                // 多分类 chips（全部工具视图）
                int chipX = 0;
                if (Tool.Categories != null && Tool.Categories.Count > 1)
                {
                    var cats = Tool.Categories;
                    int maxChips = 2;
                    int endX = Width - 68;
                    chipX = 12;
                    for (int i = 0; i < cats.Count && i < maxChips; i++)
                    {
                        var label = cats[i];
                        if (i == maxChips - 1 && cats.Count > maxChips)
                            label = "+" + (cats.Count - maxChips);
                        using (var f = ThemeService.UiFont(7.5f))
                        {
                            var w = (int)g.MeasureString(label, f).Width + 12;
                            if (chipX + w > endX) break;
                            var chipRect = new Rectangle(chipX, 74, w, 16);
                            using (var path = MainForm.RoundedRect(chipRect, 8))
                            using (var b = new SolidBrush(c.SurfaceActive))
                                g.FillPath(b, path);
                            using (var b = new SolidBrush(c.TextMuted))
                                g.DrawString(label, f, b, chipRect.X + 6, chipRect.Y + 1);
                            chipX += w + 4;
                        }
                    }
                }

                // 架构徽章（可点击时显示箭头提示）
                var badgeRect = BadgeBounds;
                var badgeBg = _archHover ? c.AccentSoft : c.SurfaceActive;
                using (var path = MainForm.RoundedRect(badgeRect, 10))
                using (var b = new SolidBrush(badgeBg))
                    g.FillPath(b, path);
                using (var f = ThemeService.UiFont(8f, bold: true))
                using (var b = new SolidBrush(c.Accent))
                {
                    var txt = ArchText;
                    var tw = g.MeasureString(txt, f).Width;
                    g.DrawString(txt, f, b, badgeRect.X + (badgeRect.Width - tw) / 2f, badgeRect.Y + 4);
                }

                // 启动按钮
                var btnRect = LaunchButtonBounds();
                var btnBg = _btnHover ? c.AccentHover : c.Accent;
                using (var path = MainForm.RoundedRect(btnRect, 6))
                using (var b = new SolidBrush(btnBg))
                    g.FillPath(b, path);
                using (var f = ThemeService.UiFont(8.5f, bold: true))
                using (var b = new SolidBrush(Color.White))
                {
                    var t = Tool.LaunchButtonText;
                    var tw = g.MeasureString(t, f).Width;
                    g.DrawString(t, f, b, btnRect.X + (btnRect.Width - tw) / 2f, btnRect.Y + 5);
                }

                // 多架构提示角标
                if (HasArchMenu && !_archHover)
                {
                    using (var f = new Font("Segoe MDL2 Assets", 6.5f))
                    using (var b = new SolidBrush(c.TextMuted))
                        g.DrawString("\uE70D", f, b, badgeRect.Right - 12, badgeRect.Y + 7);
                }
            }

            private static string extGlyph(string ext)
            {
                switch (ext.ToLowerInvariant())
                {
                    case "bat":
                    case "cmd": return "\uE756";
                    case "ps1":
                    case "vbs": return "\uE943";
                    case "msc": return "\uEC7A";
                    default: return "\uE8B7";
                }
            }
        }
    }
}