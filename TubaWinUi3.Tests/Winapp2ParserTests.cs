using FluentCleaner.Models;
using FluentCleaner.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// Winapp2 规则库解析与 Key 模型测试（FluentCleaner.Core 引擎移植版）。
/// </summary>
public class Winapp2ParserTests
{
    private const string SampleIni = """
        ; Version: 260828
        ; # of entries: 2

        [Winapp2]
        LangSecRef=3025

        [Google Chrome *]
        LangSecRef=3029
        Default=True
        Warning=Removes caches and cookies
        SpecialDetect=DET_CHROME
        FileKey1=%LocalAppData%\Google\Chrome*\User Data\*\Cache|*.*|RECURSE
        FileKey2=%LocalAppData%\Google\Chrome\User Data\Default\Code Cache
        ExcludeKey1=FILE|%LocalAppData%\Google\Chrome\User Data\|Preferences
        RegKey1=HKCU\Software\Google\Chrome\GBE

        [Custom App]
        Section=自定义分类
        DetectFile=%AppData%\CustomApp
        FileKey1=%AppData%\CustomApp\Cache|*.tmp;*.log|REMOVESELF
        RegKey1=HKCU\Software\CustomApp|LastRun
        Default=False
        """;

    [Fact]
    public void Parse_ProducesEntriesAndSkipsHeader()
    {
        var entries = new Winapp2Parser().Parse(SampleIni);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Google Chrome", entries[0].Name);   // trailing " *" stripped
        Assert.Equal("Custom App", entries[1].Name);
    }

    [Fact]
    public void Parse_ParsesChromeEntry()
    {
        var chrome = new Winapp2Parser().Parse(SampleIni)[0];

        Assert.Equal(3029, chrome.LangSecRef);
        Assert.True(chrome.Default);
        Assert.Equal("Removes caches and cookies", chrome.Warning);
        Assert.Equal("DET_CHROME", chrome.SpecialDetect);
        Assert.Equal(2, chrome.FileKeys.Count);
        Assert.Single(chrome.ExcludeKeys);
        Assert.Single(chrome.RegKeys);
    }

    [Fact]
    public void Parse_InvalidEntryWithoutDetectionIsDropped()
    {
        var ini = """
            [Orphan Entry]
            FileKey1=%Temp%|*.tmp|RECURSE
            """;

        var entries = new Winapp2Parser().Parse(ini);
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_RawTextPreserved()
    {
        var chrome = new Winapp2Parser().Parse(SampleIni)[0];
        Assert.Contains("FileKey1", chrome.RawText);
        Assert.Contains("RegKey1", chrome.RawText);
    }

    [Fact]
    public void FileKeyEntry_ParsesPatternsAndFlags()
    {
        var recurse = FileKeyEntry.Parse(@"%LocalAppData%\Temp|*.tmp;*.log|RECURSE");
        Assert.Equal(@"%LocalAppData%\Temp", recurse.Path);
        Assert.Equal("*.tmp;*.log", recurse.Pattern);
        Assert.Equal(FileKeyFlag.Recurse, recurse.Flag);

        var removeSelf = FileKeyEntry.Parse(@"%Temp%|REMOVESELF");
        Assert.Equal("*.*", removeSelf.Pattern);
        Assert.Equal(FileKeyFlag.RemoveSelf, removeSelf.Flag);

        var plain = FileKeyEntry.Parse(@"%Temp%");
        Assert.Equal("*.*", plain.Pattern);
        Assert.Equal(FileKeyFlag.None, plain.Flag);
    }

    [Fact]
    public void RegKeyEntry_ParsesValueName()
    {
        var tree = RegKeyEntry.Parse(@"HKCU\Software\Foo");
        Assert.Equal(@"HKCU\Software\Foo", tree.KeyPath);
        Assert.Null(tree.ValueName);

        var value = RegKeyEntry.Parse(@"HKCU\Software\Foo|LastRun");
        Assert.Equal(@"HKCU\Software\Foo", value.KeyPath);
        Assert.Equal("LastRun", value.ValueName);
    }

    [Fact]
    public void ExcludeKeyEntry_ParsesTypes()
    {
        var file = ExcludeKeyEntry.Parse(@"FILE|%AppData%\Profiles\|places.sqlite");
        Assert.Equal(ExcludeType.File, file.Type);
        Assert.Equal(@"%AppData%\Profiles\", file.Path);
        Assert.Equal("places.sqlite", file.Pattern);

        var path = ExcludeKeyEntry.Parse(@"PATH|%Temp%\Sub");
        Assert.Equal(ExcludeType.Path, path.Type);
        Assert.Null(path.Pattern);

        var reg = ExcludeKeyEntry.Parse(@"REG|HKCU\Software\Protected");
        Assert.Equal(ExcludeType.Reg, reg.Type);
    }

    [Fact]
    public void CategoryResolver_MapsLangSecRefAndFallsBack()
    {
        var chrome = new CleanerEntry { LangSecRef = 3029 };
        Assert.Equal("Google Chrome", CategoryResolver.TryMapLangSecRef(chrome).Name);

        var sectioned = new CleanerEntry { Section = "自定义分类" };
        Assert.Equal("自定义分类", CategoryResolver.TryMapLangSecRef(sectioned).Name);

        var other = new CleanerEntry();
        Assert.Equal("其他应用程序", CategoryResolver.TryMapLangSecRef(other).Name);
    }
}
