using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwareDiagramLayoutTests
{
    [Theory]
    [InlineData(2, 3, 1)]
    [InlineData(2, 3, 28)]
    [InlineData(6, 8, 7)]
    public void FullCards_ScatterOnFourSidesWithoutOverlapping(int disks, int networks, int seed)
    {
        var cards = new List<HardwareDiagramLayout.Card>();
        void Add(string label, bool left, int count = 1, double height = 105)
        {
            for (var i = 1; i <= count; i++)
            {
                var key = $"{label}_{i}";
                cards.Add(new(key, label, left, HardwareDiagramLayout.CardWidth(key, label), height + i * 9));
            }
        }
        Add("处理器", true); Add("内存", true); Add("硬盘", true, disks);
        Add("主板", true); Add("BIOS", true); Add("声卡", true);
        Add("显示器", false, 2); Add("显卡", false, 2); Add("电池", false, height: 190);
        Add("系统", false); Add("NPU", false); Add("网卡", false, networks);
        var layout = HardwareDiagramLayout.Arrange(cards, 1132, seed);
        Assert.Equal(cards.Count, layout.Cards.Count);
        Assert.Equal(layout.Cards.ToArray(), HardwareDiagramLayout.Arrange(cards, 1132, seed).Cards.ToArray());
        Assert.NotEqual(layout.Cards.ToArray(), HardwareDiagramLayout.Arrange(cards, 1132, seed + 1).Cards.ToArray());
        foreach (var card in layout.Cards)
        {
            Assert.InRange(card.X, 0, 1132 - card.Width);
            Assert.InRange(card.Y, 0, layout.Height - card.Height);
            foreach (var other in layout.Cards.Where(other => other.Key != card.Key))
                Assert.False(card.X < other.X + other.Width && card.X + card.Width > other.X
                    && card.Y < other.Y + other.Height && card.Y + card.Height > other.Y,
                    $"{card.Key} overlaps {other.Key}");
            Assert.False(card.X < 796 && card.X + card.Width > 336
                && card.Y < layout.ModelTop + 360 && card.Y + card.Height > layout.ModelTop,
                $"{card.Key} overlaps the model");
        }
        Assert.Contains(layout.Cards, card => card.X + card.Width < 336);
        Assert.Contains(layout.Cards, card => card.X > 796);
        Assert.Contains(layout.Cards, card => card.Y + card.Height < layout.ModelTop);
        Assert.Contains(layout.Cards, card => card.Y > layout.ModelTop + 360);
    }
}
