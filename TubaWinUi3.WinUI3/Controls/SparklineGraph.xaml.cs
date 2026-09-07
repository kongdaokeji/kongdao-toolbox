using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;
using Windows.Foundation;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using XamlEllipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace TubaWinUi3.Controls;

public sealed partial class SparklineGraph : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty DataPointsProperty = DependencyProperty.Register(
        nameof(DataPoints), typeof(IList<GraphDataPoint>), typeof(SparklineGraph),
        new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
        nameof(LineColor), typeof(Color), typeof(SparklineGraph),
        new PropertyMetadata(Color.FromArgb(255, 76, 110, 245), OnVisualChanged));

    public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(
        nameof(FillOpacity), typeof(double), typeof(SparklineGraph),
        new PropertyMetadata(0.15, OnVisualChanged));

    public static readonly DependencyProperty LabelFormatProperty = DependencyProperty.Register(
        nameof(LabelFormat), typeof(string), typeof(SparklineGraph),
        new PropertyMetadata("{0:0}", OnVisualChanged));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(SparklineGraph),
        new PropertyMetadata("", OnVisualChanged));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(SparklineGraph),
        new PropertyMetadata(1.5, OnVisualChanged));

    public IList<GraphDataPoint>? DataPoints
    {
        get => (IList<GraphDataPoint>?)GetValue(DataPointsProperty);
        set => SetValue(DataPointsProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    public string LabelFormat
    {
        get => (string)GetValue(LabelFormatProperty);
        set => SetValue(LabelFormatProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private List<Point> _screenPoints = [];

    public SparklineGraph()
    {
        InitializeComponent();
        DataContext = this;
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        ActualThemeChanged += OnThemeChanged;
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => DrawGraph();
    private void OnThemeChanged(FrameworkElement sender, object args) => DrawGraph();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => DrawGraph();

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SparklineGraph)d).DrawGraph();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SparklineGraph)d).DrawGraph();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_screenPoints.Count < 2 || DataPoints == null || DataPoints.Count < 2)
        {
            TooltipBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var pos = e.GetCurrentPoint(GraphCanvas).Position;
        var w = ActualWidth;

        var step = w / (_screenPoints.Count - 1);
        var idx = (int)Math.Round(pos.X / step);
        if (idx < 0) idx = 0;
        if (idx >= _screenPoints.Count) idx = _screenPoints.Count - 1;

        var dp = DataPoints[idx];
        var valStr = string.Format(LabelFormat, dp.Y);
        TooltipText.Text = $"{valStr}{Unit}";

        var tipX = _screenPoints[idx].X + 12;
        var tipY = _screenPoints[idx].Y - 28;
        if (tipX + 80 > w) tipX = _screenPoints[idx].X - 80;
        if (tipY < 0) tipY = _screenPoints[idx].Y + 8;

        TooltipBorder.SetValue(Canvas.LeftProperty, tipX);
        TooltipBorder.SetValue(Canvas.TopProperty, tipY);
        TooltipBorder.Visibility = Visibility.Visible;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        TooltipBorder.Visibility = Visibility.Collapsed;
    }

    private void DrawGraph()
    {
        var canvas = GraphCanvas;
        if (canvas == null || DataPoints == null || DataPoints.Count < 2) return;

        canvas.Children.Clear();
        _screenPoints.Clear();

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double minY = double.MaxValue, maxY = double.MinValue;

        foreach (var p in DataPoints)
        {
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        double rangeY = maxY - minY;
        if (rangeY == 0) rangeY = 1;

        var padding = 2.0;
        var drawH = h - padding * 2;

        var points = new List<Point>();
        for (int i = 0; i < DataPoints.Count; i++)
        {
            double x = (double)i / (DataPoints.Count - 1) * w;
            double y = padding + drawH - ((DataPoints[i].Y - minY) / rangeY) * drawH;
            points.Add(new Point(x, y));
        }
        _screenPoints = points;

        var lc = LineColor;

        var areaPath = new XamlPath
        {
            Fill = new SolidColorBrush(Color.FromArgb((byte)(FillOpacity * 255), lc.R, lc.G, lc.B)),
        };

        var areaFigure = new PathFigure { StartPoint = points[0] };
        for (int i = 1; i < points.Count; i++)
            areaFigure.Segments.Add(new LineSegment { Point = points[i] });
        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[^1].X, h) });
        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[0].X, h) });
        areaFigure.Segments.Add(new LineSegment { Point = points[0] });

        var areaGeo = new PathGeometry();
        areaGeo.Figures.Add(areaFigure);
        areaPath.Data = areaGeo;
        canvas.Children.Add(areaPath);

        var linePath = new XamlPath
        {
            Stroke = new SolidColorBrush(lc),
            StrokeThickness = StrokeThickness,
        };

        var lineFigure = new PathFigure { StartPoint = points[0] };
        for (int i = 1; i < points.Count; i++)
            lineFigure.Segments.Add(new LineSegment { Point = points[i] });

        var lineGeo = new PathGeometry();
        lineGeo.Figures.Add(lineFigure);
        linePath.Data = lineGeo;
        canvas.Children.Add(linePath);

        if (points.Count > 0)
        {
            var last = points[^1];
            var dot = new XamlEllipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(lc),
                Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
                StrokeThickness = 1.5,
            };
            canvas.Children.Add(dot);
            Canvas.SetLeft(dot, last.X - 3);
            Canvas.SetTop(dot, last.Y - 3);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public struct GraphDataPoint
{
    public double X { get; }
    public double Y { get; }

    public GraphDataPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
