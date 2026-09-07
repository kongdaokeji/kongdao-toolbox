using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Panel = System.Windows.Forms.Panel;
using ReaLTaiizor.Controls;
using TubaWinUi3.Compatible.Models;
using TubaWinUi3.Compatible.Services;

namespace TubaWinUi3.Compatible.Forms
{
    /// <summary>
    /// 硬件信息页（对齐 WinUI3 版布局）：
    /// 头部（标题/副标题 + 刷新/截图）→ 三个指标卡（型号信息/系统信息/运行时间）→ 详细信息卡片列表，
    /// 点击复制 + 右下角 toast 提示 + 实时运行时间。
    /// </summary>
    public class HardwarePage : UserControl
    {
        private TubaScrollView _scrollView;
        private Panel _contentLayer;
        private Label _loadingLabel;
        private CrownButton _refreshButton;
        private CrownButton _screenshotButton;
        private MetricCard _modelCard;
        private MetricCard _systemCard;
        private MetricCard _uptimeCard;
        private Panel _detailsHost;
        private Toast _toast;
        private System.Windows.Forms.Timer _uptimeTimer;
        private System.Windows.Forms.Timer _refreshCooldown;
        private IReadOnlyList<HardwareInfoSection> _sections;
        private bool _loaded;
        private bool _loading;

        const int PAD = 24;
        const int HEADER_H = 58;

        public HardwarePage()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = ThemeService.Colors.Background;

            _toast = new Toast();
            _toast.Visible = false;
            Controls.Add(_toast);

            _loadingLabel = new Label();
            _loadingLabel.Text = "正在读取硬件信息…";
            _loadingLabel.Font = ThemeService.UiFont(11f);
            _loadingLabel.ForeColor = ThemeService.Colors.TextMuted;
            _loadingLabel.BackColor = Color.Transparent;
            _loadingLabel.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(_loadingLabel);

            _scrollView = new TubaScrollView();
            _scrollView.Dock = DockStyle.Fill;
            _scrollView.BackColor = ThemeService.Colors.Background;
            _scrollView.MouseWheel += HandleWheel;
            Controls.Add(_scrollView);

            _contentLayer = new Panel();
            _contentLayer.BackColor = ThemeService.Colors.Background;
            _scrollView.Controls.Add(_contentLayer);

            _refreshCooldown = new System.Windows.Forms.Timer();
            _refreshCooldown.Interval = 900;
            _refreshCooldown.Tick += (s, e) =>
            {
                _refreshCooldown.Stop();
                if (_refreshButton != null) _refreshButton.Enabled = true;
            };

            _uptimeTimer = new System.Windows.Forms.Timer();
            _uptimeTimer.Interval = 1000;
            _uptimeTimer.Tick += (s, e) => UpdateUptime();
        }

        private void HandleWheel(object sender, MouseEventArgs e)
        {
            var vp = _scrollView.Viewport;
            int step = e.Delta > 0 ? -48 : 48;
            int maxY = Math.Max(0, _contentLayer.Height - _scrollView.Height);
            int y = Math.Max(0, Math.Min(vp.Y + step, maxY));
            _scrollView.VScrollTo(y);
            SyncContent();
        }

        private void SyncContent()
        {
            if (_contentLayer == null) return;
            _contentLayer.Top = -_scrollView.Viewport.Y;
        }

        public void Reload()
        {
            _sections = null;
            _loaded = false;
            EnsureLoaded();
        }

        public void EnsureLoaded()
        {
            if (_loaded || _loading) return;
            _loading = true;

            var bw = new BackgroundWorker();
            bw.DoWork += (s, e) => { e.Result = HardwareInfoService.LoadAsync(); };
            bw.RunWorkerCompleted += (s, e) =>
            {
                _loading = false;
                if (e.Error != null)
                {
                    ShowError("硬件信息读取失败", e.Error.Message);
                    return;
                }
                _sections = e.Result as IReadOnlyList<HardwareInfoSection>;
                _loaded = true;
                BuildUI();
            };
            bw.RunWorkerAsync();
        }

        private void ShowError(string title, string message)
        {
            _loadingLabel.Text = title + ": " + message;
            _loadingLabel.Visible = true;
            _toast.ShowToast(title, message, ToastKind.Error);
        }

