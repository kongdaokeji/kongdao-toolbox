using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class RatingSystemPage : Page
{
    private Pivot _mainPivot = null!;
    private InfoBar _infoBar = null!;

    // 笔记本
    private ProgressBar _laptopProgress = null!;
    private ListView _laptopList = null!;
    private StackPanel _laptopEmpty = null!;
    private ComboBox _laptopSortCombo = null!;
    private TextBlock _laptopStatsText = null!;
    private TextBlock _laptopIntro = null!;
    private string _laptopSortBy = "overall";

    // 台式机
    private ProgressBar _desktopProgress = null!;
    private ListView _desktopList = null!;
    private StackPanel _desktopEmpty = null!;
    private ComboBox _desktopTypeCombo = null!;
    private ComboBox _desktopSortCombo = null!;
    private TextBlock _desktopStatsText = null!;
    private TextBlock _desktopIntro = null!;
    private string _desktopComponentType = "cpu";
    private string _desktopSortBy = "overall";

    // 暂存的提交对话框使用的硬件信息
    private string _detectedDeviceModel = "";
    private string _detectedCpu = "";
    private string _detectedGpu = "";

    public RatingSystemPage()
    {
        InitializeComponent();
        Content = BuildUI();
        Loaded += OnLoaded;
    }

    private Grid BuildUI()
    {
        var root = new Grid
        {
            Padding = new Thickness(24, 12, 24, 8),
            RowSpacing = 8
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 顶部工具栏
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // InfoBar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Pivot

        // 顶部标题 + 提交按钮
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        toolbar.Children.Add(new TextBlock
        {
            Text = "硬件评分系统",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var submitBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\ue898", FontSize = 14 },
                    new TextBlock { Text = "提交评分", FontSize = 13 }
                }
            },
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6)
        };
        submitBtn.Click += (_, _) => _ = OpenSubmitDialogAsync();
        toolbar.Children.Add(submitBtn);

        toolbar.Children.Add(new TextBlock
        {
            Text = "为你的笔记本或台式机硬件打分，查看社区排行榜对比评价",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        });

        root.Children.Add(toolbar);
        Grid.SetRow(toolbar, 0);

        _infoBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            Severity = InfoBarSeverity.Informational
        };
        root.Children.Add(_infoBar);
        Grid.SetRow(_infoBar, 1);

        _mainPivot = new Pivot();
        _mainPivot.Items.Add(BuildLaptopPivotItem());
        _mainPivot.Items.Add(BuildDesktopPivotItem());
        root.Children.Add(_mainPivot);
        Grid.SetRow(_mainPivot, 2);

        return root;
    }

    // ---------------------------------------------------------------------
    // 构建笔记本排行 Pivot
    // ---------------------------------------------------------------------
    private PivotItem BuildLaptopPivotItem()
    {
        var grid = new Grid { RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // filterRow
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // intro
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // progress
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list

        // filterRow: 排序 / 刷新 / 统计
        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 2, 0, 2)
        };

        filterRow.Children.Add(new TextBlock
        {
            Text = "排序：",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        _laptopSortCombo = new ComboBox { MinWidth = 110 };
        foreach (var (key, label) in RatingConstants.LaptopSortOptions)
            _laptopSortCombo.Items.Add(label);
        _laptopSortCombo.SelectedIndex = 0;
        _laptopSortCombo.SelectionChanged += (_, _) =>
        {
            var idx = _laptopSortCombo.SelectedIndex;
            if (idx >= 0 && idx < RatingConstants.LaptopSortOptions.Length)
            {
                _laptopSortBy = RatingConstants.LaptopSortOptions[idx].Key;
                _ = LoadLaptopLeaderboardAsync();
            }
        };
        filterRow.Children.Add(_laptopSortCombo);

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\ue72c", FontSize = 13 },
                    new TextBlock { Text = "刷新", FontSize = 13 }
                }
            },
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4)
        };
        refreshBtn.Click += (_, _) => _ = LoadLaptopLeaderboardAsync(true);
        filterRow.Children.Add(refreshBtn);

        _laptopStatsText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        filterRow.Children.Add(_laptopStatsText);

        grid.Children.Add(filterRow);
        Grid.SetRow(filterRow, 0);

        _laptopIntro = new TextBlock
        {
            Text = "笔记本排行榜按「机型型号 + CPU + GPU」归组聚合，展示各机型的社区综合评价。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Margin = new Thickness(0, 0, 0, 2)
        };
        grid.Children.Add(_laptopIntro);
        Grid.SetRow(_laptopIntro, 1);

        _laptopProgress = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed
        };
        grid.Children.Add(_laptopProgress);
        Grid.SetRow(_laptopProgress, 2);

        var listArea = new Grid();
        listArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _laptopEmpty = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(0, 40, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new FontIcon { Glyph = "\ue7c3", FontSize = 36, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                new TextBlock { Text = "暂无笔记本评分", Foreground = new SolidColorBrush(ThemeColors.DimText) }
            }
        };

        _laptopList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            Visibility = Visibility.Collapsed
        };
        _laptopList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement fe && fe.Tag is LaptopLeaderboardEntry entry)
                _ = OpenLaptopReviewsAsync(entry);
        };

        listArea.Children.Add(_laptopEmpty);
        Grid.SetRow(_laptopEmpty, 0);
        listArea.Children.Add(_laptopList);
        Grid.SetRow(_laptopList, 1);

        var listBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = listArea,
            Margin = new Thickness(0, 2, 0, 0)
        };
        grid.Children.Add(listBorder);
        Grid.SetRow(listBorder, 3);

        return new PivotItem
        {
            Header = new TextBlock { Text = "笔记本排行", FontSize = 14 },
            Content = grid
        };
    }

    // ---------------------------------------------------------------------
    // 构建台式机部件排行 Pivot
    // ---------------------------------------------------------------------
    private PivotItem BuildDesktopPivotItem()
    {
        var grid = new Grid { RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // filterRow
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // intro
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // progress
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list

        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 2, 0, 2)
        };

        filterRow.Children.Add(new TextBlock
        {
            Text = "部件：",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        _desktopTypeCombo = new ComboBox { MinWidth = 130 };
        foreach (var t in RatingConstants.ComponentTypesInOrder)
            _desktopTypeCombo.Items.Add(RatingConstants.GetComponentTypeLabel(t));
        _desktopTypeCombo.SelectedIndex = 0;
        _desktopTypeCombo.SelectionChanged += (_, _) =>
        {
            var idx = _desktopTypeCombo.SelectedIndex;
            if (idx >= 0 && idx < RatingConstants.ComponentTypesInOrder.Count)
            {
                _desktopComponentType = RatingConstants.ComponentTypesInOrder[idx];
                _ = LoadDesktopLeaderboardAsync();
            }
        };
        filterRow.Children.Add(_desktopTypeCombo);

        filterRow.Children.Add(new TextBlock
        {
            Text = "排序：",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        _desktopSortCombo = new ComboBox { MinWidth = 110 };
        foreach (var (key, label) in RatingConstants.DesktopSortOptions)
            _desktopSortCombo.Items.Add(label);
        _desktopSortCombo.SelectedIndex = 0;
        _desktopSortCombo.SelectionChanged += (_, _) =>
        {
            var idx = _desktopSortCombo.SelectedIndex;
            if (idx >= 0 && idx < RatingConstants.DesktopSortOptions.Length)
            {
                _desktopSortBy = RatingConstants.DesktopSortOptions[idx].Key;
                _ = LoadDesktopLeaderboardAsync();
            }
        };
        filterRow.Children.Add(_desktopSortCombo);

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\ue72c", FontSize = 13 },
                    new TextBlock { Text = "刷新", FontSize = 13 }
                }
            },
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4)
        };
        refreshBtn.Click += (_, _) => _ = LoadDesktopLeaderboardAsync(true);
        filterRow.Children.Add(refreshBtn);

        _desktopStatsText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        filterRow.Children.Add(_desktopStatsText);

        grid.Children.Add(filterRow);
        Grid.SetRow(filterRow, 0);

        _desktopIntro = new TextBlock
        {
            Text = "台式机排行榜按部件型号归组，选择部件分类查看对应排行榜，单击列表项查看详细评价。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Margin = new Thickness(0, 0, 0, 2)
        };
        grid.Children.Add(_desktopIntro);
        Grid.SetRow(_desktopIntro, 1);

        _desktopProgress = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed
        };
        grid.Children.Add(_desktopProgress);
        Grid.SetRow(_desktopProgress, 2);

        var listArea = new Grid();
        listArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        listArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _desktopEmpty = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(0, 40, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new FontIcon { Glyph = "\ue7c3", FontSize = 36, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                new TextBlock { Text = "暂无该分类评分", Foreground = new SolidColorBrush(ThemeColors.DimText) }
            }
        };

        _desktopList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            Visibility = Visibility.Collapsed
        };
        _desktopList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement fe && fe.Tag is DesktopLeaderboardEntry entry)
                _ = OpenDesktopReviewsAsync(entry);
        };

        listArea.Children.Add(_desktopEmpty);
        Grid.SetRow(_desktopEmpty, 0);
        listArea.Children.Add(_desktopList);
        Grid.SetRow(_desktopList, 1);

        var listBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = listArea,
            Margin = new Thickness(0, 2, 0, 0)
        };
        grid.Children.Add(listBorder);
        Grid.SetRow(listBorder, 3);

        return new PivotItem
        {
            Header = new TextBlock { Text = "台式机部件排行", FontSize = 14 },
            Content = grid
        };
    }

    // ---------------------------------------------------------------------
    // 加载
    // ---------------------------------------------------------------------
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DetecHardwareInfo();
        await LoadLaptopLeaderboardAsync();
        await LoadDesktopLeaderboardAsync();
        _ = LoadStatsAsync();
    }

    private void DetecHardwareInfo()
    {
        try
        {
            _detectedDeviceModel = HardwareInfoService.GetDeviceModel();
            _detectedCpu = HardwareInfoService.GetCpuName();
            _detectedGpu = HardwareInfoService.GetGpuName();
        }
        catch { }
    }

    private async Task LoadLaptopLeaderboardAsync(bool forceRefresh = false)
    {
        if (_laptopProgress is null) return;
        _laptopProgress.Visibility = Visibility.Visible;
        _laptopList.Visibility = Visibility.Collapsed;
        _laptopEmpty.Visibility = Visibility.Collapsed;

        var entries = await Task.Run(() => RatingSystemService.GetLaptopLeaderboardAsync(_laptopSortBy, 1, 50, forceRefresh));

        _laptopProgress.Visibility = Visibility.Collapsed;
        _laptopList.Items.Clear();

        int displayRank = 0;
        foreach (var entry in entries)
        {
            displayRank++;
            _laptopList.Items.Add(BuildLaptopRow(entry, displayRank));
        }

        if (entries.Count > 0)
        {
            _laptopList.Visibility = Visibility.Visible;
            _laptopStatsText.Text = $"共 {entries.Count} 款机型";
        }
        else
        {
            _laptopEmpty.Visibility = Visibility.Visible;
            _laptopStatsText.Text = "暂无数据";
        }
    }

    private async Task LoadDesktopLeaderboardAsync(bool forceRefresh = false)
    {
        if (_desktopProgress is null) return;
        _desktopProgress.Visibility = Visibility.Visible;
        _desktopList.Visibility = Visibility.Collapsed;
        _desktopEmpty.Visibility = Visibility.Collapsed;

        var entries = await Task.Run(() => RatingSystemService.GetDesktopLeaderboardAsync(_desktopComponentType, _desktopSortBy, 1, 50, forceRefresh));

        _desktopProgress.Visibility = Visibility.Collapsed;
        _desktopList.Items.Clear();

        int displayRank = 0;
        foreach (var entry in entries)
        {
            displayRank++;
            _desktopList.Items.Add(BuildDesktopRow(entry, displayRank));
        }

        if (entries.Count > 0)
        {
            _desktopList.Visibility = Visibility.Visible;
            _desktopStatsText.Text = $"共 {entries.Count} 款型号";
            _desktopIntro.Text = $"台式机·{RatingConstants.GetComponentTypeLabel(_desktopComponentType)}排行榜，按部件型号归组。点击列表项查看详细评价。";
        }
        else
        {
            _desktopEmpty.Visibility = Visibility.Visible;
            _desktopStatsText.Text = "暂无数据";
        }
    }

    private async Task LoadStatsAsync(bool forceRefresh = false)
    {
        var stats = await Task.Run(() => RatingSystemService.GetStatsAsync(forceRefresh));
        if (stats is null) return;
    }

    // ---------------------------------------------------------------------
    // 行卡片
    // ---------------------------------------------------------------------
    private static Color RankColor(int rank) => rank switch
    {
        1 => Color.FromArgb(255, 255, 215, 0),
        2 => Color.FromArgb(255, 192, 192, 192),
        3 => Color.FromArgb(255, 205, 127, 50),
        _ => ThemeColors.DimText
    };

    private static Border BuildRankBadge(int rank)
    {
        Color c = RankColor(rank);
        return new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(40, c.R, c.G, c.B)),
            Child = new TextBlock
            {
                Text = rank.ToString(),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(c),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static TextBlock ScoreText(double score, bool big = true)
    {
        Color c = score >= 8.5 ? ThemeColors.AccentGreen
            : score >= 7 ? ThemeColors.AccentBlue
            : score >= 5 ? ThemeColors.AccentOrange
            : ThemeColors.AccentRed;
        return new TextBlock
        {
            Text = score.ToString("F1"),
            FontSize = big ? 18 : 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(c)
        };
    }

    private Grid BuildLaptopRow(LaptopLeaderboardEntry entry, int rank)
    {
        var rowGrid = new Grid
        {
            ColumnSpacing = 12,
            Tag = entry,
            Padding = new Thickness(12, 8, 12, 8)
        };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });   // rank
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // info
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // overall
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // count

        rowGrid.Children.Add(BuildRankBadge(rank));
        Grid.SetColumn((FrameworkElement)rowGrid.Children[^1], 0);

        var infoPanel = new StackPanel { Spacing = 2 };
        infoPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.DeviceModel) ? "未知机型" : entry.DeviceModel,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextWrapping = TextWrapping.Wrap
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"CPU: {entry.Cpu}    GPU: {entry.Gpu}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        });

        // 维度小分数
        var dims = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 2, 0, 0)
        };
        dims.Children.Add(MakeDimText("质感", entry.AvgBuildQuality));
        dims.Children.Add(MakeDimText("屏幕", entry.AvgScreen));
        dims.Children.Add(MakeDimText("噪音", entry.AvgNoise));
        dims.Children.Add(MakeDimText("性能", entry.AvgPerformance));
        infoPanel.Children.Add(dims);

        rowGrid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);

        var overallPanel = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        overallPanel.Children.Add(ScoreText(entry.AvgOverall));
        overallPanel.Children.Add(new TextBlock
        {
            Text = "总评",
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        rowGrid.Children.Add(overallPanel);
        Grid.SetColumn(overallPanel, 2);

        rowGrid.Children.Add(new TextBlock
        {
            Text = $"{entry.RatingCount} 评价",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        Grid.SetColumn((FrameworkElement)rowGrid.Children[^1], 3);

        return rowGrid;
    }

    private static TextBlock MakeDimText(string label, double score)
    {
        Color c = score >= 8.5 ? ThemeColors.AccentGreen
            : score >= 7 ? ThemeColors.AccentBlue
            : score >= 5 ? ThemeColors.AccentOrange
            : ThemeColors.AccentRed;
        return new TextBlock
        {
            Text = $"{label} {score:F1}",
            FontSize = 11,
            Foreground = new SolidColorBrush(c)
        };
    }

    private Grid BuildDesktopRow(DesktopLeaderboardEntry entry, int rank)
    {
        var rowGrid = new Grid
        {
            ColumnSpacing = 12,
            Tag = entry,
            Padding = new Thickness(12, 8, 12, 8)
        };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        rowGrid.Children.Add(BuildRankBadge(rank));
        Grid.SetColumn((FrameworkElement)rowGrid.Children[^1], 0);

        var infoPanel = new StackPanel { Spacing = 2 };
        infoPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.ComponentModel) ? "未知型号" : entry.ComponentModel,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextWrapping = TextWrapping.Wrap
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = RatingConstants.GetComponentTypeLabel(entry.ComponentType),
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        rowGrid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);

        var overallPanel = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        overallPanel.Children.Add(ScoreText(entry.AvgOverall));
        overallPanel.Children.Add(new TextBlock
        {
            Text = "总评",
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        rowGrid.Children.Add(overallPanel);
        Grid.SetColumn(overallPanel, 2);

        rowGrid.Children.Add(new TextBlock
        {
            Text = $"{entry.RatingCount} 评价",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        Grid.SetColumn((FrameworkElement)rowGrid.Children[^1], 3);

        return rowGrid;
    }

    // ---------------------------------------------------------------------
    // 评价详情对话框
    // ---------------------------------------------------------------------
    private async Task OpenLaptopReviewsAsync(LaptopLeaderboardEntry entry)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
            Title = string.IsNullOrWhiteSpace(entry.DeviceModel) ? "笔记本评价" : entry.DeviceModel,
            CloseButtonText = "关闭"
        };

        var container = new StackPanel { Spacing = 6 };

        container.Children.Add(new TextBlock
        {
            Text = $"CPU: {entry.Cpu}\nGPU: {entry.Gpu}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        });

        container.Children.Add(new TextBlock
        {
            Text = $"总评均分 {entry.AvgOverall:F1} · {entry.RatingCount} 条评价",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Margin = new Thickness(0, 4, 0, 6)
        });

        var progress = new ProgressBar { IsIndeterminate = true };
        container.Children.Add(progress);

        var reviewsPanel = new StackPanel { Spacing = 6 };
        container.Children.Add(reviewsPanel);

        dialog.Content = new ScrollViewer
        {
            Content = container,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 460
        };

        _ = dialog.ShowAsync();

        var reviews = await Task.Run(() =>
            RatingSystemService.GetLaptopReviewsAsync(entry.DeviceModel, entry.Cpu, entry.Gpu));

        progress.Visibility = Visibility.Collapsed;
        reviewsPanel.Children.Clear();

        if (reviews.Count == 0)
        {
            reviewsPanel.Children.Add(new TextBlock
            {
                Text = "暂无详细评价",
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                FontSize = 12
            });
        }
        else
        {
            foreach (var r in reviews)
                reviewsPanel.Children.Add(BuildLaptopReviewCard(r));
        }
    }

    private Border BuildLaptopReviewCard(LaptopReviewEntry r)
    {
        var panel = new StackPanel { Spacing = 4 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        header.Children.Add(new TextBlock
        {
            Text = r.Author,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        header.Children.Add(ScoreText(r.OverallScore, false));
        panel.Children.Add(header);

        var dims = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        dims.Children.Add(MakeDimText("质感", r.BuildQualityScore));
        dims.Children.Add(MakeDimText("屏幕", r.ScreenScore));
        dims.Children.Add(MakeDimText("噪音", r.NoiseScore));
        dims.Children.Add(MakeDimText("性能", r.PerformanceScore));
        panel.Children.Add(dims);

        if (!string.IsNullOrWhiteSpace(r.ReviewText))
            panel.Children.Add(new TextBlock
            {
                Text = r.ReviewText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemeColors.SecondaryText)
            });

        panel.Children.Add(new TextBlock
        {
            Text = FormatTime(r.CreatedAtMs),
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = panel
        };
    }

    private async Task OpenDesktopReviewsAsync(DesktopLeaderboardEntry entry)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
            Title = string.IsNullOrWhiteSpace(entry.ComponentModel) ? "部件评价" : entry.ComponentModel,
            CloseButtonText = "关闭"
        };

        var container = new StackPanel { Spacing = 6 };

        container.Children.Add(new TextBlock
        {
            Text = $"{RatingConstants.GetComponentTypeLabel(entry.ComponentType)} · {entry.RatingCount} 条评价 · 均分 {entry.AvgOverall:F1}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        var progress = new ProgressBar { IsIndeterminate = true };
        container.Children.Add(progress);

        var reviewsPanel = new StackPanel { Spacing = 6 };
        container.Children.Add(reviewsPanel);

        dialog.Content = new ScrollViewer
        {
            Content = container,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 460
        };

        _ = dialog.ShowAsync();

        var reviews = await Task.Run(() =>
            RatingSystemService.GetDesktopReviewsAsync(entry.ComponentType, entry.ComponentModel));

        progress.Visibility = Visibility.Collapsed;
        reviewsPanel.Children.Clear();

        if (reviews.Count == 0)
        {
            reviewsPanel.Children.Add(new TextBlock
            {
                Text = "暂无详细评价",
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                FontSize = 12
            });
        }
        else
        {
            foreach (var r in reviews)
                reviewsPanel.Children.Add(BuildDesktopReviewCard(r));
        }
    }

    private Border BuildDesktopReviewCard(DesktopReviewEntry r)
    {
        var panel = new StackPanel { Spacing = 4 };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        header.Children.Add(new TextBlock
        {
            Text = r.Author,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        header.Children.Add(ScoreText(r.OverallScore, false));
        panel.Children.Add(header);

        if (!string.IsNullOrWhiteSpace(r.ReviewText))
            panel.Children.Add(new TextBlock
            {
                Text = r.ReviewText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(ThemeColors.SecondaryText)
            });

        panel.Children.Add(new TextBlock
        {
            Text = FormatTime(r.CreatedAtMs),
            FontSize = 10,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        return new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(ThemeColors.SubtleBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = panel
        };
    }

    private static string FormatTime(long ms)
    {
        if (ms <= 0) return "";
        try
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            return dt.ToString("yyyy-MM-dd HH:mm");
        }
        catch { return ""; }
    }

    // ---------------------------------------------------------------------
    // 提交评分对话框
    // ---------------------------------------------------------------------
    private async Task OpenSubmitDialogAsync()
    {
        bool isLaptop;
        try { isLaptop = HardwareInfoService.IsLaptop(); }
        catch { isLaptop = false; }

        ContentDialog? dialog = isLaptop ? BuildLaptopSubmitDialog() : BuildDesktopSubmitDialog();
        if (dialog is null) return;
        dialog.XamlRoot = XamlRoot;
        dialog.RequestedTheme = ThemeService.CurrentElementTheme;
        dialog.PrimaryButtonText = "提交";
        dialog.CloseButtonText = "取消";
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            e.Cancel = true;
            if (isLaptop)
                await SubmitLaptopAsync(dialog);
            else
                await SubmitDesktopAsync(dialog);
        };

        await dialog.ShowAsync();
    }

    private ContentDialog BuildLaptopSubmitDialog()
    {
        var panel = new StackPanel { Spacing = 10, Width = 460 };

        panel.Children.Add(MakeInfoRow("机型型号", _detectedDeviceModel));
        panel.Children.Add(MakeInfoRow("CPU", _detectedCpu));
        panel.Children.Add(MakeInfoRow("GPU", _detectedGpu));

        var authorBox = new TextBox
        {
            Header = "昵称（可选）",
            PlaceholderText = "匿名用户",
            Text = RatingSystemService.AuthorName
        };
        panel.Children.Add(authorBox);

        Slider overall = BuildScoreSlider("总评");
        Slider buildQuality = BuildScoreSlider("质感");
        Slider screen = BuildScoreSlider("屏幕");
        Slider noise = BuildScoreSlider("噪音");
        Slider performance = BuildScoreSlider("性能");
        panel.Children.Add(overall);
        panel.Children.Add(buildQuality);
        panel.Children.Add(screen);
        panel.Children.Add(noise);
        panel.Children.Add(performance);

        var reviewBox = new TextBox
        {
            Header = "评价文字（可选）",
            PlaceholderText = "说说这台笔记本的使用体验……",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 1000,
            MaxHeight = 100
        };
        panel.Children.Add(reviewBox);

        var tip = new TextBlock
        {
            Text = "同一设备对同一机型只能提交一次评分。",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        panel.Children.Add(tip);

        var dialog = new ContentDialog
        {
            Title = "提交笔记本评分",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 600
            },
            Tag = new LaptopSubmitState(authorBox, overall, buildQuality, screen, noise, performance, reviewBox)
        };
        return dialog;
    }

    private record LaptopSubmitState(
        TextBox Author, Slider Overall, Slider BuildQuality, Slider Screen, Slider Noise,
        Slider Performance, TextBox Review);

    private async Task SubmitLaptopAsync(ContentDialog dialog)
    {
        if (dialog.Tag is not LaptopSubmitState s)
        {
            ShowToast(InfoBarSeverity.Error, "提交失败", "状态丢失");
            return;
        }

        var req = new RatingSystemService.LaptopRatingRequest
        {
            DeviceModel = _detectedDeviceModel,
            Cpu = _detectedCpu,
            Gpu = _detectedGpu,
            OverallScore = (int)s.Overall.Value,
            BuildQualityScore = (int)s.BuildQuality.Value,
            ScreenScore = (int)s.Screen.Value,
            NoiseScore = (int)s.Noise.Value,
            PerformanceScore = (int)s.Performance.Value,
            ReviewText = string.IsNullOrWhiteSpace(s.Review.Text) ? null : s.Review.Text,
            Author = s.Author.Text
        };

        if (string.IsNullOrWhiteSpace(req.DeviceModel) && string.IsNullOrWhiteSpace(req.Cpu))
        {
            ShowToast(InfoBarSeverity.Warning, "信息不全", "未能检测到机型信息");
            return;
        }

        var (ok, msg) = await Task.Run(() => RatingSystemService.SubmitLaptopRatingAsync(req));
        if (ok)
        {
            dialog.Hide();
            ShowToast(InfoBarSeverity.Success, "提交成功", "感谢你的评价！正在刷新排行榜……");
            await LoadLaptopLeaderboardAsync(true);
        }
        else
        {
            ShowToast(InfoBarSeverity.Error, "提交失败", msg);
        }
    }

    private ContentDialog BuildDesktopSubmitDialog()
    {
        var panel = new StackPanel { Spacing = 10, Width = 460 };

        var typeCombo = new ComboBox { Header = "部件类型", MinWidth = 200 };
        foreach (var t in RatingConstants.ComponentTypesInOrder)
            typeCombo.Items.Add(RatingConstants.GetComponentTypeLabel(t));
        typeCombo.SelectedIndex = 0;
        panel.Children.Add(typeCombo);

        var modelBox = new TextBox
        {
            Header = "部件型号",
            PlaceholderText = "如 Intel Core i7-14700K"
        };
        panel.Children.Add(modelBox);

        // 尝试根据默认选中的部件类型预填当前硬件
        PrefillDesktopModel(typeCombo.SelectedIndex, modelBox);

        typeCombo.SelectionChanged += (_, _) =>
        {
            modelBox.Text = "";
            PrefillDesktopModel(typeCombo.SelectedIndex, modelBox);
        };

        var authorBox = new TextBox
        {
            Header = "昵称（可选）",
            PlaceholderText = "匿名用户",
            Text = RatingSystemService.AuthorName
        };
        panel.Children.Add(authorBox);

        Slider overall = BuildScoreSlider("总评");
        panel.Children.Add(overall);

        var reviewBox = new TextBox
        {
            Header = "评价文字（可选）",
            PlaceholderText = "说说这个部件的使用体验……",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 1000,
            MaxHeight = 100
        };
        panel.Children.Add(reviewBox);

        var tip = new TextBlock
        {
            Text = "同一设备对同一部件只能提交一次评分。",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        panel.Children.Add(tip);

        var dialog = new ContentDialog
        {
            Title = "提交台式机部件评分",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 600
            },
            Tag = new DesktopSubmitState(typeCombo, modelBox, authorBox, overall, reviewBox)
        };
        return dialog;
    }

    private void PrefillDesktopModel(int typeIndex, TextBox modelBox)
    {
        if (typeIndex < 0 || typeIndex >= RatingConstants.ComponentTypesInOrder.Count) return;
        var type = RatingConstants.ComponentTypesInOrder[typeIndex];
        string model = type switch
        {
            "cpu" => _detectedCpu,
            "gpu" => _detectedGpu,
            "motherboard" => SafeGet(HardwareInfoService.GetMotherboardModel),
            "disk" => SafeGet(HardwareInfoService.GetPrimaryDiskModel),
            "memory" => SafeGet(HardwareInfoService.GetMemoryDescription),
            _ => ""
        };
        if (!string.IsNullOrWhiteSpace(model))
            modelBox.Text = model;
    }

    private static string SafeGet(Func<string> getter)
    {
        try { return getter() ?? ""; }
        catch { return ""; }
    }

    public sealed record DesktopSubmitState(
        ComboBox TypeCombo, TextBox ModelBox, TextBox Author, Slider Overall, TextBox Review);

    private async Task SubmitDesktopAsync(ContentDialog dialog)
    {
        if (dialog.Tag is not DesktopSubmitState s)
        {
            ShowToast(InfoBarSeverity.Error, "提交失败", "状态丢失");
            return;
        }

        int typeIdx = s.TypeCombo.SelectedIndex;
        string type = typeIdx >= 0 && typeIdx < RatingConstants.ComponentTypesInOrder.Count
            ? RatingConstants.ComponentTypesInOrder[typeIdx] : "other";

        var model = s.ModelBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowToast(InfoBarSeverity.Warning, "信息不全", "请填写部件型号");
            return;
        }

        var req = new RatingSystemService.DesktopRatingRequest
        {
            ComponentType = type,
            ComponentModel = model,
            OverallScore = (int)s.Overall.Value,
            ReviewText = string.IsNullOrWhiteSpace(s.Review.Text) ? null : s.Review.Text,
            Author = s.Author.Text
        };

        var (ok, msg) = await Task.Run(() => RatingSystemService.SubmitDesktopRatingAsync(req));
        if (ok)
        {
            dialog.Hide();
            ShowToast(InfoBarSeverity.Success, "提交成功", "感谢你的评价！正在刷新排行榜……");
            // 切换到对应分类并刷新
            _desktopComponentType = type;
            _desktopTypeCombo.SelectedIndex = typeIdx;
            await LoadDesktopLeaderboardAsync(true);
        }
        else
        {
            ShowToast(InfoBarSeverity.Error, "提交失败", msg);
        }
    }

    private static Slider BuildScoreSlider(string label)
    {
        var slider = new Slider
        {
            Minimum = 1,
            Maximum = 10,
            Value = 8,
            StepFrequency = 1,
            SnapsTo = SliderSnapsTo.StepValues,
            TickFrequency = 1,
            TickPlacement = TickPlacement.None,
            Header = $"{label}（1-10）",
            Width = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        return slider;
    }

    private static StackPanel MakeInfoRow(string label, string value)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        sp.Children.Add(new TextBlock
        {
            Text = label + "：",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "未知" : value,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2
        });
        return sp;
    }

    // ---------------------------------------------------------------------
    // Toast
    // ---------------------------------------------------------------------
    private DispatcherQueueTimer? _toastTimer;
    private void ShowToast(InfoBarSeverity severity, string title, string message)
    {
        _infoBar.Severity = severity;
        _infoBar.Title = title;
        _infoBar.Message = message;
        _infoBar.IsOpen = true;

        _toastTimer?.Stop();
        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(3);
        _toastTimer.Tick += (_, _) =>
        {
            _infoBar.IsOpen = false;
            _toastTimer!.Stop();
        };
        _toastTimer.Start();
    }
}