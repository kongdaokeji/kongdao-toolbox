using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services.DisplayTools;

namespace TubaWinUi3.Pages;

public sealed partial class DisplayToolsPage
{
    private readonly Dictionary<string, Slider> _colorSliders = new();
    private bool _loadingColor, _colorBusy;
    private long _colorRevision;
    private DisplayGammaSession? _pendingColor;
    private DisplayColorConfirmationPage? _colorConfirmation;

    private void InitializeColorEditor()
    {
        void Add(StackPanel target, string id, string title, double min, double max, double step, double value)
        {
            var group = new StackPanel { Spacing = 4 };
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            var number = new NumberBox { Minimum = min, Maximum = max, SmallChange = step, Value = value, Width = 96 };
            AutomationProperties.SetName(number, title); AutomationProperties.SetAutomationId(number, "DisplayColorNumber_" + id);
            Grid.SetColumn(number, 1); row.Children.Add(number); group.Children.Add(row);
            var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = step, Value = value };
            AutomationProperties.SetName(slider, title); AutomationProperties.SetAutomationId(slider, "DisplayColorSlider_" + id);
            slider.ValueChanged += (_, _) => { if (Math.Abs(number.Value - slider.Value) > .00001) number.Value = slider.Value; ColorValueChanged(); };
            number.ValueChanged += (_, _) => { if (!double.IsNaN(number.Value) && Math.Abs(number.Value - slider.Value) > .00001) slider.Value = number.Value; };
            _colorSliders.Add(id, slider); group.Children.Add(slider); target.Children.Add(group);
        }
        Add(BasicColorControls, "temperature", "色温 · K（近似）", 3500, 9000, 100, 6500);
        Add(BasicColorControls, "brightness", "画面亮度 · %", 75, 110, 1, 100);
        Add(BasicColorControls, "contrast", "对比度 · %", 80, 120, 1, 100);
        Add(AdvancedColorControls, "gamma", "Gamma · 中间调", .8, 1.3, .01, 1);
        Add(AdvancedColorControls, "red", "红色通道 · %", 75, 110, 1, 100);
        Add(AdvancedColorControls, "green", "绿色通道 · %", 75, 110, 1, 100);
        Add(AdvancedColorControls, "blue", "蓝色通道 · %", 75, 110, 1, 100);
        ColorPreset.PlaceholderText = "自定义参数";
        ColorPreset.SelectedIndex = 0;
    }
    private DisplayColorSettings ReadColorValues() => new(_colorSliders["temperature"].Value,
        _colorSliders["brightness"].Value, _colorSliders["contrast"].Value, _colorSliders["gamma"].Value,
        _colorSliders["red"].Value, _colorSliders["green"].Value, _colorSliders["blue"].Value);
    private void SetColorValues(DisplayColorSettings settings)
    {
        _loadingColor = true;
        try
        {
            double[] values = [settings.Temperature, settings.Brightness, settings.Contrast, settings.Gamma, settings.Red, settings.Green, settings.Blue];
            string[] keys = ["temperature", "brightness", "contrast", "gamma", "red", "green", "blue"];
            for (int i = 0; i < keys.Length; i++) _colorSliders[keys[i]].Value = values[i];
            ColorPreset.SelectedIndex = -1;
            for (int i = 0; i < ColorPreset.Items.Count; i++)
                if (DisplayColorSettings.Preset((string)((ComboBoxItem)ColorPreset.Items[i]).Tag) == settings) { ColorPreset.SelectedIndex = i; break; }
        }
        finally { _loadingColor = false; }
    }
    private void ColorValueChanged()
    {
        if (_loadingColor) return;
        ColorPreset.SelectedIndex = -1;
        ColorEffectStatus.Text = "参数已修改，点击“应用到所选屏幕”预览。";
    }
    private void ColorPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingColor || ColorPreset.SelectedItem is not ComboBoxItem { Tag: string id } || _colorSliders.Count < 7) return;
        SetColorValues(DisplayColorSettings.Preset(id));
        ColorEffectStatus.Text = "预设已选好，点击“应用到所选屏幕”预览。";
    }
    private void ResetColorValues_Click(object sender, RoutedEventArgs e) { SetColorValues(new()); ColorEffectStatus.Text = "参数已重置，尚未应用。"; }
    private void RefreshColorEditor(DisplayInspection display)
    {
        StopColorPreview();
        var existing = DisplayColorAdjustment.Find(display.InterfaceId);
        SetColorValues(existing?.Settings ?? new());
        ColorEffectStatus.Text = existing is null ? "参数尚未应用" : "显示当前会话的颜色参数。";
        bool sdr = display.AdvancedColorActive == false;
        ColorCapability.IsOpen = !sdr || display.Mirrored;
        ColorCapability.Message = !sdr ? "此屏幕处于 HDR / 高级颜色模式，或系统未能确认颜色状态；滤镜与 SDR 配置暂不可用。"
            : "此屏幕使用镜像输出，颜色调节也会作用于共享同一输出的屏幕。";
        ColorEditor.IsEnabled = ApplyColorButton.IsEnabled = sdr;
        RestoreColorButton.IsEnabled = existing is not null;
        ApplyProfileButton.IsEnabled = false;
        RestoreProfileButton.IsEnabled = DisplayColorProfiles.CanRestore(display);
        ProfileName.Text = string.IsNullOrEmpty(display.ProfilePath) ? "系统默认" : System.IO.Path.GetFileName(display.ProfilePath);
        ProfileDetails.Text = DisplayInspectionService.ProfileDescription(display.ProfilePath).Split('\n')[0];
        _ = LoadColorProfilesAsync(display, ++_colorRevision);
    }
    private async Task LoadColorProfilesAsync(DisplayInspection display, long revision)
    {
        ColorProfilePicker.IsEnabled = false;
        ColorProfilePicker.Items.Clear();
        try
        {
            var options = await Task.Run(() => DisplayColorProfiles.List(display));
            if (!_loaded || revision != _colorRevision) return;
            foreach (var option in options) ColorProfilePicker.Items.Add(option);
            int selected = options.FindIndex(p => !p.IsNative && string.Equals(p.Path, display.ProfilePath, StringComparison.OrdinalIgnoreCase));
            if (options.Count > 0) ColorProfilePicker.SelectedIndex = Math.Max(0, selected);
            else ProfileChoiceDescription.Text = "没有发现可用的显示器配置。";
        }
        catch (Exception ex) { if (revision == _colorRevision) ProfileChoiceDescription.Text = $"无法读取可用配置：{ex.Message}"; }
        finally { if (revision == _colorRevision) ColorProfilePicker.IsEnabled = !_colorBusy; }
    }
    private void ColorProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ColorProfilePicker.SelectedItem is not DisplayProfileOption option) return;
        ProfileChoiceDescription.Text = option.IsNative
            ? "自动读取这块屏幕的三原色、白点与 Gamma，生成并安装原生 ICC 描述，无需寻找或导入文件；未提供 Gamma 时采用 2.2。"
            : option.Name.Contains("sRGB") ? "使用系统内置的 sRGB 配置；适合本身接近 sRGB 的屏幕，不会扩大或限制屏幕的物理色域。"
            : "使用此系统已安装配置作为所选屏幕的默认 SDR 配置，请选择与屏幕匹配的型号。";
        ApplyProfileButton.IsEnabled = !_colorBusy && Selected?.AdvancedColorActive == false;
    }
    private void SetColorBusy(bool busy)
    {
        _colorBusy = busy;
        DisplayPicker.IsEnabled = RefreshButton.IsEnabled = !busy;
        ColorEditor.IsEnabled = !busy && Selected?.AdvancedColorActive == false;
        ApplyColorButton.IsEnabled = !busy && Selected?.AdvancedColorActive == false;
        ApplyProfileButton.IsEnabled = !busy && Selected?.AdvancedColorActive == false && ColorProfilePicker.SelectedItem is DisplayProfileOption;
        ColorProfilePicker.IsEnabled = !busy;
        RestoreColorButton.IsEnabled = !busy && Selected is { } d && DisplayColorAdjustment.Find(d.InterfaceId) is not null;
        RestoreProfileButton.IsEnabled = !busy && Selected is { } p && DisplayColorProfiles.CanRestore(p);
    }
    private async void ApplyColor_Click(object sender, RoutedEventArgs e)
    {
        if (_colorBusy || Selected is not { } display) return;
        var settings = ReadColorValues(); SetColorBusy(true);
        try
        {
            var session = await Task.Run(async () => { await DisplayInspectionService.RefreshAdvancedColorAsync(display); var s = DisplayColorAdjustment.Get(display); s.Preview(settings); return s; });
            if (!_loaded || Selected?.InterfaceId != display.InterfaceId) { session.CancelPreview(); return; }
            _pendingColor = session;
            ColorEffectStatus.Text = "效果预览中，请在全屏窗口确认是否保留。";
            _colorConfirmation = new DisplayColorConfirmationPage(display, session, ActualTheme);
            var result = await _colorConfirmation.ShowAsync();
            if (_loaded)
            {
                SetColorValues(session.Settings);
                ColorEffectStatus.Text = result.Kept ? "效果已保留；退出工具箱时恢复原始曲线。" : "未保留此次调整，已恢复到预览前。";
                if (result.Error is not null) { ColorEffectStatus.Text = "此次调整未确认完成。"; Notice(result.Error, InfoBarSeverity.Error); }
            }
        }
        catch (Exception ex) { Notice($"未能应用滤镜：{ex.Message}", InfoBarSeverity.Error); }
        finally { StopColorPreview(); SetColorBusy(false); }
    }
    private void StopColorPreview()
    {
        _colorConfirmation?.Cancel(); _colorConfirmation = null;
        _pendingColor?.CancelPreview(); _pendingColor = null;
    }
    private async void RestoreColor_Click(object sender, RoutedEventArgs e)
    {
        if (_colorBusy || Selected is not { } display) return;
        StopColorPreview(); SetColorBusy(true);
        try { await Task.Run(() => DisplayColorAdjustment.Reset(display.InterfaceId)); SetColorValues(new()); ColorEffectStatus.Text = "已恢复本工具调整前的原始效果。"; }
        catch (Exception ex) { Notice(ex.Message, InfoBarSeverity.Error); }
        finally { SetColorBusy(false); }
    }
    private async void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_colorBusy || Selected is not { } display || ColorProfilePicker.SelectedItem is not DisplayProfileOption option) return;
        StopColorPreview(); SetColorBusy(true);
        try
        {
            string path = await Task.Run(async () => { await DisplayInspectionService.RefreshAdvancedColorAsync(display); DisplayColorAdjustment.Reset(display.InterfaceId); return DisplayColorProfiles.Apply(display, option); });
            display.ProfilePath = path; RefreshColorEditor(display);
            Notice("颜色配置已应用到所选屏幕，支持色彩管理的程序将使用该配置。", InfoBarSeverity.Success);
        }
        catch (Exception ex) { RefreshColorEditor(display); Notice($"未能应用颜色配置：{ex.Message}", InfoBarSeverity.Error); }
        finally { SetColorBusy(false); }
    }
    private async void RestoreProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_colorBusy || Selected is not { } display) return;
        StopColorPreview(); SetColorBusy(true);
        try
        {
            display.ProfilePath = await Task.Run(() => { DisplayColorAdjustment.Reset(display.InterfaceId); DisplayColorProfiles.Restore(display); return DisplayInspectionService.GetProfile(display.DeviceName); });
            RefreshColorEditor(display); Notice("已恢复本工具更改前的颜色配置。", InfoBarSeverity.Success);
        }
        catch (Exception ex) { Notice(ex.Message, InfoBarSeverity.Error); }
        finally { SetColorBusy(false); }
    }
}
