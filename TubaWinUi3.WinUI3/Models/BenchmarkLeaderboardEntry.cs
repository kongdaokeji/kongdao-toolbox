using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Models;

public sealed class BenchmarkLeaderboardEntry
{
    public int Rank { get; set; }

    public BenchmarkReportEntry Report { get; set; } = new();

    public Brush RankBrush
    {
        get
        {
            switch (Rank)
            {
                case 1:
                    return new SolidColorBrush(Color.FromArgb(255, 212, 175, 55));
                case 2:
                    return new SolidColorBrush(Color.FromArgb(255, 192, 192, 192));
                case 3:
                    return new SolidColorBrush(Color.FromArgb(255, 205, 127, 50));
                default:
                    if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out object value) && value is Brush brush)
                        return brush;
                    return new SolidColorBrush(Color.FromArgb(255, 0, 99, 177));
            }
        }
    }
}
