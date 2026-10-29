using System;
using System.Linq;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>运行库修复：缺失项 → 微软官方安装包映射（RuntimeRepairService.BuildPackages）。</summary>
public class RuntimeRepairServiceTests
{
    [Fact]
    public void V14RegistryMissing_ProducesInstallPackage()
    {
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.VisualCppId,
            ["未检测到 Visual C++ v14 x64 注册项", "未检测到 Visual C++ v14 x86 注册项"]);

        Assert.Contains(packages, p => p.FileName == "vc14_x64.exe" && p.Args.SequenceEqual(["/install", "/quiet", "/norestart"]));
        Assert.Contains(packages, p => p.FileName == "vc14_x86.exe" && p.Args.SequenceEqual(["/install", "/quiet", "/norestart"]));
    }

    [Fact]
    public void V14DllFileMissing_ProducesRepairPackage()
    {
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.VisualCppId,
            ["缺少 System32 (x64)\\vcruntime140.dll", "缺少 SysWOW64 (x86)\\msvcp140.dll"]);

        Assert.Contains(packages, p => p.FileName == "vc14_x64.exe" && p.Args.SequenceEqual(["/repair", "/quiet", "/norestart"]));
        Assert.Contains(packages, p => p.FileName == "vc14_x86.exe" && p.Args.SequenceEqual(["/repair", "/quiet", "/norestart"]));
    }

    [Fact]
    public void LegacyRegistryMissing_ProducesInstallPackage_WithQuietForOldVersions()
    {
        var missing = new[] { "未检测到 Visual C++ 2013 x86 运行库", "未检测到 Visual C++ 2012 x64 运行库", "未检测到 Visual C++ 2010 x64 运行库", "未检测到 Visual C++ 2008 x86 运行库" };
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.VisualCppId, missing);

        Assert.Contains(packages, p => p.FileName == "vc2013_x86.exe" && p.Args.SequenceEqual(["/install", "/quiet", "/norestart"]));
        Assert.Contains(packages, p => p.FileName == "vc2012_x64.exe" && p.Args.SequenceEqual(["/install", "/quiet", "/norestart"]));
        // 2010/2008 用旧式 /q 静默参数
        Assert.Contains(packages, p => p.FileName == "vc2010_x64.exe" && p.Args.SequenceEqual(["/q", "/norestart"]));
        Assert.Contains(packages, p => p.FileName == "vc2008_x86.exe" && p.Args.SequenceEqual(["/q", "/norestart"]));
    }

    [Fact]
    public void LegacyDllFileMissing_ProducesRepairPackage()
    {
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.VisualCppId,
            ["缺少 Visual C++ 2013 x86 SysWOW64 (x86)\\msvcr120.dll"]);

        Assert.Contains(packages, p => p.FileName == "vc2013_x86.exe" && p.Args.SequenceEqual(["/repair", "/quiet", "/norestart"]));
    }

    [Fact]
    public void Legacy2008WinSxSMissing_UsesVc2008Package()
    {
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.VisualCppId,
            ["缺少 Visual C++ 2008 x64 WinSxS\\msvcr90.dll"]);

        // WinSxS 缺 DLL（注册表仍在）→ 修复模式
        Assert.Contains(packages, p => p.FileName == "vc2008_x64.exe" && p.Args.Contains("/repair"));
    }

    [Fact]
    public void DotNet_Produces481OfflinePackage()
    {
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.DotNetId,
            ["未检测到 .NET Framework 4.8.1 Runtime"]);

        var package = Assert.Single(packages);
        Assert.Equal("ndp481-x86-x64-allos-enu.exe", package.FileName);
        Assert.Equal("/repair", package.Args[0]);
    }

    [Fact]
    public void DirectX_ProducesWebSetupPackage()
    {
        var packages = RuntimeRepairService.BuildPackages(RuntimeRepairService.DirectXId,
            ["缺少 System32\\d3dx9_43.dll"]);

        var package = Assert.Single(packages);
        Assert.Equal("dxwebsetup.exe", package.FileName);
        Assert.Equal(["/Q"], package.Args);
    }

    [Fact]
    public void NoMissingVisualCpp_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RuntimeRepairService.BuildPackages(RuntimeRepairService.VisualCppId, []));
    }

    [Fact]
    public void UnknownRuntimeId_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RuntimeRepairService.BuildPackages("unknown", ["x"]));
    }
}