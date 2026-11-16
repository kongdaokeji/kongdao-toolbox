using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KongdaoTools.AiXamlCard;

/// <summary>
/// 卡片可用的固定配色。宿主在加载卡片前按当前主题设置 <see cref="IsDark"/>，
/// 之后所有令牌解析成对应主题的具体颜色，模型不必自己写颜色字面量。
/// </summary>
public static class CardPalette
{
    private static readonly Dictionary<string, (string Light, string Dark)> Map = new(StringComparer.Ordinal)
    {
        ["KongdaoText"] = ("#1B1B1B", "#FFFFFF"),
        ["KongdaoMuted"] = ("#5D5D5D", "#C7C7C7"),
        ["KongdaoCard"] = ("#FFFFFF", "#2B2B2B"),
        ["KongdaoSubtle"] = ("#F3F3F3", "#323232"),
        ["KongdaoStroke"] = ("#D6D6D6", "#3A3A3A"),
        ["KongdaoAccent"] = ("#0067C0", "#60CDFF"),
        ["KongdaoGood"] = ("#0F7B0F", "#6CCB5F"),
        ["KongdaoWarn"] = ("#9D5D00", "#FCE100"),
        ["KongdaoBad"] = ("#C42B1C", "#FF99A4")
    };

    public static bool IsDark { get; set; }

    public static IReadOnlyCollection<string> Tokens => Map.Keys;

    /// <summary>令牌名（如 <c>KongdaoText</c>）→ 当前主题的颜色字面量；未知令牌返回 null。</summary>
    public static string? Token(string name) =>
        Map.TryGetValue(name, out var pair) ? (IsDark ? pair.Dark : pair.Light) : null;

    public static Color ColorOf(string name)
    {
        var text = (Token(name) ?? "#000000").TrimStart('#');
        var value = Convert.ToUInt32(text, 16);
        return text.Length == 8
            ? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static Brush Brush(string name) => new SolidColorBrush(ColorOf(name));

    /// <summary>XAML 里写的是颜色字面量；代码里用这个把令牌换成对应的 Brush。</summary>
    public static Brush BrushFor(string name) => Brush(name);

    public static string Text => Token("KongdaoText")!;
    public static string Muted => Token("KongdaoMuted")!;
    public static string Card => Token("KongdaoCard")!;
    public static string Subtle => Token("KongdaoSubtle")!;
    public static string Stroke => Token("KongdaoStroke")!;
    public static string Accent => Token("KongdaoAccent")!;
    public static string Good => Token("KongdaoGood")!;
    public static string Warn => Token("KongdaoWarn")!;
    public static string Bad => Token("KongdaoBad")!;

    public static Brush TextBrush => BrushFor("KongdaoText");
    public static Brush MutedBrush => BrushFor("KongdaoMuted");
    public static Brush CardBrush => BrushFor("KongdaoCard");
    public static Brush SubtleBrush => BrushFor("KongdaoSubtle");
    public static Brush StrokeBrush => BrushFor("KongdaoStroke");
    public static Brush AccentBrush => BrushFor("KongdaoAccent");
    public static Brush GoodBrush => BrushFor("KongdaoGood");
    public static Brush WarnBrush => BrushFor("KongdaoWarn");
    public static Brush BadBrush => BrushFor("KongdaoBad");
}
