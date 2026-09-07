using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using TubaWinUi3.Controls;

namespace TubaWinUi3.Pages;

public sealed partial class TestPage : Page
{
    public TestPage()
    {
        InitializeComponent();

        var rand = new Random();
        var points = new List<GraphDataPoint>();
        for (int i = 0; i < 30; i++)
        {
            points.Add(new GraphDataPoint(i, 30 + 40 * Math.Sin(i * 0.4) + rand.NextDouble() * 10));
        }
        TestSparkline.DataPoints = points;
    }
}
