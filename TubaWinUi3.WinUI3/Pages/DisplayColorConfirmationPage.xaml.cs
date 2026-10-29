using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TubaWinUi3.Services.DisplayTools;
using Windows.System;

namespace TubaWinUi3.Pages;

internal sealed record DisplayColorConfirmationResult(bool Kept, string? Error);

/// <summary>A separate full-screen confirmation on the adjusted display; gamma rollback remains independent of this UI.</summary>
public sealed partial class DisplayColorConfirmationPage : Page
{
    private readonly DisplayInspection _display;
    private readonly DisplayGammaSession _session;
    private readonly TaskCompletionSource<DisplayColorConfirmationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Window? _window;
    private bool _kept, _finished;
    private string? _error;
    private int _lastSeconds = -1;

    internal DisplayColorConfirmationPage(DisplayInspection display, DisplayGammaSession session, ElementTheme theme)
    {
        InitializeComponent();
        _display = display; _session = session; RequestedTheme = theme;
        DisplayName.Text = $"已调整：{display.Name}";
        _timer.Tick += (_, _) => UpdateCountdown();
        Loaded += (_, _) => KeepButton.Focus(FocusState.Programmatic);
    }

    internal async Task<DisplayColorConfirmationResult> ShowAsync()
    {
        try
        {
            _window = new Window { Title = "保留屏幕调整？" };
            _window.Closed += (_, _) =>
            {
                _timer.Stop();
                if (!_kept) _session.CancelPreview();
                _finished = true;
                _completion.TrySetResult(new(_kept, _error ?? _session.RestoreError));
            };
            _window.Content = this;
            _window.AppWindow.MoveAndResize(_display.Bounds);
            _window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _window.Activate();
            _timer.Start(); UpdateCountdown();
            return await _completion.Task;
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    private void UpdateCountdown()
    {
        if (_finished) return;
        if (_session.PreviewDeadline is not { } deadline) { Cancel(); return; }
        double remaining = Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalSeconds);
        int seconds = (int)Math.Ceiling(remaining);
        if (_lastSeconds != seconds) { Countdown.Text = $"{seconds} 秒后自动恢复原状"; _lastSeconds = seconds; }
        TimeRemaining.Value = remaining;
        if (remaining <= 0) Cancel();
    }

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        if (_finished) return;
        try { _session.Confirm(); _kept = true; Finish(); }
        catch (Exception ex) { _error = ex.Message; Cancel(); }
    }
    private void Restore_Click(object sender, RoutedEventArgs e) => Cancel();
    private void Confirmation_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { e.Handled = true; Cancel(); }
    }
    internal void Cancel()
    {
        if (_finished) return;
        _session.CancelPreview();
        Finish();
    }
    private void Finish()
    {
        if (_finished) return;
        _finished = true; _timer.Stop();
        if (_window is not null) _window.Close();
        else _completion.TrySetResult(new(false, _error ?? _session.RestoreError));
    }
}