        private static string SectionValue(IReadOnlyList<HardwareInfoSection> sections, int sectionIdx, string label)
        {
            if (sections == null || sections.Count <= sectionIdx) return "未知";
            foreach (var item in sections[sectionIdx].Items)
            {
                if (item.Label == label)
                    return string.IsNullOrWhiteSpace(item.Value) ? "未知" : item.Value;
            }
            return "未知";
        }

        private void BuildUI()
        {
            Controls.Clear();
            Controls.Add(_toast);
            Controls.Add(_loadingLabel);
            Controls.Add(_scrollView);
            _contentLayer.Controls.Clear();

            if (_sections == null || _sections.Count < 3)
            {
                _scrollView.Visible = false;
                _loadingLabel.Text = "未获取到硬件信息";
                _loadingLabel.Visible = true;
                return;
            }

            _scrollView.Visible = true;
            _loadingLabel.Visible = false;

            var c = ThemeService.Colors;
            int w = Math.Max(Width - PAD * 2, 700);
            int x = PAD;

            // === 头部：标题 + 副标题 + 按钮 ===
            var title = new Label();
            title.Text = "硬件信息";
            title.Font = ThemeService.UiFont(17f, bold: true);
            title.ForeColor = c.TextPrimary;
            title.BackColor = Color.Transparent;
            title.AutoSize = true;
            _contentLayer.Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "汇总本机型号、系统和关键硬件参数。";
            subtitle.Font = ThemeService.UiFont(9f);
            subtitle.ForeColor = c.TextSecondary;
            subtitle.BackColor = Color.Transparent;
            subtitle.AutoSize = true;
            _contentLayer.Controls.Add(subtitle);

            _refreshButton = new CrownButton();
            _refreshButton.Text = "刷新";
            _refreshButton.ButtonStyle = ReaLTaiizor.Enum.Crown.ButtonStyle.Flat;
            _refreshButton.Cursor = Cursors.Hand;
            _refreshButton.Click += (s, e) => Reload();
            _contentLayer.Controls.Add(_refreshButton);

            _screenshotButton = new CrownButton();
            _screenshotButton.Text = "截图";
            _screenshotButton.ButtonStyle = ReaLTaiizor.Enum.Crown.ButtonStyle.Flat;
            _screenshotButton.Cursor = Cursors.Hand;
            _screenshotButton.Click += (s, e) => Screenshot();
            _contentLayer.Controls.Add(_screenshotButton);

            title.SetBounds(x, 6, title.Width, 30);
            subtitle.SetBounds(x, 40, 400, 20);
            _screenshotButton.SetBounds(x + w - 164, 14, 76, 28);
            _refreshButton.SetBounds(x + w - 82, 14, 70, 28);

            // === 三个指标卡 ===
            _mTop = 78;
            int gapX = 14;
            int cardW = (w - gapX * 2) / 3;
            int cardH = 98;
            _modelCard = new MetricCard("型号信息", "\uE772") { Parent = _contentLayer };
            _modelCard.Clicked += (s, e) => CopyValue(_modelCard.Value);
            _systemCard = new MetricCard("系统信息", "\uE770") { Parent = _contentLayer };
            _systemCard.Clicked += (s, e) => CopyValue(_systemCard.Value);
            _uptimeCard = new MetricCard("运行时间", "\uE917") { Parent = _contentLayer };
            _uptimeCard.Clicked += (s, e) => CopyValue(_uptimeCard.Value);

            _modelCard.SetBounds(x, _mTop, cardW, cardH);
            _systemCard.SetBounds(x + cardW + gapX, _mTop, cardW, cardH);
            _uptimeCard.SetBounds(x + cardW * 2 + gapX * 2, _mTop, cardW, cardH);

            // === 详细信息 ===
            int detailsTop = _mTop + cardH + 18;
            var detailsTitle = new Label();
            detailsTitle.Text = "详细信息";
            detailsTitle.Font = ThemeService.UiFont(14f, bold: true);
            detailsTitle.ForeColor = c.TextPrimary;
            detailsTitle.BackColor = Color.Transparent;
            _contentLayer.Controls.Add(detailsTitle);

            _detailsHost = new Panel();
            _detailsHost.BackColor = c.Surface;
            _contentLayer.Controls.Add(_detailsHost);
            int detailsTitleTop = detailsTop + 2;
            detailsTitle.SetBounds(x, detailsTitleTop, 200, 26);

            _rowsTop = detailsTitleTop + 34;
            FillDetails(_rowsTop, x, w);

            int totalH = Math.Max(_rowsTop + rowCount * ROW_H + 36, _scrollView.Height);
            _contentLayer.SetBounds(0, 0, Width, totalH);
            _contentLayer.Top = 0;
            _scrollView.ContentSize = new Size(Width, totalH);
            _scrollView.VScrollTo(0);
            SyncContent();

            _uptimeTimer.Start();
            UpdateCardValues();
        }

