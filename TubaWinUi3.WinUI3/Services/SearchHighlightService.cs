using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace TubaWinUi3.Services;

public static class SearchHighlightService
{
    private static readonly ConditionalWeakTable<Border, DispatcherQueueTimer> _activeTimers = new();

    public static void HighlightBorder(Border border)
    {
        if (border is null) return;

        if (_activeTimers.TryGetValue(border, out var existingTimer))
        {
            existingTimer.Stop();
            _activeTimers.Remove(border);
        }

        var originalBorderBrush = border.BorderBrush;
        var originalBorderThickness = border.BorderThickness;

        border.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        border.BorderThickness = new Thickness(2);

        BounceBorder(border);

        var timer = border.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2.5);
        timer.Tick += (s, e) =>
        {
            ((DispatcherQueueTimer)s!).Stop();
            _activeTimers.Remove(border);
            border.BorderBrush = originalBorderBrush;
            border.BorderThickness = originalBorderThickness;
        };

        _activeTimers.Add(border, timer);
        timer.Start();
    }

    private static void BounceBorder(Border border)
    {
        if (FastModeService.IsFastModeEnabled()) return;

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(border);
        if (visual is null) return;

        var compositor = visual.Compositor;

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleAnimation.InsertKeyFrame(0.15f, new System.Numerics.Vector3(1.03f, 1.03f, 1f));
        scaleAnimation.InsertKeyFrame(0.35f, new System.Numerics.Vector3(0.98f, 0.98f, 1f));
        scaleAnimation.InsertKeyFrame(0.5f, new System.Numerics.Vector3(1.015f, 1.015f, 1f));
        scaleAnimation.InsertKeyFrame(0.65f, new System.Numerics.Vector3(0.995f, 0.995f, 1f));
        scaleAnimation.InsertKeyFrame(0.8f, new System.Numerics.Vector3(1.005f, 1.005f, 1f));
        scaleAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleAnimation.Duration = TimeSpan.FromMilliseconds(800);

        visual.CenterPoint = new System.Numerics.Vector3(
            (float)border.ActualSize.X / 2,
            (float)border.ActualSize.Y / 2, 0f);
        visual.StartAnimation("Scale", scaleAnimation);
    }
}
