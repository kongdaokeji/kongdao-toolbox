using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class NetworkConnectionsPage : Page
{
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);

    private DispatcherTimer? _refreshTimer;
    private bool _loading;

    public NetworkConnectionsPage()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (AutoRefreshToggle.IsOn && !_loading)
                await RefreshAsync();
        };
        _refreshTimer.Start();

        AutoRefreshToggle.Toggled += (_, _) =>
        {
            if (AutoRefreshToggle.IsOn)
                _refreshTimer?.Start();
            else
                _refreshTimer?.Stop();
        };

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loading = true;
        LoadingBar.Visibility = Visibility.Visible;
        var conns = await Task.Run(() => NetworkAdapterProxyService.GetActiveConnections());
        DispatcherQueue.TryEnqueue(() =>
        {
            RenderConnections(conns);
            LoadingBar.Visibility = Visibility.Collapsed;
            _loading = false;
        });
    }

    private void RenderConnections(List<ConnectionEntry> connections)
    {
        ConnectionPanel.Children.Clear();

        if (connections.Count == 0)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            SubtitleText.Text = "TCP 连接实时监控 · 0 条连接";
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        SubtitleText.Text = $"TCP 连接实时监控 · {connections.Count} 条连接";

        foreach (var c in connections)
        {
            var typeColor = c.AdapterType == "Wi-Fi" ? AccentBlue : AccentGreen;

            var row = new Grid
            {
                Padding = new Thickness(8, 4, 8, 4),
                ColumnSpacing = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var proc = new TextBlock
            {
                Text = c.ProcessName, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                VerticalAlignment = VerticalAlignment.Center
            };

            var typeTag = new Border
            {
                Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(25, typeColor.R, typeColor.G, typeColor.B)),
                Child = new TextBlock
                {
                    Text = c.AdapterType, FontSize = 11,
                    Foreground = new SolidColorBrush(typeColor),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var addr = new TextBlock
            {
                Text = c.RemoteAddress, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                VerticalAlignment = VerticalAlignment.Center
            };

            var port = new TextBlock
            {
                Text = c.RemotePort.ToString(), FontSize = 12, FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                VerticalAlignment = VerticalAlignment.Center
            };

            var adapter = new TextBlock
            {
                Text = c.AdapterName, FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                VerticalAlignment = VerticalAlignment.Center
            };

            row.Children.Add(proc); Grid.SetColumn(proc, 0);
            row.Children.Add(typeTag); Grid.SetColumn(typeTag, 1);
            row.Children.Add(addr); Grid.SetColumn(addr, 2);
            row.Children.Add(port); Grid.SetColumn(port, 3);
            row.Children.Add(adapter); Grid.SetColumn(adapter, 4);

            ConnectionPanel.Children.Add(row);
        }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer?.Stop();
        App.MainWindow?.NavigateBack();
    }
}