        private int rowCount;
        private int _mTop;
        private int _rowsTop;

        private const int ROW_H = 46;

        private void FillDetails(int top, int x, int w)
        {
            _detailsHost.Controls.Clear();
            var details = _sections[2].Items;
            rowCount = details.Count;

            int y = 0;
            int idx = 0;
            foreach (var item in details)
            {
                var row = new DetailRow(item, idx) { Parent = _detailsHost };
                row.Clicked += (s, e) => CopyValue(item.Value);
                row.MouseWheel += HandleWheel;
                row.SetBounds(0, y, w, ROW_H);
                y += ROW_H;
                idx++;
            }
            _detailsHost.SetBounds(x, top, w, y + 8);
        }

        private void UpdateCardValues()
        {
            if (_modelCard == null || _sections == null) return;
            _modelCard.Value = SectionValue(_sections, 0, "设备型号");
            _systemCard.Value = SectionValue(_sections, 1, "系统");
            UpdateUptime();
        }

        private void UpdateUptime()
        {
            if (_uptimeCard == null) return;
            var uptime = TimeSpan.FromMilliseconds((uint)Environment.TickCount);
            _uptimeCard.Value = uptime.Days + "天" + uptime.Hours + "小时" + uptime.Minutes + "分钟" + uptime.Seconds + "秒";
        }

        private void CopyValue(string value)
        {
            try { Clipboard.SetText(value ?? ""); } catch { }
            _toast.ShowToast("已复制", value ?? "");
        }

        private void Screenshot()
        {
            try
            {
                using (var bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));

                    // 右下角水印（对齐 WinUI3 版截图样式）
                    using (var g = Graphics.FromImage(bmp))
                    {
                        const string watermark = "图吧工具箱";
                        using (var font = ThemeService.UiFont(9f))
                        {
                            var size = g.MeasureString(watermark, font);
                            float barW = size.Width + 20;
                            float barH = size.Height + 10;
                            float barX = Width - barW - 16;
                            float barY = Height - barH - 14;
                            using (var path = MainForm.RoundedRect(
                                new Rectangle((int)barX, (int)barY, (int)barW, (int)barH), 6))
                            using (var b = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                                g.FillPath(b, path);
                            using (var b = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                                g.DrawString(watermark, font, b, barX + 10, barY + 5);
                        }
                    }
                    Clipboard.SetImage(bmp);
                }
                _toast.ShowToast("截图已复制到剪贴板", "可直接粘贴使用", ToastKind.Success);
            }
            catch (Exception ex)
            {
                _toast.ShowToast("截图失败", ex.Message, ToastKind.Error);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_contentLayer == null) return;
            _loadingLabel.SetBounds(0, (Height - 40) / 2, Width, 40);
            _toast.SetBounds(Width - 330 - 16, Height - 66 - 16, 330, 66);
            if (_loaded && _modelCard != null && Height > 0)
            {
                int w = Math.Max(Width - PAD * 2, 700);
                int x = PAD;
                int gapX = 14;
                int cardW = (w - gapX * 2) / 3;
                int cardH = 98;
                _modelCard.SetBounds(x, _mTop, cardW, cardH);
                _systemCard.SetBounds(x + cardW + gapX, _mTop, cardW, cardH);
                _uptimeCard.SetBounds(x + cardW * 2 + gapX * 2, _mTop, cardW, cardH);
                if (_detailsHost != null && _detailsHost.Parent == _contentLayer)
                    _detailsHost.SetBounds(_detailsHost.Left, _detailsHost.Top, w, _detailsHost.Height);
                foreach (Control child in _detailsHost.Controls)
                    child.Width = w;
                if (_refreshButton != null)
                {
                    _refreshButton.SetBounds(x + w - 82, 14, 70, 28);
                    _screenshotButton.SetBounds(x + w - 164, 14, 76, 28);
                }
            }
        }

