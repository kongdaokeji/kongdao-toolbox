using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class SetupWizardDialog : ContentDialog
{
    private int _currentStep;
    private bool _compactMode;
    private BackdropType _backdropType = BackdropType.Mica;

    private readonly Border[] _layoutOptions = [];
    private readonly Border[] _backdropOptions = [];

    public SetupWizardDialog()
    {
        InitializeComponent();

        _layoutOptions = [CardModeOption, CompactModeOption];
        _backdropOptions = [BackdropMicaOption, BackdropMicaAltOption, BackdropAcrylicOption];

        UpdateStepUI();
    }

    private void UpdateStepUI()
    {
        Step0Content.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step1Content.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Content.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Content.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

        StepPager.SelectedPageIndex = _currentStep;

        switch (_currentStep)
        {
            case 0:
                StepTitleText.Text = "欢迎使用图吧工具箱";
                StepSubtitleText.Text = "请仔细阅读以下重要信息。";
                PrimaryButtonText = "下一步";
                SecondaryButtonText = "上一步";
                IsSecondaryButtonEnabled = false;
                CloseButtonText = "跳过";
                break;
            case 1:
                StepTitleText.Text = "选择显示方式";
                StepSubtitleText.Text = "您可以随时在设置中更改此选项。";
                PrimaryButtonText = "下一步";
                SecondaryButtonText = "上一步";
                IsSecondaryButtonEnabled = true;
                CloseButtonText = "跳过";
                break;
            case 2:
                StepTitleText.Text = "选择背景材质";
                StepSubtitleText.Text = "不同的材质会为窗口带来不同的视觉效果。";
                PrimaryButtonText = "下一步";
                SecondaryButtonText = "上一步";
                IsSecondaryButtonEnabled = true;
                CloseButtonText = "跳过";
                break;
            case 3:
                StepTitleText.Text = "搜索集成";
                StepSubtitleText.Text = "让工具出现在 Windows 搜索中，方便快速启动。";
                PrimaryButtonText = "完成";
                SecondaryButtonText = "上一步";
                IsSecondaryButtonEnabled = true;
                CloseButtonText = "跳过";
                break;
        }
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_currentStep < 3)
        {
            args.Cancel = true;
            _currentStep++;
            UpdateStepUI();
        }
        else
        {
            ApplySettings();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_currentStep > 0)
        {
            _currentStep--;
            UpdateStepUI();
        }
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        AppSettings.Set("SetupCompleted", true);
    }

    private void LayoutOption_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var tag = border.Tag?.ToString();
        _compactMode = tag == "Compact";
        UpdateLayoutOptionSelection(border);
    }

    private void UpdateLayoutOptionSelection(Border selected)
    {
        foreach (var border in _layoutOptions)
        {
            if (border is null) continue;
            var isSelected = border == selected;
            border.BorderBrush = isSelected
                ? (Brush)App.Current.Resources["AccentFillColorDefaultBrush"]
                : (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void BackdropOption_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        if (!Enum.TryParse<BackdropType>(border.Tag?.ToString(), out var type)) return;
        _backdropType = type;
        UpdateBackdropOptionSelection(border);
    }

    private void UpdateBackdropOptionSelection(Border selected)
    {
        foreach (var border in _backdropOptions)
        {
            if (border is null) continue;
            var isSelected = border == selected;
            border.BorderBrush = isSelected
                ? (Brush)App.Current.Resources["AccentFillColorDefaultBrush"]
                : (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void ApplySettings()
    {
        CompactModeService.SetCompactModeEnabled(_compactMode);
        BackdropService.SetBackdropType(_backdropType);

        var enableSearchIndex = WizardSearchIndexToggle.IsOn;
        AppSettings.Set("WindowsSearchIndex", enableSearchIndex);
        if (enableSearchIndex)
            _ = WindowsSearchIndexService.RegisterAllToolsAsync();

        AppSettings.Set("SetupCompleted", true);
    }
}
