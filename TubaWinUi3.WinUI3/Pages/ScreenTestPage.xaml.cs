using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using WinUIColor = Windows.UI.Color;

namespace TubaWinUi3.Pages;

public sealed partial class ScreenTestPage : Page
{
    private readonly Window _window;
    private int _currentIndex;
    private DispatcherTimer? _hintTimer;
    private bool _hintVisible;
    private bool _hintShownOnce;

    private static readonly WinUIColor[] SolidColors =
    [
        Colors.White,
        Colors.Red,
        Colors.Green,
        Colors.Blue,
        Colors.Cyan,
        Colors.Magenta,
        Colors.Yellow,
        WinUIColor.FromArgb(255, 128, 128, 128),
        Colors.Black
    ];

    private static readonly string[] SolidColorNames =
    [
        "白色", "红色", "绿色", "蓝色", "青色", "品红", "黄色", "灰色", "黑色"
    ];

    private static readonly (string Name, Func<WriteableBitmap> Draw)[] PatternGenerators =
    [
        ("漏光测试", DrawLightBleed),
        ("干扰测试", DrawInterference),
        ("对焦测试", DrawFocus),
        ("呼吸效应", DrawBreathing),
        ("对比度 - 亮", DrawContrastBright),
        ("对比度 - 暗", DrawContrastDark),
        ("色阶 - 灰", DrawGrayscaleRamp),
        ("色阶 - 红", DrawRedRamp),
        ("色阶 - 绿", DrawGreenRamp),
        ("色阶 - 蓝", DrawBlueRamp),
        ("饱和度", DrawSaturation),
        ("网格", DrawGrid),
        ("棋盘格", DrawCheckerboard),
    ];

    private int TotalSlides => SolidColors.Length + PatternGenerators.Length;

    public ScreenTestPage(Window window)
    {
        InitializeComponent();
        _window = window;
        _currentIndex = 0;

        HintBorder.Visibility = Visibility.Collapsed;
        HintText.Text = BuildHint();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        ShowCurrentSlide();
    }

    private string BuildHint()
    {
        var name = GetSlideName(_currentIndex);
        return $"← → 切换  |  ESC 退出  |  {name}  ({_currentIndex + 1}/{TotalSlides})";
    }

