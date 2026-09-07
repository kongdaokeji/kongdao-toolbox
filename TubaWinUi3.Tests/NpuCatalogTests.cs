using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class NpuCatalogTests
{
    private const string GenericIntelNpu = "Intel(R) AI Boost";
    private const string GenericAmdNpu = "AMD IPU Device";

    // ---------- Intel Core Ultra ----------

    [Theory]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H")]  // Meteor Lake, NPU 3
    [InlineData("Intel(R) Core(TM) Ultra 9 185H")]
    [InlineData("Intel(R) Core(TM) Ultra 5 125H")]
    [InlineData("Intel(R) Core(TM) Ultra 3 105U")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H 2.10GHz")]
    public void Intel_Series1_Is11Tops(string cpuName)
        => Assert.Equal("11 TOPS", NpuCatalog.LookupTops(GenericIntelNpu, cpuName));

    [Theory]
    [InlineData("Intel(R) Core(TM) Ultra 7 258V")]  // Lunar Lake, NPU 4
    [InlineData("Intel(R) Core(TM) Ultra 9 288V")]
    [InlineData("Intel(R) Core(TM) Ultra 5 226V")]
    [InlineData("Intel(R) Core(TM) Ultra 5 228V")]
    public void Intel_LunarLake_Is48Tops(string cpuName)
        => Assert.Equal("48 TOPS", NpuCatalog.LookupTops(GenericIntelNpu, cpuName));

    [Theory]
    [InlineData("Intel(R) Core(TM) Ultra 9 285K")]  // Arrow Lake, NPU 4
    [InlineData("Intel(R) Core(TM) Ultra 7 265KF")]
    [InlineData("Intel(R) Core(TM) Ultra 7 265H")]
    [InlineData("Intel(R) Core(TM) Ultra 9 275HX")]
    [InlineData("Intel(R) Core(TM) Ultra 5 245H")]
    [InlineData("Intel(R) Core(TM) Ultra 5 230E")]
    public void Intel_ArrowLake_Is13Tops(string cpuName)
        => Assert.Equal("13 TOPS", NpuCatalog.LookupTops(GenericIntelNpu, cpuName));

    [Fact]
    public void Intel_PantherLake_Unknown()
        => Assert.Null(NpuCatalog.LookupTops(GenericIntelNpu, "Intel(R) Core(TM) Ultra 9 375H"));

    [Fact]
    public void Intel_HasNpuAndFullCpuName_Variant()
        => Assert.Equal("48 TOPS", NpuCatalog.LookupTops("Intel(R) AI Boost", "Intel(R) Core(TM) Ultra 7 268V"));

    // ---------- AMD Ryzen ----------

    [Theory]
    [InlineData("AMD Ryzen(TM) AI 9 HX 370")]       // Strix Point
    [InlineData("AMD Ryzen AI 9 HX 375")]
    [InlineData("AMD Ryzen(TM) AI 7 350")]          // Krackan Point
    [InlineData("AMD Ryzen(TM) AI 5 340")]
    [InlineData("AMD Ryzen(TM) AI Max+ 395")]       // Strix Halo
    [InlineData("AMD Ryzen(TM) AI 9 HX 370 5.1GHz")]
    public void Amd_RyzenAi_Is50Tops(string cpuName)
        => Assert.Equal("50 TOPS", NpuCatalog.LookupTops(GenericAmdNpu, cpuName));

    [Theory]
    [InlineData("AMD Ryzen(TM) 7 8845HS")]          // Hawk Point mobile
    [InlineData("AMD Ryzen(TM) 5 8645HS")]
    [InlineData("AMD Ryzen 9 8945HS")]
    [InlineData("AMD Ryzen(TM) 7 8700G")]           // Phoenix desktop
    [InlineData("AMD Ryzen 5 8600G")]
    public void Amd_Series8_Is16Tops(string cpuName)
        => Assert.Equal("16 TOPS", NpuCatalog.LookupTops(GenericAmdNpu, cpuName));

    [Theory]
    [InlineData("AMD Ryzen(TM) 7 7840HS")]          // Phoenix mobile, XDNA 1
    [InlineData("AMD Ryzen 9 7940HS")]
    [InlineData("AMD Ryzen 5 7640U")]
    [InlineData("AMD Ryzen 7 7745HX")]
    public void Amd_Series7_Is10Tops(string cpuName)
        => Assert.Equal("10 TOPS", NpuCatalog.LookupTops(GenericAmdNpu, cpuName));

    // ---------- Qualcomm Snapdragon ----------

    [Theory]
    [InlineData("Qualcomm(R) Hexagon(TM) NPU", "Snapdragon(R) X Elite - X1E-78-100")]
    [InlineData("Qualcomm AI Engine Direct Device", "Snapdragon(R) X Elite X1E-80-100")]
    [InlineData("Qualcomm(R) Hexagon(TM) NPU", "Snapdragon(R) X Plus - X1P-64-100")]
    [InlineData("Snapdragon(R) X Plus - X1P-42-100 CRD", "Snapdragon(R) X Plus - X1P-42-100")]
    [InlineData("Qualcomm(R) Hexagon(TM) NPU", "Snapdragon(R) X Plus 28-core")]
    public void Qualcomm_SnapdragonX_Is45Tops(string npuName, string cpuName)
        => Assert.Equal("45 TOPS", NpuCatalog.LookupTops(npuName, cpuName));

    // ---------- Fallbacks ----------

    [Fact]
    public void UnknownNpu_ReturnsNull()
        => Assert.Null(NpuCatalog.LookupTops("Microsoft Compute Accelerator", "Intel(R) Core(TM) i7-13700K"));

    [Fact]
    public void UnknownIntelGeneration_ReturnsNull()
        => Assert.Null(NpuCatalog.LookupTops(GenericIntelNpu, "Intel(R) Core(TM) Ultra 7 460H"));

    [Fact]
    public void EmptyNames_ReturnsNull()
        => Assert.Null(NpuCatalog.LookupTops(null, null));
}