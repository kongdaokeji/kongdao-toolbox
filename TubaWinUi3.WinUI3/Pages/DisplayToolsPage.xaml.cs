using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using TubaWinUi3.Services.DisplayTools;

namespace TubaWinUi3.Pages;

public sealed partial class DisplayToolsPage : Page
{
    private List<DisplayInspection> _displays=[];
    private DisplayInspection? Selected => DisplayPicker.SelectedIndex>=0 && DisplayPicker.SelectedIndex<_displays.Count ? _displays[DisplayPicker.SelectedIndex] : null;
    private bool _loaded;
    public DisplayToolsPage()
    {
        InitializeComponent();
        InitializeColorEditor();
        Loaded+=async (_,_)=> { _loaded=true; await RefreshAsync(); };
        Unloaded+=(_,_)=> { _loaded=false; _colorRevision++; StopColorPreview(); };
        ActualThemeChanged+=(_,_)=> { if(Selected is {} d) DisplayChromaticityChart.Draw(GamutCanvas,d.Primaries,d.WhitePoint); };
    }
    private async void Refresh_Click(object sender,RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if(!RefreshButton.IsEnabled) return;
        string? old=Selected?.InterfaceId;
        RefreshButton.IsEnabled=false; DisplayPicker.IsEnabled=false;
        Status.Severity=InfoBarSeverity.Informational; Status.Message="正在读取显示器信息…"; Status.IsOpen=true;
        try
        {
            var displays=await Task.Run(DisplayInspectionService.InspectAsync);
            if(!_loaded) return;
            _displays=displays;
            DisplayPicker.Items.Clear();
            foreach(var d in displays) DisplayPicker.Items.Add(d.Summary);
            Details.Visibility=displays.Count>0?Visibility.Visible:Visibility.Collapsed;
            if(displays.Count>0) { int index=displays.FindIndex(d=>d.InterfaceId==old); DisplayPicker.SelectedIndex=Math.Max(index,0); }
            else { Status.Message="未发现活动显示器。请连接显示器后刷新。"; Status.Severity=InfoBarSeverity.Warning; }
        }
        catch(Exception ex) { Status.Message=$"读取失败：{ex.Message}"; Status.Severity=InfoBarSeverity.Error; }
        finally { RefreshButton.IsEnabled=true; DisplayPicker.IsEnabled=true; }
    }
    private void Display_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(Selected is not { } d) return;
        MonitorName.Text=d.Name;
        MonitorMode.Text=$"{(d.Primary ? "主显示器  ·  " : "")}{d.Mode}";
        MonitorScale.Text=d.Scale;
        var overviewKeys=new[] { "制造商 / 产品 ID", "连接方式", "屏幕尺寸", "像素密度", "刷新范围" };
        FillRows(InfoRows,d.Info.Where(p=>overviewKeys.Contains(p.Key)));
        FillRows(TechnicalRows,d.Info);
        FillRows(HdrRows,d.Hdr);
        HdrSummary.Text=string.Join("  ·  ",d.Hdr.Take(2).Select(p=>p.Value));
        GamutSummary.Text=DisplayGamut.Describe(d.Gamuts);
        GamutRows.Children.Clear();
        foreach(var g in d.Gamuts)
        {
            var panel=new Grid { ColumnSpacing=8 };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(80) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(80) });
            var label=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text=g.Name,VerticalAlignment=VerticalAlignment.Center });
            var help=new Button { Width=24,Height=24,Padding=new Thickness(2),Style=(Style)Application.Current.Resources["SubtleButtonStyle"] };
            var circle=new Grid { Width=16,Height=16 };
            circle.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Stroke=help.Foreground,StrokeThickness=1 });
            circle.Children.Add(new TextBlock { Text="?",FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center });
            help.Content=circle;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(help,$"了解 {g.Name}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(help,"DisplayGamutHelp_"+g.Name.Replace(" ","_").Replace("-","_"));
            ToolTipService.SetToolTip(help,$"了解 {g.Name}");
            var explanation=new StackPanel { Spacing=12,MaxWidth=320 };
            explanation.Children.Add(new TextBlock { Text=g.Name,Style=(Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            explanation.Children.Add(new TextBlock { Text=DisplayGamut.Explain(g.Name),TextWrapping=TextWrapping.Wrap });
            explanation.Children.Add(new TextBlock { Text="覆盖率：本屏与该色域的交集占该色域的比例，最高 100%。\n\n面积比：本屏色域三角形面积与参考色域面积之比，可以超过 100%，但不表示参考色域内的每一种颜色都能显示。\n\n两项均在 CIE 1931 xy 平面计算，不能代表色准。",TextWrapping=TextWrapping.Wrap,Opacity=.68 });
            help.Flyout=new Flyout { Content=explanation };
            label.Children.Add(help); panel.Children.Add(label);
            var coverage=new TextBlock { Text=$"{g.Coverage:F1}%",HorizontalAlignment=HorizontalAlignment.Right };
            var area=new TextBlock { Text=$"{g.AreaRatio:F1}%",HorizontalAlignment=HorizontalAlignment.Right };
            Grid.SetColumn(coverage,1); Grid.SetColumn(area,2); panel.Children.Add(coverage); panel.Children.Add(area);
            GamutRows.Children.Add(panel);
        }
        DisplayChromaticityChart.Draw(GamutCanvas,d.Primaries,d.WhitePoint);
        RefreshColorEditor(d);
        Status.IsOpen=d.Warnings.Count>0;
        Status.Severity=InfoBarSeverity.Warning; Status.Message=string.Join("\n",d.Warnings);
        OverviewScroll.ChangeView(null,0,null,true);
    }
    private void Layout_Changed(object sender,SizeChangedEventArgs e)
    {
        if(OverviewColumns is null || ColorCard is null) return;
        bool wide=e.NewSize.Width>=900;
        OverviewColumns.ColumnDefinitions[1].Width=new GridLength(wide?1:0,GridUnitType.Star);
        Grid.SetColumn(ColorCard,wide?1:0); Grid.SetRow(ColorCard,wide?0:1);
        ColorSettingsColumns.ColumnDefinitions[1].Width=new GridLength(wide?1:0,GridUnitType.Star);
        Grid.SetColumn(ProfileSettingsCard,wide?1:0); Grid.SetRow(ProfileSettingsCard,wide?0:1);
    }
    private void Tab_Changed(SelectorBar sender,SelectorBarSelectionChangedEventArgs args)
    {
        if(OverviewView is null || TestView is null || ProfileView is null) return;
        string tab=sender.SelectedItem?.Tag as string ?? "overview";
        OverviewView.Visibility=tab=="overview"?Visibility.Visible:Visibility.Collapsed;
        TestView.Visibility=tab=="tests"?Visibility.Visible:Visibility.Collapsed;
        ProfileView.Visibility=tab=="color"?Visibility.Visible:Visibility.Collapsed;
    }
    private static void FillRows(StackPanel target,IEnumerable<KeyValuePair<string,string>> rows)
    {
        target.Children.Clear();
        foreach(var row in rows)
        {
            var p=new Grid { ColumnSpacing=16,Padding=new Thickness(0,4,0,4) };
            p.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(136) });
            p.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            p.Children.Add(new TextBlock { Text=row.Key,TextWrapping=TextWrapping.Wrap,Opacity=.68 });
            var value=new TextBlock { Text=row.Value,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true };
            Grid.SetColumn(value,1); p.Children.Add(value);
            target.Children.Add(p);
        }
    }
    private async void Copy_Click(object sender,RoutedEventArgs e)
    {
        try { if(Selected is not {} d) return; var result=await ClipboardService.TrySetTextAsync(d.Report()); Notice(result.Success?"检测报告已复制。":$"复制失败：{result.Error?.Message}",result.Success?InfoBarSeverity.Success:InfoBarSeverity.Error); }
        catch(Exception ex) { Notice($"复制失败：{ex.Message}",InfoBarSeverity.Error); }
    }
    private async void Settings_Click(object sender,RoutedEventArgs e)
    {
        try { if(!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:display"))) Notice("无法打开显示设置。",InfoBarSeverity.Error); }
        catch(Exception ex) { Notice(ex.Message,InfoBarSeverity.Error); }
    }
    private void Test_Click(object sender,RoutedEventArgs e)
    {
        Window? window=null;
        try
        {
            if(Selected is not {} d) return;
            int start=int.Parse((string)((ComboBoxItem)TestPicker.SelectedItem).Tag);
            window=new Window { Title="屏幕检测 · Esc 退出" };
            window.Content=new ScreenTestPage(window,start) { RequestedTheme=ElementTheme.Dark };
            window.AppWindow.MoveAndResize(d.Bounds);
            window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            window.Activate();
        }
        catch(Exception ex) { window?.Close(); Notice($"无法启动测试：{ex.Message}",InfoBarSeverity.Error); }
    }
    private void Notice(string text,InfoBarSeverity severity) { Status.Message=text; Status.Severity=severity; Status.IsOpen=true; }
}