        public void ApplyTheme(bool dark)
        {
            var c = ThemeService.Colors;
            BackColor = c.Background;
            _scrollView.BackColor = c.Background;
            _contentLayer.BackColor = c.Background;
            _loadingLabel.ForeColor = c.TextMuted;
            if (_detailsHost != null)
            {
                _detailsHost.BackColor = c.Surface;
                foreach (Control child in _detailsHost.Controls)
                {
                    if (child is DetailRow)
                        child.Invalidate();
                }
            }
            if (_modelCard != null) { _modelCard.Invalidate(); _systemCard.Invalidate(); _uptimeCard.Invalidate(); }
        }

        /// <summary>指标卡：48x48 图标块 + 标签 + 加粗数值（可换行），点击复制。</summary>
        private sealed class MetricCard : Control
        {
            private readonly string _labelText;
            private readonly string _glyph;
            private bool _hover;
            private string _value;

            public string Value
            {
                get { return _value; }
                set
                {
                    _value = value ?? "未知";
                    Text = _labelText + ": " + _value;
                    Invalidate();
                }
            }

            public event EventHandler Clicked;

            public MetricCard(string label, string glyph)
            {
                _labelText = label;
                _glyph = glyph;
                _value = "未知";
                Text = label + ": " + _value;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                Cursor = Cursors.Hand;
                BackColor = Color.Transparent;
                var tt = new ToolTip();
                tt.SetToolTip(this, "点击复制");
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                var h = Clicked;
                if (h != null) h(this, EventArgs.Empty);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var c = ThemeService.Colors;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = MainForm.RoundedRect(rect, 8))
                {
                    using (var b = new SolidBrush(_hover ? c.SurfaceHover : c.Surface))
                        g.FillPath(b, path);
                    using (var p = new Pen(_hover ? c.Accent : c.Border, 1f))
                        g.DrawPath(p, path);
                }

                // 图标块
                using (var tile = MainForm.RoundedRect(new Rectangle(16, (Height - 48) / 2, 48, 48), 8))
                using (var b = new SolidBrush(c.SurfaceActive))
                    g.FillPath(b, tile);
                using (var f = new Font("Segoe MDL2 Assets", 20f))
                using (var b = new SolidBrush(c.Accent))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    var sz = g.MeasureString(_glyph, f);
                    g.DrawString(_glyph, f, b, 16 + (48 - sz.Width) / 2f, (Height - 48) / 2 + (48 - sz.Height) / 2f);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                }

                // 标签
                using (var f = ThemeService.UiFont(9f))
                using (var b = new SolidBrush(c.TextSecondary))
                    g.DrawString(_labelText, f, b, 80, 14);

