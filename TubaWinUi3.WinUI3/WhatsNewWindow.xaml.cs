using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Net.Http;
using System.Text.Json;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3;

public sealed partial class WhatsNewWindow : Page
{
    private readonly Window _window;

    private record ReleaseEntry(string TagName, string? Name, string? Body, string? Date);

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private List<ReleaseEntry> _releases = [];

    static WhatsNewWindow()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-WhatsNew");
    }

    public WhatsNewWindow(Window window)
    {
        _window = window;
        InitializeComponent();
        _ = LoadReleasesAsync();
    }

    public static void Show()
    {
        var window = new Window();
        var page = new WhatsNewWindow(window);
        page.RequestedTheme = ThemeService.CurrentElementTheme;

        window.Content = page;
        BackdropService.ApplyBackdrop(window);
        window.AppWindow.Title = "新增内容";

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea is not null)
            {
                var workArea = displayArea.WorkArea;
                var w = (int)(workArea.Width * 0.82);
                var h = (int)(workArea.Height * 0.85);
                window.AppWindow.Resize(new SizeInt32(w, h));
                window.AppWindow.Move(new PointInt32(
                    workArea.X + (int)((workArea.Width - w) / 2),
                    workArea.Y + (int)((workArea.Height - h) / 2)));
            }
        }
        catch
        {
            window.AppWindow.Resize(new SizeInt32(1100, 750));
            try
            {
                var mainPos = App.MainWindow?.AppWindow.Position;
                if (mainPos is not null)
                    window.AppWindow.Move(new PointInt32(mainPos.Value.X + 50, mainPos.Value.Y + 50));
            }
            catch { }
        }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        ApplyTitleBarTheme(window);
        window.Activate();
    }

    private static void ApplyTitleBarTheme(Window window)
    {
        var tb = window.AppWindow.TitleBar;
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    private async Task LoadReleasesAsync()
    {
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var owner = "luolangaga";
            var repo = "tubatool";
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=20";

            var json = await _http.GetStringAsync(url);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _releases.Clear();

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var tag = item.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                    var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    var body = item.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
                    var date = item.TryGetProperty("published_at", out var dateEl) ? dateEl.GetString() : null;

                    if (!string.IsNullOrEmpty(tag))
                        _releases.Add(new ReleaseEntry(tag, name, body, date));
                }
            }

            PopulateVersionList();

            if (_releases.Count > 0)
                VersionListView.SelectedIndex = 0;
        }
        catch
        {
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }

    private void PopulateVersionList()
    {
        VersionListView.Items.Clear();

        foreach (var release in _releases)
        {
            var sp = new StackPanel { Spacing = 2 };

            var titleText = new TextBlock
            {
                Text = release.Name ?? release.TagName,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var dateText = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.6,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (release.Date is not null && DateTime.TryParse(release.Date, out var dt))
                dateText.Text = dt.ToString("yyyy-MM-dd");

            sp.Children.Add(titleText);
            sp.Children.Add(dateText);

            var item = new ListViewItem
            {
                Tag = release.TagName,
                Content = sp
            };

            VersionListView.Items.Add(item);
        }
    }

    private void VersionListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionListView.SelectedIndex < 0 || VersionListView.SelectedIndex >= _releases.Count)
            return;

        var release = _releases[VersionListView.SelectedIndex];
        VersionTitleText.Text = release.Name ?? release.TagName;

        if (release.Date is not null && DateTime.TryParse(release.Date, out var dt))
            VersionDateText.Text = dt.ToString("yyyy年M月d日");
        else
            VersionDateText.Text = "";

        MarkdownTextService.RenderToRichTextBlock(ChangelogText, release.Body ?? "暂无更新日志。");
    }
}
