// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. Adapted from microsoft/WinUI-Gallery Controls/HomePage/Tile.xaml.cs.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace KongdaoToolbox.Controls;

public sealed partial class DashboardGalleryTile : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DashboardGalleryTile), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(DashboardGalleryTile), new PropertyMetadata(""));
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(object), typeof(DashboardGalleryTile), new PropertyMetadata(null));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public HyperlinkButton ActionButton => OpenButton;
    public event RoutedEventHandler? Click;

    public DashboardGalleryTile()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateCompactLayout();

        // Win10 上这张卡片的 AcrylicBrush 是应用侧 GPU 模糊：首页几十张卡片叠在一起，
        // 窗口一移动就要全部重算，拖动必然掉帧。改用等效纯色（普通 alpha 混合，代价可忽略）。
        if (Services.AcrylicCompat.UseSolidSurface)
        {
            ApplySolidSurface();
            Loaded += (_, _) => ApplySolidSurface();
            ActualThemeChanged += (_, _) => ApplySolidSurface();
        }
    }

    private void ApplySolidSurface()
        => TileRoot.Background = Services.AcrylicCompat.CreateTileSurfaceBrush(ActualTheme);
    private void OpenButton_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);

    private void UpdateCompactLayout()
    {
        bool compact = ActualWidth < 190 || ActualHeight < 156;
        BodyGrid.Padding = new Thickness(compact ? 12 : 24);
        BodyGrid.RowSpacing = compact ? 8 : 12;
        BodyGrid.RowDefinitions[0].Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(48);
        BodyGrid.RowDefinitions[1].Height = compact ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        DescriptionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        WordsPanel.Margin = new Thickness(0, 0, compact ? 16 : 0, 0);
        LogoBox.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        LogoBox.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Top;
        LogoBox.Width = LogoBox.Height = compact ? Math.Max(16, Math.Min(ActualWidth - 32, ActualHeight - 52) * 0.9) : 48;
    }
}