                // 数值（自动换行）
                var layout = new RectangleF(80, 36, Width - 96, Height - 40);
                using (var f = ThemeService.UiFont(10.5f, bold: true))
                using (var b = new SolidBrush(c.TextPrimary))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                {
                    if (g.MeasureString(Value, f).Width > layout.Width)
                    {
                        using (var fSmall = ThemeService.UiFont(9.5f, bold: true))
                            g.DrawString(Value, fSmall, b, layout, sf);
                    }
                    else
                    {
                        g.DrawString(Value, f, b, layout, sf);
                    }
                }
            }
        }

        /// <summary>详细信息行：标签 | 分隔线 | 加粗值 | 「真」徽章 | 品牌色块，点击复制，交替行弱底。</summary>
        private sealed class DetailRow : Control
        {
            private readonly HardwareInfoItem _item;
            private readonly int _index;
            private bool _hover;
            public event EventHandler Clicked;

            public DetailRow(HardwareInfoItem item, int index)
            {
                _item = item;
                _index = index;
                Text = item.Label + ": " + item.Value;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                Cursor = Cursors.Hand;
                BackColor = Color.Transparent;
                var tt = new ToolTip();
                tt.SetToolTip(this, "点击复制");
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                var h = Clicked;
                if (h != null) h(this, EventArgs.Empty);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var c = ThemeService.Colors;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                if (_hover)
                {
                    using (var b = new SolidBrush(c.SurfaceHover))
                        g.FillRectangle(b, 0, 0, Width, Height);
                }
                else if (_index % 2 == 1)
                {
                    using (var b = new SolidBrush(c.SurfaceSubtle))
                        g.FillRectangle(b, 0, 0, Width, Height);
                }

                int padX = 16;

                // 标签（宽 90）
                using (var f = ThemeService.UiFont(9f))
                using (var b = new SolidBrush(c.TextSecondary))
                    g.DrawString(_item.Label ?? "", f, b, padX, (Height - 17) / 2f);

                // 分隔线
                using (var b = new SolidBrush(c.Border))
                    g.FillRectangle(b, padX + 92, (Height - 16) / 2f, 1, 16);

                // 品牌色块（右端，替代 SVG logo）
                int rightPad = 20;
                bool hasBrand = !string.IsNullOrEmpty(_item.BrandKey);
                if (_item.IsVerified) rightPad += 58;
                if (hasBrand) rightPad += 28;

                var valueRect = new RectangleF(padX + 104, 0, Width - padX - 104 - rightPad, Height);
                using (var f = ThemeService.UiFont(9.5f, bold: true))
                using (var b = new SolidBrush(c.TextPrimary))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(_item.Value ?? "", f, b, valueRect, sf);

                if (hasBrand)
                {
                    var tileRect = new Rectangle(Width - 24, (Height - 20) / 2, 20, 20);
                    using (var path = MainForm.RoundedRect(tileRect, 5))
                    using (var b = new SolidBrush(ThemeService.BrandColor(_item.BrandKey)))
                        g.FillPath(b, path);
                }

                if (_item.IsVerified)
                {
                    // 「真」徽章（对齐 WinUI3 绿色 #00C864）
                    var badgeRect = new Rectangle(Width - (hasBrand ? 56 : 22) - 42, (Height - 20) / 2, 42, 20);
                    using (var path = MainForm.RoundedRect(badgeRect, 4))
                    using (var b = new SolidBrush(Color.FromArgb(38, 0, 200, 100)))
                        g.FillPath(b, path);
                    using (var f = ThemeService.UiFont(8f, bold: true))
                    using (var b = new SolidBrush(Color.FromArgb(255, 0, 200, 100)))
                        g.DrawString("真", f, b, badgeRect.X + 15, badgeRect.Y + 3);
                }
            }
        }

        private enum ToastKind { Info, Success, Error }

        /// <summary>右下角 toast 提示（对齐 WinUI3 InfoBar 样式）。</summary>
        private sealed class Toast : Control
        {
            private string _title = "";
            private string _message = "";
            private ToastKind _kind = ToastKind.Info;
            private readonly System.Windows.Forms.Timer _timer;

            public Toast()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Size = new Size(330, 66);
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 3800;
                _timer.Tick += (s, e) =>
                {
                    _timer.Stop();
                    Visible = false;
                };
            }

            public void ShowToast(string title, string message, ToastKind kind = ToastKind.Info)
            {
                _title = title;
                _message = message != null && message.Length > 96 ? message.Substring(0, 96) + "…" : (message ?? "");
                _kind = kind;
                Visible = true;
                BringToFront();
                Invalidate();
                _timer.Stop();
                _timer.Start();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var c = ThemeService.Colors;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = MainForm.RoundedRect(rect, 8))
                {
                    using (var b = new SolidBrush(Color.FromArgb(245, c.SurfaceActive.R, c.SurfaceActive.G, c.SurfaceActive.B)))
                        g.FillPath(b, path);
                    using (var p = new Pen(c.Border, 1f))
                        g.DrawPath(p, path);
                }

                // 状态色条
                var accent = _kind == ToastKind.Error ? c.Danger : (_kind == ToastKind.Success ? c.Success : c.Accent);
                using (var barPath = MainForm.RoundedRect(new Rectangle(3, 10, 4, Height - 20), 2))
                using (var b = new SolidBrush(accent))
                    g.FillPath(b, barPath);

                using (var f = ThemeService.UiFont(9.5f, bold: true))
                using (var b = new SolidBrush(c.TextPrimary))
                    g.DrawString(_title, f, b, 18, 12);

                using (var f = ThemeService.UiFont(8.5f))
                using (var b = new SolidBrush(c.TextSecondary))
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(_message, f, b, new RectangleF(18, 34, Width - 30, 24), sf);
            }
        }
    }
}