    private string GetSlideName(int index)
    {
        if (index < SolidColors.Length)
            return SolidColorNames[index];
        return PatternGenerators[index - SolidColors.Length].Name;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                _hintTimer?.Stop();
                try { _window.AppWindow.SetPresenter(AppWindowPresenterKind.Default); } catch { }
                _window.Close();
                break;
            case VirtualKey.Right:
            case VirtualKey.Down:
            case VirtualKey.Space:
                _currentIndex = (_currentIndex + 1) % TotalSlides;
                ShowCurrentSlide();
                break;
            case VirtualKey.Left:
            case VirtualKey.Up:
                _currentIndex = (_currentIndex - 1 + TotalSlides) % TotalSlides;
                ShowCurrentSlide();
                break;
        }
        e.Handled = true;
    }

    private void ShowCurrentSlide()
    {
        HintText.Text = BuildHint();

        if (!_hintShownOnce)
        {
            _hintShownOnce = true;
            ShowHint();
        }

        if (_currentIndex < SolidColors.Length)
        {
            DrawCanvas.Visibility = Visibility.Collapsed;
            RootGrid.Background = new SolidColorBrush(SolidColors[_currentIndex]);
        }
        else
        {
            RootGrid.Background = new SolidColorBrush(Colors.Black);
            DrawCanvas.Visibility = Visibility.Visible;
            DrawPattern(PatternGenerators[_currentIndex - SolidColors.Length].Draw);
        }
    }

    private void DrawPattern(Func<WriteableBitmap> drawFunc)
    {
        var bmp = drawFunc();
        DrawCanvas.Children.Clear();
        var img = new Image { Source = bmp, Stretch = Stretch.Fill };
        DrawCanvas.Children.Add(img);
        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        img.Width = DrawCanvas.ActualWidth > 0 ? DrawCanvas.ActualWidth : ActualWidth;
        img.Height = DrawCanvas.ActualHeight > 0 ? DrawCanvas.ActualHeight : ActualHeight;
    }

    private double SlideOffset => ActualWidth > 0 ? ActualWidth : 1920;

    private void ShowHint()
    {
        _hintTimer?.Stop();
        _hintVisible = true;
        HintBorder.Visibility = Visibility.Visible;
        HintBorder.Opacity = 1;

        var slideIn = new DoubleAnimation
        {
            From = SlideOffset,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideIn, HintTranslate);
        Storyboard.SetTargetProperty(slideIn, "X");

        var sb = new Storyboard();
        sb.Children.Add(slideIn);
        sb.Begin();

        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            HideHint();
        };
        _hintTimer.Start();
    }

    private void HideHint()
    {
        _hintVisible = false;

        var slideOut = new DoubleAnimation
        {
            From = 0,
            To = SlideOffset,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slideOut, HintTranslate);
        Storyboard.SetTargetProperty(slideOut, "X");

        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(350)
        };
        Storyboard.SetTarget(fadeOut, HintBorder);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");

        var sb = new Storyboard();
        sb.Children.Add(slideOut);
        sb.Children.Add(fadeOut);
        sb.Completed += (_, _) =>
        {
            if (!_hintVisible) HintBorder.Visibility = Visibility.Collapsed;
        };
        sb.Begin();
    }

    private static void SetPixel(byte[] buffer, int w, int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = (y * w + x) * 4;
        buffer[i] = b;
        buffer[i + 1] = g;
        buffer[i + 2] = r;
        buffer[i + 3] = a;
    }

    private static void WriteBuf(WriteableBitmap bmp, byte[] buf)
    {
        using var stream = bmp.PixelBuffer.AsStream();
        stream.Write(buf, 0, buf.Length);
    }

    private static WriteableBitmap DrawLightBleed()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = (double)x / w - 0.5;
                double dy = (double)y / h - 0.5;
                double dist = Math.Sqrt(dx * dx + dy * dy) * 2.0;
                double brightness = Math.Max(0, 1.0 - dist * 0.8);
                byte v = (byte)(brightness * 255);
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawInterference()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        var rand = new Random(42);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)(rand.Next(2) * 255);
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawFocus()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int cx = w / 2, cy = h / 2;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int dx = Math.Abs(x - cx);
                int dy = Math.Abs(y - cy);
                bool line = (dx + dy) % 4 < 2;
                byte v = line ? (byte)255 : (byte)0;
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawBreathing()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int cx = w / 2, cy = h / 2;
        int maxR = Math.Min(w, h) / 2;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double distRatio = dist / maxR;
                double brightness = Math.Max(0, 1.0 - distRatio);
                byte v = (byte)(brightness * 255);
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawContrastBright()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int cols = 10;
        int colW = w / cols;
        byte[] levels = [175, 183, 191, 199, 207, 215, 223, 231, 239, 247];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int col = Math.Min(x / colW, cols - 1);
                byte v = levels[col];
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawContrastDark()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int cols = 10;
        int colW = w / cols;
        byte[] levels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int col = Math.Min(x / colW, cols - 1);
                byte v = levels[col];
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawGrayscaleRamp()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)((double)x / w * 255);
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawRedRamp()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)((double)x / w * 255);
                SetPixel(buf, w, x, y, v, 0, 0);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawGreenRamp()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)((double)x / w * 255);
                SetPixel(buf, w, x, y, 0, v, 0);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawBlueRamp()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)((double)x / w * 255);
                SetPixel(buf, w, x, y, 0, 0, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawSaturation()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int cols = 7;
        int colW = w / cols;
        byte[,] rgb = {
            {255,0,0}, {255,127,0}, {255,255,0},
            {0,255,0}, {0,0,255}, {75,0,130}, {148,0,211}
        };
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int col = Math.Min(x / colW, cols - 1);
                SetPixel(buf, w, x, y, rgb[col, 0], rgb[col, 1], rgb[col, 2]);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawGrid()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int step = 40;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool onGrid = x % step == 0 || y % step == 0;
                byte v = onGrid ? (byte)200 : (byte)30;
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }

    private static WriteableBitmap DrawCheckerboard()
    {
        int w = 1920, h = 1080;
        var bmp = new WriteableBitmap(w, h);
        var buf = new byte[w * h * 4];
        int size = 40;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool check = (x / size + y / size) % 2 == 0;
                byte v = check ? (byte)255 : (byte)0;
                SetPixel(buf, w, x, y, v, v, v);
            }
        }
        WriteBuf(bmp, buf);
        return bmp;
    }
}
