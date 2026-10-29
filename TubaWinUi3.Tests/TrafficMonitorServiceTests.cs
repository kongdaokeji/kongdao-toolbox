using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class TrafficMonitorServiceTests
{
    private static TrafficConnectionInfo Conn(string key, long speedIn, long speedOut)
        => new() { Key = key, SpeedIn = speedIn, SpeedOut = speedOut };

    [Fact]
    public void SortAndCap_OrdersByActivityDescending_AndCaps()
    {
        var list = new[]
        {
            Conn("a", 10, 0),
            Conn("b", 0, 5),
            Conn("c", 1, 100),
            Conn("d", 0, 0)
        };

        var result = TrafficMonitorService.SortAndCap(list, 2);

        Assert.Equal(["c", "a"], result.Select(x => x.Key));
    }

    [Fact]
    public void SortAndCap_TieBreak_ByKey()
    {
        var list = new[] { Conn("b", 5, 5), Conn("a", 5, 5) };

        var result = TrafficMonitorService.SortAndCap(list, 10);

        Assert.Equal(["a", "b"], result.Select(x => x.Key));
    }

    [Fact]
    public void Recorder_Add_KeepsLatestAndCaps()
    {
        var recorder = new TrafficSnapshotRecorder();
        var start = new DateTime(2026, 1, 1, 0, 0, 0);

        for (int i = 0; i < TrafficSnapshotRecorder.MaxSnapshots + 50; i++)
            recorder.Add(new TrafficSnapshot { Time = start.AddSeconds(i) });

        Assert.Equal(TrafficSnapshotRecorder.MaxSnapshots, recorder.Count);
        // 最旧 50 条被丢弃：第一条应为第 51 条
        Assert.True(recorder.TryGet(0, out var first));
        Assert.Equal(start.AddSeconds(50), first.Time);
        // 最新一条保留
        Assert.NotNull(recorder.Latest);
        Assert.Equal(start.AddSeconds(TrafficSnapshotRecorder.MaxSnapshots + 49), recorder.Latest!.Time);
    }

    [Fact]
    public void Recorder_TryGet_OutOfRange_ReturnsFalse()
    {
        var recorder = new TrafficSnapshotRecorder();
        recorder.Add(new TrafficSnapshot { Time = DateTime.Now });

        Assert.False(recorder.TryGet(-1, out _));
        Assert.False(recorder.TryGet(1, out _));
        Assert.True(recorder.TryGet(0, out _));
    }

    [Fact]
    public void Recorder_Clear_Empties()
    {
        var recorder = new TrafficSnapshotRecorder();
        recorder.Add(new TrafficSnapshot { Time = DateTime.Now });

        recorder.Clear();

        Assert.Equal(0, recorder.Count);
        Assert.Null(recorder.Latest);
        Assert.False(recorder.TryGet(0, out _));
    }

    [Fact]
    public void ParseDisplayDnsOutput_ParsesChineseBlocks()
    {
        const string output = """
            Windows IP 配置

                记录名称. . . . . . . : passets-ec.pinterest.com
                ----------------------------------------
                没有 AAAA 类型的记录


                记录名称. . . . . . . : example.com
                记录类型. . . . . . . : 1
                生存时间. . . . . . . : 348826
                数据长度. . . . . . . : 4
                部分. . . . . . . . . : 答案
                A (主机)记录  . . . . : 93.184.216.34


                记录名称. . . . . . . : ipv6.example.com
                记录类型. . . . . . . : 28
                生存时间. . . . . . . : 100
                数据长度. . . . . . . : 16
                部分. . . . . . . . . : 答案
                AAAA 记录  . . . . : 2001:db8::1
            """;

        var map = TrafficMonitorService.ParseDisplayDnsOutput(output);

        Assert.Equal(2, map.Count);
        Assert.Equal("example.com", map["93.184.216.34"]);
        Assert.Equal("ipv6.example.com", map["2001:db8::1"]);
        // 无 A 记录的块不产生映射
        Assert.False(map.ContainsKey("pinterest.com"));
    }

    [Fact]
    public void ParseDisplayDnsOutput_ParsesEnglishBlocks()
    {
        const string output = """
            Windows IP Configuration

                Record Name. . . . . : www.bing.com
                Record Type. . . . . : 1
                Time To Live. . . . . : 300
                Data Length. . . . . : 4
                Section. . . . . . . : Answer
                A (Host) Record. . . : 13.107.21.200
            """;

        var map = TrafficMonitorService.ParseDisplayDnsOutput(output);

        Assert.Equal("www.bing.com", map["13.107.21.200"]);
    }

    [Fact]
    public void ParseDisplayDnsOutput_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(TrafficMonitorService.ParseDisplayDnsOutput(""));
        Assert.Empty(TrafficMonitorService.ParseDisplayDnsOutput("没有输出\n乱码"));
    }
}
