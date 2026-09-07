using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;
using Windows.Foundation;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using XamlRun = Microsoft.UI.Xaml.Documents.Run;
using XamlSpan = Microsoft.UI.Xaml.Documents.Span;

namespace TubaWinUi3.Controls;

public sealed partial class HealthGauge : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(HealthGauge),
        new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(HealthGauge),
        new PropertyMetadata("健康状态"));


    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set
        {
            SetValue(ValueProperty, value);
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(PointerBrush));
        }
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string StatusLabel
    {
        get
        {
            if (Value < 20) return "报废";
            if (Value < 60) return "较差";
            if (Value <= 90) return "良好";
            return "正常";
        }
    }

    public SolidColorBrush PointerBrush
    {
        get
        {
            if (Value < 20) return new SolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0x00, 0x00));
            if (Value < 60) return new SolidColorBrush(Color.FromArgb(0xFF, 0xF4, 0x8F, 0x2C));
            return new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x7B, 0xFF));
        }
    }

    private Color GetArcColor()
    {
        if (Value < 20) return Color.FromArgb(0xFF, 0xF4, 0x00, 0x00);
        if (Value < 60) return Color.FromArgb(0xFF, 0xF4, 0x8F, 0x2C);
        return Color.FromArgb(0xFF, 0x3A, 0x7B, 0xFF);
    }

    private Color GetArcColorMid()
    {
        if (Value < 20) return Color.FromArgb(0xFF, 0xF5, 0x4D, 0x4C);
        if (Value < 60) return Color.FromArgb(0xFF, 0xF8, 0xB7, 0x77);
        return Color.FromArgb(0xFF, 0x6A, 0xA4, 0xFF);
    }

    private Color GetArcColorLight()
    {
        if (Value < 20) return Color.FromArgb(0xFF, 0xF8, 0xA4, 0xA4);
        if (Value < 60) return Color.FromArgb(0xFF, 0xF7, 0xAF, 0xA7);
        return Color.FromArgb(0xFF, 0xAE, 0xDC, 0xFF);
    }

    public HealthGauge()
    {
        InitializeComponent();
        DataContext = this;
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        ActualThemeChanged += OnThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => DrawGauge();
    private void OnThemeChanged(FrameworkElement sender, object args) => DrawGauge();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => DrawGauge();

    private void DrawGauge()
    {
        var canvas = GaugeCanvas;
        if (canvas == null) return;

        canvas.Children.Clear();

        double w = canvas.Width;
        double h = canvas.Height;
        double cx = w / 2;
        double cy = h / 2;

        var isDark = ActualTheme == ElementTheme.Dark;
        var outerTickColor = isDark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00);
        var innerTrackColor = isDark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0x00, 0x00, 0x00);

        double outerRadius = cx * 0.92;
        double innerRadius = cx * 0.75;
        double arcWidth = 14;
        double gradientWidth = 26;

        double startAngle = 0;
        double totalAngle = 360;
        double valueAngle = (Value / 100.0) * totalAngle;

        DrawOuterTicks(canvas, cx, cy, outerRadius, outerTickColor);
        DrawTrackArc(canvas, cx, cy, innerRadius, arcWidth, innerTrackColor, startAngle, totalAngle);

        if (Value > 0)
        {
            DrawValueArc(canvas, cx, cy, innerRadius, arcWidth, GetArcColor(), startAngle, valueAngle);
            DrawGradientArc(canvas, cx, cy, innerRadius, gradientWidth, startAngle, valueAngle);
        }

        var run1 = new XamlRun { Text = ((int)Value).ToString(), FontSize = 40, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var run2 = new XamlRun { Text = "%", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        var inline1 = new XamlSpan { FontSize = 40, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        inline1.Inlines.Add(run1);
        var inline2 = new XamlSpan { FontSize = 40, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        inline2.Inlines.Add(run2);

        var richText = new RichTextBlock
        {
            FontSize = 40,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();
        paragraph.Inlines.Add(inline1);
        paragraph.Inlines.Add(inline2);
        richText.Blocks.Add(paragraph);

        Canvas.SetLeft(richText, cx - 40);
        Canvas.SetTop(richText, cy - 25);
        canvas.Children.Add(richText);
    }

    private void DrawOuterTicks(Canvas canvas, double cx, double cy, double radius, Color color)
    {
        double tickWidth = 8;
        double gapAngle = 2;
        double tickAngle = 360.0 / 12 - gapAngle;

        for (int i = 0; i < 12; i++)
        {
            double start = i * (360.0 / 12);
            double end = start + tickAngle;
            var path = CreateArcPath(cx, cy, radius, tickWidth, color, start, end);
            canvas.Children.Add(path);
        }
    }

    private void DrawTrackArc(Canvas canvas, double cx, double cy, double radius, double strokeWidth, Color color, double startAngle, double sweepAngle)
    {
        double gapSize = 2;
        double segmentAngle = 360.0 / 4 - gapSize;
        double[] segmentStarts = { 0, 90 + gapSize, 180 + gapSize, 270 + gapSize };

        foreach (var segStart in segmentStarts)
        {
            var path = CreateArcPath(cx, cy, radius, strokeWidth, color, segStart, segStart + segmentAngle);
            canvas.Children.Add(path);
        }
    }

    private void DrawValueArc(Canvas canvas, double cx, double cy, double radius, double strokeWidth, Color color, double startAngle, double sweepAngle)
    {
        if (sweepAngle <= 0) return;
        var path = CreateArcPath(cx, cy, radius, strokeWidth, color, startAngle, startAngle + sweepAngle);
        canvas.Children.Add(path);
    }

    private void DrawGradientArc(Canvas canvas, double cx, double cy, double radius, double strokeWidth, double startAngle, double sweepAngle)
    {
        if (sweepAngle <= 0) return;

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        gradient.GradientStops.Add(new GradientStop { Color = GetArcColor(), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = GetArcColorMid(), Offset = 0.5 });
        gradient.GradientStops.Add(new GradientStop { Color = GetArcColorLight(), Offset = 1.0 });

        var path = CreateArcPath(cx, cy, radius, strokeWidth, startAngle, startAngle + sweepAngle);
        path.Stroke = gradient;
        path.StrokeThickness = strokeWidth;
        path.Fill = null;
        canvas.Children.Add(path);
    }

    private XamlPath CreateArcPath(double cx, double cy, double radius, double strokeWidth, Color color, double startAngle, double endAngle)
    {
        var path = new XamlPath
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = strokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        var figure = new PathFigure();
        double startRad = DegreesToRadians(startAngle - 90);
        double endRad = DegreesToRadians(endAngle - 90);

        double x1 = cx + radius * Math.Cos(startRad);
        double y1 = cy + radius * Math.Sin(startRad);
        double x2 = cx + radius * Math.Cos(endRad);
        double y2 = cy + radius * Math.Sin(endRad);

        figure.StartPoint = new Point(x1, y1);

        double sweep = endAngle - startAngle;
        if (sweep >= 360) sweep = 359.9;
        bool isLarge = sweep > 180;

        var arc = new ArcSegment
        {
            Point = new Point(x2, y2),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLarge,
        };

        figure.Segments.Add(arc);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        path.Data = geometry;

        return path;
    }

    private XamlPath CreateArcPath(double cx, double cy, double radius, double strokeWidth, double startAngle, double endAngle)
    {
        return CreateArcPath(cx, cy, radius, strokeWidth, Color.FromArgb(0, 0, 0, 0), startAngle, endAngle);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (HealthGauge)d;
        g.OnPropertyChanged(nameof(StatusLabel));
        g.OnPropertyChanged(nameof(PointerBrush));
        g.DrawGauge();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum HealthGaugeLevel
{
    Critical,
    Warning,
    Good,
    Excellent
}
