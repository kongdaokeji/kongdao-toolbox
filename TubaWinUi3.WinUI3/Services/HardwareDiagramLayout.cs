namespace TubaWinUi3.Services;

// Randomize once per page, then retain the same positions while the model is being used.
internal static class HardwareDiagramLayout
{
    internal sealed record Card(string Key, string Label, bool Left, double Width, double Height);
    internal sealed record Placement(string Key, double X, double Y, double Width, double Height);
    internal sealed record Layout(IReadOnlyList<Placement> Cards, double Height, double ModelTop);

    internal static double CardWidth(string key, string label)
    {
        uint hash = 2166136261;
        foreach (var character in key) hash = unchecked((hash ^ character) * 16777619);
        return label == "处理器" ? 336 : 280 + hash % 45;
    }

    internal static Layout Arrange(IReadOnlyList<Card> cards, double width, int seed = 0)
    {
        const double gap = 18;
        var area = cards.Sum(card => (card.Width + gap) * (card.Height + gap));
        var height = Math.Max(780, Math.Ceiling((area + 460 * 360) / (width * .67)));
        for (var expansion = 0; ; expansion++, height += 64)
        {
            var modelX = (width - 460) / 2;
            var modelY = (height - 360) / 2;
            for (var attempt = 0; attempt < 18; attempt++)
            {
                var random = new Random(unchecked(seed + expansion * 7919 + attempt * 104729));
                var shuffled = cards.OrderBy(_ => random.Next()).ToArray();
                var phase = random.NextDouble() * Math.PI * 2;
                var angles = shuffled.Select((card, i) => (card.Key,
                    Angle: phase + (i + random.NextDouble() * .45) * Math.PI * 2 / Math.Max(1, cards.Count)))
                    .ToDictionary(pair => pair.Key, pair => pair.Angle);
                var ordered = cards.OrderByDescending(card => card.Width * card.Height * (.9 + random.NextDouble() * .2)).ToArray();
                var placed = new List<Placement>();
                foreach (var card in ordered)
                {
                    var angle = angles[card.Key];
                    var targetX = width / 2 + Math.Cos(angle) * (width / 2 - card.Width / 2 - 16) - card.Width / 2;
                    var targetY = height / 2 + Math.Sin(angle) * (height / 2 - card.Height / 2 - 16) - card.Height / 2;
                    Placement? best = null;
                    var bestScore = double.MaxValue;
                    for (var sample = 0; sample < 520; sample++)
                    {
                        var x = sample == 0 ? targetX : 8 + random.NextDouble() * (width - card.Width - 16);
                        var y = sample == 0 ? targetY : 8 + random.NextDouble() * (height - card.Height - 16);
                        if (x < 8 || y < 8 || x + card.Width > width - 8 || y + card.Height > height - 8) continue;
                        if (Intersects(x, y, card.Width, card.Height, modelX, modelY, 460, 360, gap)) continue;
                        if (placed.Any(other => Intersects(x, y, card.Width, card.Height,
                            other.X, other.Y, other.Width, other.Height, gap))) continue;
                        var score = Math.Pow((x - targetX) / width, 2) + Math.Pow((y - targetY) / height, 2);
                        if (score >= bestScore) continue;
                        bestScore = score;
                        best = new(card.Key, x, y, card.Width, card.Height);
                        if (sample == 0) break;
                    }
                    if (best is null) break;
                    placed.Add(best);
                }
                if (placed.Count == cards.Count) return new(placed, height, modelY);
            }
        }
    }

    private static bool Intersects(double x, double y, double w, double h,
        double otherX, double otherY, double otherW, double otherH, double gap) =>
        x < otherX + otherW + gap && x + w + gap > otherX
        && y < otherY + otherH + gap && y + h + gap > otherY;
}
