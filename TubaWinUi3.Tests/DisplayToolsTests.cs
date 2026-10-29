using TubaWinUi3.Services.DisplayTools;

namespace TubaWinUi3.Tests;

public class DisplayToolsTests
{
    [Fact]
    public void Summary_UsesCoverageRatherThanAreaToAssessRange()
    {
        var text=DisplayGamut.Describe([new("sRGB",80,140),new("DCI-P3",60,120)]);
        Assert.Contains("范围较有限",text); Assert.DoesNotContain("覆盖较完整",text);
    }
    [Fact]
    public void Summary_DistinguishesStandardAndWideGamutCoverage()
    {
        var standard=DisplayGamut.Describe([new("sRGB",99.8,99.9),new("DCI-P3",73.6,73.6)]);
        Assert.Contains("部分广色域颜色",standard);
        var wide=DisplayGamut.Describe([new("sRGB",100,140),new("DCI-P3",98,102)]);
        Assert.Contains("两者覆盖较完整",wide);
    }
    [Fact]
    public void Summary_MissingDataDoesNotInventAssessment()
    {
        Assert.Contains("无法判断",DisplayGamut.Describe([]));
        Assert.Contains("无法判断",DisplayGamut.Describe([new("sRGB",double.NaN,100),new("DCI-P3",98,102)]));
    }
    [Fact]
    public void EveryReferenceHasItsOwnExplanation()
    {
        var explanations=DisplayGamut.Standards.Select(s=>DisplayGamut.Explain(s.Name)).ToArray();
        Assert.Equal(5,explanations.Distinct().Count());
        Assert.Contains("不能直接换算",DisplayGamut.Explain("NTSC (1953)"));
        Assert.Contains("不能据此判定支持 HDR",DisplayGamut.Explain("Rec.2020"));
    }
    [Fact]
    public void SameGamut_HasFullCoverageAndEqualArea()
    {
        var g=DisplayGamut.Calculate(DisplayGamut.Standards[0].Points)[0];
        Assert.Equal(100,g.Coverage,8); Assert.Equal(100,g.AreaRatio,8);
    }
    [Fact]
    public void LargerGamut_CoverageIsClippedButAreaIsNot()
    {
        var g=DisplayGamut.Calculate(DisplayGamut.Standards[1].Points)[0];
        Assert.Equal(100,g.Coverage,8); Assert.True(g.AreaRatio>130);
    }
    [Fact]
    public void ReversedTriangle_WorksAndInvalidCoordinatesAreRejected()
    {
        var p=DisplayGamut.Standards[0].Points.Reverse().ToArray();
        Assert.Equal(100,DisplayGamut.Calculate(p)[0].Coverage,8);
        Assert.Empty(DisplayGamut.Calculate([new(0,0),new(0,0),new(0,0)]));
        Assert.Empty(DisplayGamut.Calculate([new(double.NaN,.3),new(.3,.6),new(.15,.06)]));
    }
    [Fact]
    public void DisjointTriangles_HaveZeroCoverage()
    {
        var g=DisplayGamut.Calculate([new(.02,.8),new(.04,.8),new(.02,.83)])[0];
        Assert.Equal(0,g.Coverage,8);
    }
    [Fact]
    public void BadEdid_IsNotPresentedAsDeviceCapabilities()
    {
        Assert.False(DisplayEdid.Parse([]).Valid);
        var b=BaseEdid(); b[20]^=1;
        Assert.False(DisplayEdid.Parse(b).Valid);
    }
    [Fact]
    public void BaseEdid_DecodesLittleEndianProductAndGamma()
    {
        var d=DisplayEdid.Parse(BaseEdid());
        Assert.True(d.Valid); Assert.Equal("DEL",d.Manufacturer); Assert.Equal("0x1234",d.Product);
        Assert.Equal("2.20",d.Gamma); Assert.Equal("1.4",d.Version);
    }
    [Fact]
    public void CtaHdr_RequiresValidChecksumAndDoesNotReadPastBlock()
    {
        var b=new byte[256]; BaseEdid().CopyTo(b,0); b[126]=1; FixChecksum(b,0);
        b[128]=2; b[129]=3; b[130]=8; b[132]=0xE3; b[133]=6; b[134]=12; b[135]=1;
        FixChecksum(b,128);
        var d=DisplayEdid.Parse(b); Assert.True(d.HdrPq); Assert.Contains("HLG",d.HdrFormats);
        b[135]^=1; d=DisplayEdid.Parse(b); Assert.False(d.HdrPq); Assert.Contains("校验失败",d.Warning);
        b[130]=5; b[132]=0xFF; FixChecksum(b,128); Assert.False(DisplayEdid.Parse(b).HdrPq);
    }
    [Fact]
    public void MissingExtension_IsReported()
    {
        var b=BaseEdid(); b[126]=1; FixChecksum(b,0);
        Assert.Contains("不完整",DisplayEdid.Parse(b).Warning);
    }
    private static byte[] BaseEdid()
    {
        var b=new byte[128]; new byte[]{0,255,255,255,255,255,255,0}.CopyTo(b,0);
        b[8]=0x10; b[9]=0xAC; b[10]=0x34; b[11]=0x12; b[17]=35; b[18]=1; b[19]=4; b[20]=128; b[23]=120;
        FixChecksum(b,0); return b;
    }
    private static void FixChecksum(byte[] b,int start) { b[start+127]=0; int sum=0; for(int i=start;i<start+127;i++) sum+=b[i]; b[start+127]=unchecked((byte)-sum); }
}
