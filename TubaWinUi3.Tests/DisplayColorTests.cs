using System.Buffers.Binary;
using TubaWinUi3.Services.DisplayTools;

namespace TubaWinUi3.Tests;

public class DisplayColorTests
{
    private static ushort[] Linear() => Enumerable.Range(0, 768).Select(i => (ushort)(i % 256 * 257)).ToArray();
    [Fact]
    public void Neutral_PreservesExistingCalibration()
    {
        var baseline = Linear(); baseline[100] = 25000;
        Assert.Equal(baseline, new DisplayColorSettings().BuildRamp(baseline));
    }
    [Theory]
    [InlineData("neutral")][InlineData("warm")][InlineData("cool")][InlineData("soft")][InlineData("bright")][InlineData("contrast")]
    public void Presets_ProduceBoundedMonotonicCurves(string preset)
    {
        var ramp = DisplayColorSettings.Preset(preset).BuildRamp(Linear());
        Assert.Equal(768, ramp.Length);
        for (int c = 0; c < 3; c++) for (int i = 1; i < 256; i++) Assert.True(ramp[c * 256 + i] >= ramp[c * 256 + i - 1]);
    }
    [Fact]
    public void WarmAndCool_HaveDifferentChannelBalance()
    {
        var warm = DisplayColorSettings.Preset("warm").BuildRamp(Linear());
        var cool = DisplayColorSettings.Preset("cool").BuildRamp(Linear());
        Assert.True(warm[200] > warm[712]); Assert.True(cool[200] < cool[712]);
    }
    [Fact]
    public void InvalidSettings_DoNotReachTheDriver()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayColorSettings(Gamma: double.NaN).BuildRamp(Linear()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayColorSettings(Brightness: 0).BuildRamp(Linear()));
        Assert.Throws<ArgumentException>(() => new DisplayColorSettings().BuildRamp([]));
    }
    [Fact]
    public void Preview_CancelReturnsToLastConfirmedCurve_ThenRestoreReturnsToOriginal()
    {
        var hardware = Linear(); var original = (ushort[])hardware.Clone();
        var session = new DisplayGammaSession(() => (ushort[])hardware.Clone(), r => hardware = (ushort[])r.Clone());
        session.Preview(DisplayColorSettings.Preset("warm")); session.Confirm();
        var confirmed = (ushort[])hardware.Clone();
        session.Preview(DisplayColorSettings.Preset("cool")); session.CancelPreview();
        Assert.Equal(confirmed, hardware); Assert.Null(session.PreviewDeadline);
        session.Restore(); Assert.Equal(original, hardware);
    }
    [Fact]
    public void SilentlyIgnoredWrite_IsDetected()
    {
        var hardware = Linear();
        var session = new DisplayGammaSession(() => (ushort[])hardware.Clone(), _ => { });
        Assert.Throws<InvalidOperationException>(() => session.Preview(DisplayColorSettings.Preset("warm")));
        Assert.Null(session.PreviewDeadline); Assert.Equal(Linear(), hardware);
    }
    [Fact]
    public async Task UnconfirmedPreview_RollsBackWithoutUiTimer()
    {
        var hardware = Linear();
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool applied = false;
        var session = new DisplayGammaSession(() => (ushort[])hardware.Clone(), r =>
        {
            hardware = (ushort[])r.Clone();
            if (applied && hardware.SequenceEqual(Linear())) restored.TrySetResult();
            applied = true;
        });
        try
        {
            session.Preview(DisplayColorSettings.Preset("warm"), TimeSpan.FromMilliseconds(50));
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Linear(), hardware);
        }
        finally { session.Restore(); }
    }
    [Fact]
    public void ExpiredPreview_CannotBeKeptEvenBeforeTimerCallbackRuns()
    {
        var hardware = Linear();
        var session = new DisplayGammaSession(() => (ushort[])hardware.Clone(), r => hardware = (ushort[])r.Clone());
        try
        {
            session.Preview(DisplayColorSettings.Preset("warm"), TimeSpan.Zero);
            Assert.Throws<InvalidOperationException>(() => session.Confirm());
            Assert.Equal(Linear(), hardware);
        }
        finally { session.Restore(); }
    }
    [Fact]
    public void ExternalCalibration_IsNotOverwrittenByTimeoutOrShutdown()
    {
        var hardware = Linear();
        var session = new DisplayGammaSession(() => (ushort[])hardware.Clone(), r => hardware = (ushort[])r.Clone());
        session.Preview(DisplayColorSettings.Preset("warm"));
        hardware = DisplayColorSettings.Preset("cool").BuildRamp(Linear());
        var external = (ushort[])hardware.Clone();
        session.CancelPreview(); session.Restore(onlyIfOwned: true);
        Assert.Equal(external, hardware);
    }
    [Fact]
    public void NativeIcc_HasValidTagBoundariesAndD50White()
    {
        var b = DisplayIccProfile.CreateNative(DisplayGamut.Standards[0].Points, new(.3127, .3290), 2.2);
        Assert.True(DisplayColorProfiles.IsRgbDisplayProfile(b));
        Assert.Equal(b.Length, (int)BinaryPrimitives.ReadUInt32BigEndian(b));
        int n = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(128));
        var columns = new List<double[]>();
        for (int i = 0; i < n; i++)
        {
            int entry = 132 + i * 12;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(entry + 4));
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(entry + 8));
            Assert.Equal(0, offset % 4); Assert.InRange(offset, 132 + n * 12, b.Length - size);
            string sig = System.Text.Encoding.ASCII.GetString(b, entry, 4);
            if (sig is "rXYZ" or "gXYZ" or "bXYZ") columns.Add(Enumerable.Range(0, 3).Select(k => BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(offset + 8 + k * 4)) / 65536.0).ToArray());
        }
        double[] d50 = [.9642, 1, .8249];
        for (int i = 0; i < 3; i++) Assert.InRange(columns.Sum(c => c[i]), d50[i] - .0001, d50[i] + .0001);
        Assert.InRange(columns[0][0], .43, .44); // sRGB red adapted to ICC D50
    }
    [Fact]
    public void NativeIcc_RejectsInvalidCharacterization()
    {
        Assert.Throws<ArgumentException>(() => DisplayIccProfile.CreateNative([], new(.3127, .3290), 2.2));
        Assert.Throws<ArgumentException>(() => DisplayIccProfile.CreateNative(DisplayGamut.Standards[0].Points, new(double.NaN, .3290), 2.2));
    }
}
