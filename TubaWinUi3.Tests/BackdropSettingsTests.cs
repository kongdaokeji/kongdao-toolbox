using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Tests;

public class BackdropSettingsTests
{
    [Theory]
    [InlineData("#FF202020", 0x20, 0x20, 0x20)]
    [InlineData("#202020", 0x20, 0x20, 0x20)]
    [InlineData("202020", 0x20, 0x20, 0x20)]
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]
    [InlineData("#000000", 0x00, 0x00, 0x00)]
    public void TryParseColor_RgbFormats(string input, byte r, byte g, byte b)
    {
        Assert.True(BackdropSettings.TryParseColor(input, out var color));
        Assert.Equal((byte)255, color.A);
        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Theory]
    [InlineData("#80FF0000")]
    [InlineData("80FF0000")]
    public void TryParseColor_ArgbFormats(string input)
    {
        Assert.True(BackdropSettings.TryParseColor(input, out var color));
        Assert.Equal((byte)0x80, color.A);
        Assert.Equal((byte)0xFF, color.R);
        Assert.Equal((byte)0x00, color.G);
        Assert.Equal((byte)0x00, color.B);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("#ABC")]
    [InlineData("#GGGGGG")]
    [InlineData("#FF20202")] // 奇数长度
    [InlineData("12345")]
    public void TryParseColor_InvalidInputs(string? input)
    {
        Assert.False(BackdropSettings.TryParseColor(input, out _));
    }

    [Fact]
    public void ParseColor_FallsBackOnInvalid()
    {
        var fallback = Color.FromArgb(255, 1, 2, 3);
        Assert.Equal(fallback, BackdropSettings.ParseColor("invalid", fallback));
        Assert.Equal(fallback, BackdropSettings.ParseColor(null, fallback));
    }

    [Fact]
    public void FormatColor_OpaqueDropsAlpha()
    {
        var color = Color.FromArgb(255, 32, 32, 32);
        Assert.Equal("#202020", BackdropSettings.FormatColor(color));
    }

    [Fact]
    public void FormatColor_TransparentKeepsAlpha()
    {
        var color = Color.FromArgb(0x80, 255, 0, 0);
        Assert.Equal("#80FF0000", BackdropSettings.FormatColor(color));
    }

    [Fact]
    public void FormatColor_RoundTrips()
    {
        var color = Color.FromArgb(0x7F, 0xAB, 0xCD, 0xEF);
        Assert.Equal(color, BackdropSettings.ParseColor(BackdropSettings.FormatColor(color), Color.FromArgb(0, 0, 0, 0)));
    }

    [Fact]
    public void DeriveFallbackColor_ForcesOpaque()
    {
        var tint = Color.FromArgb(0x40, 10, 20, 30);
        var fallback = BackdropSettings.DeriveFallbackColor(tint);
        Assert.Equal((byte)255, fallback.A);
        Assert.Equal((byte)10, fallback.R);
        Assert.Equal((byte)20, fallback.G);
        Assert.Equal((byte)30, fallback.B);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    public void Clamp01_ClampsToUnitRange(double input, double expected)
    {
        Assert.Equal(expected, BackdropSettings.Clamp01(input));
    }

    [Fact]
    public void Customization_FallbackColorDerivedFromTint()
    {
        var customization = new BackdropCustomization(true, Color.FromArgb(0x80, 12, 34, 56), 0.6, 0.3);
        Assert.Equal(BackdropSettings.DeriveFallbackColor(customization.TintColor), customization.FallbackColor);
        Assert.Equal((byte)255, customization.FallbackColor.A);
    }

    [Fact]
    public void Customization_DefaultsAreWithinRange()
    {
        Assert.InRange(BackdropSettings.DefaultTintOpacity, 0.0, 1.0);
        Assert.InRange(BackdropSettings.DefaultLuminosityOpacity, 0.0, 1.0);
        Assert.Equal((byte)255, BackdropSettings.DefaultTintColor.A);
    }
}
