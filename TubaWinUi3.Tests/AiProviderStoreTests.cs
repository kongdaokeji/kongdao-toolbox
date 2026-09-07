using TubaWinUi3.Services;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 提供商存储（AiProviderStore / AiService 配置解析）单元测试。
/// 使用临时文件 + 静态路径覆盖，不触碰真实用户数据。
/// </summary>
public class AiProviderStoreTests : IDisposable
{
    private readonly string _path;

    public AiProviderStoreTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TubaAiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "ai_providers.json");
        ResetStore(_path, legacy: null);
    }

    public void Dispose()
    {
        ResetStore(null, legacy: null);
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private static void ResetStore(string? path, Dictionary<string, string>? legacy)
    {
        AiProviderStore.InvalidateCache();
        AiProviderStore.StoragePathOverride = path;
        AiProviderStore.LegacyGet = legacy is null
            ? static _ => null
            : key => legacy.TryGetValue(key, out var v) ? v : null;
    }

    private static void AssertProvider(AiProvider p, string id, string name, string endpoint, bool locked)
    {
        Assert.Equal(id, p.Id);
        Assert.Equal(name, p.Name);
        Assert.Equal(endpoint, p.BaseUrl);
        Assert.Equal(locked, p.EndpointLocked);
        Assert.True(p.IsPreset);
    }

    [Fact]
    public void Defaults_ContainFourPresets()
    {
        var providers = AiProviderStore.GetProviders();

        Assert.Equal(4, providers.Count);
        AssertProvider(providers[0], "custom", "小图吧自带模型", "", locked: false);
        Assert.Equal("auto", providers[0].DefaultModel);
        Assert.Single(providers[0].Models);

        var deepseek = providers[1];
        AssertProvider(deepseek, "deepseek", "DeepSeek", "https://api.deepseek.com", locked: true);
        Assert.Equal("deepseek-v4-pro", deepseek.DefaultModel);
        Assert.Contains(deepseek.Models, m => m.Id == "deepseek-v4-pro");
        Assert.Contains(deepseek.Models, m => m.Id == "deepseek-v4-flash");
        Assert.Equal("https://platform.deepseek.com/api_keys", deepseek.KeyHintUrl);

        var mimo = providers[2];
        AssertProvider(mimo, "mimo", "小米 MiMo", "https://api.xiaomimimo.com/v1", locked: true);
        Assert.Equal("mimo-v2.5-pro", mimo.DefaultModel);
        Assert.Equal("https://platform.xiaomimimo.com/#/console/api-keys", mimo.KeyHintUrl);

        var zen = providers[3];
        AssertProvider(zen, "opencode", "OpenCode Zen", "https://opencode.ai/zen/v1", locked: true);
        Assert.True(zen.Models.Count >= 4);
        Assert.All(zen.Models, m => Assert.True(AiProviderStore.IsFreeModelId(m.Id)));
    }

    [Fact]
    public void Defaults_Unconfigured_SelectsTubaBuiltinModel()
    {
        // 全新安装/未配置：默认使用小图吧自带模型（内置端点，无需 Key）
        Assert.Equal(AiProviderStore.CustomProviderId, AiProviderStore.SelectedProviderId);
        Assert.Equal("auto", AiProviderStore.SelectedModelId);
        Assert.True(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        var providers = AiProviderStore.GetProviders();
        var custom = providers.First(p => p.Id == "custom");
        custom.BaseUrl = "https://my-gateway.example/v1";
        custom.AddModel("my-model-1");
        custom.AddModel("my-model-2");
        custom.DefaultModel = "my-model-2";
        AiProviderStore.Save();

        // 重新加载（清缓存）后内容一致
        ResetStore(_path, legacy: null);
        providers = AiProviderStore.GetProviders();
        custom = providers.First(p => p.Id == "custom");
        Assert.Equal("https://my-gateway.example/v1", custom.BaseUrl);
        Assert.Equal("my-model-2", custom.DefaultModel);
        Assert.Contains(custom.Models, m => m.Id == "my-model-1");
        Assert.Contains(custom.Models, m => m.Id == "my-model-2");
    }

    [Fact]
    public void SetSelected_PersistsAndResolvesDefault()
    {
        AiProviderStore.SetSelected("deepseek");
        Assert.Equal("deepseek", AiProviderStore.SelectedProviderId);
        Assert.Equal("deepseek-v4-pro", AiProviderStore.SelectedModelId);

        AiProviderStore.SetSelected("deepseek", "deepseek-v4-flash");
        Assert.Equal("deepseek-v4-flash", AiProviderStore.SelectedModelId);

        // 重新加载后选中状态仍保留
        ResetStore(_path, legacy: null);
        Assert.Equal("deepseek", AiProviderStore.SelectedProviderId);
        Assert.Equal("deepseek-v4-flash", AiProviderStore.SelectedModelId);
    }

    [Fact]
    public void LegacyMigration_SeedsCustomProvider()
    {
        var legacy = new Dictionary<string, string>
        {
            ["AiApiEndpoint"] = "https://old.example/v1",
            ["AiModelName"] = "old-model",
            ["AiApiKey"] = "sk-old-key",
        };
        ResetStore(_path, legacy);

        var custom = AiProviderStore.GetProvider("custom")!;
        Assert.Equal("https://old.example/v1", custom.BaseUrl);
        Assert.Equal("sk-old-key", custom.ApiKey);
        Assert.Equal("old-model", custom.DefaultModel);
        Assert.Contains(custom.Models, m => m.Id == "old-model");
        Assert.Equal("old-model", AiProviderStore.SelectedModelId);
    }

    [Fact]
    public void LegacyMigration_EmptySelectsTubaBuiltin()
    {
        // 旧版未配置任何服务 → 默认使用小图吧自带模型
        var custom = AiProviderStore.GetProvider("custom")!;
        Assert.Equal("", custom.BaseUrl);
        Assert.Equal("", custom.ApiKey);
        Assert.Equal("auto", custom.DefaultModel);
        Assert.Equal(AiProviderStore.CustomProviderId, AiProviderStore.SelectedProviderId);
        Assert.True(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void LegacyFile_EmptyCustom_KeepsTubaBuiltin()
    {
        // 模拟旧版文件（无 defaultTubaMigrated 标记）：选中「小图吧自带模型」且未配置 → 保持小图吧默认
        File.WriteAllText(_path, """
            {"version":1,"selectedProviderId":"custom","selectedModelId":"auto","providers":[
              {"id":"custom","name":"小图吧自带模型","baseUrl":"","apiKey":"","isPreset":true,"endpointLocked":false,"defaultModel":"auto","models":[{"id":"auto","label":"自动"}]},
              {"id":"opencode","name":"OpenCode Zen","baseUrl":"https://opencode.ai/zen/v1","isPreset":true,"endpointLocked":true,"defaultModel":"deepseek-v4-flash-free","models":[{"id":"deepseek-v4-flash-free"}]}
            ]}
            """);
        ResetStore(_path, legacy: null);

        Assert.Equal(AiProviderStore.CustomProviderId, AiProviderStore.SelectedProviderId);
        Assert.Equal("auto", AiProviderStore.SelectedModelId);
        Assert.True(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void LegacyFile_EmptyCustomProvider_MigratesToTubaBuiltin()
    {
        // 旧版选中空白自定义提供商（如「新建自定义」后未填写）→ 归位到小图吧自带模型
        File.WriteAllText(_path, """
            {"version":1,"selectedProviderId":"custom-2","selectedModelId":"","providers":[
              {"id":"custom","name":"小图吧自带模型","baseUrl":"","apiKey":"","isPreset":true,"endpointLocked":false,"defaultModel":"auto","models":[{"id":"auto","label":"自动"}]},
              {"id":"custom-2","name":"自定义 2","baseUrl":"","apiKey":"","isPreset":false,"endpointLocked":false,"defaultModel":"","models":[]},
              {"id":"opencode","name":"OpenCode Zen","baseUrl":"https://opencode.ai/zen/v1","isPreset":true,"endpointLocked":true,"defaultModel":"deepseek-v4-flash-free","models":[{"id":"deepseek-v4-flash-free"}]}
            ]}
            """);
        ResetStore(_path, legacy: null);

        Assert.Equal(AiProviderStore.CustomProviderId, AiProviderStore.SelectedProviderId);
        Assert.Equal("auto", AiProviderStore.SelectedModelId);
    }

    [Fact]
    public void LegacyFile_ZenAutoDefault_MigratesBackToTubaBuiltin()
    {
        // 旧版一次性迁移留下的 OpenCode Zen 自动默认（无 Key、仍是默认免费模型）→ 归位到小图吧
        File.WriteAllText(_path, """
            {"version":1,"selectedProviderId":"opencode","selectedModelId":"deepseek-v4-flash-free","defaultMigrated":true,"providers":[
              {"id":"custom","name":"小图吧自带模型","baseUrl":"","apiKey":"","isPreset":true,"endpointLocked":false,"defaultModel":"auto","models":[{"id":"auto","label":"自动"}]},
              {"id":"opencode","name":"OpenCode Zen","baseUrl":"https://opencode.ai/zen/v1","isPreset":true,"endpointLocked":true,"defaultModel":"deepseek-v4-flash-free","models":[{"id":"deepseek-v4-flash-free"},{"id":"mimo-v2.5-free"}]}
            ]}
            """);
        ResetStore(_path, legacy: null);

        Assert.Equal(AiProviderStore.CustomProviderId, AiProviderStore.SelectedProviderId);
        Assert.Equal("auto", AiProviderStore.SelectedModelId);
        Assert.True(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void LegacyFile_OpenCodeZenManuallyConfigured_NotMigrated()
    {
        // 用户手动选了 OpenCode Zen 的非默认模型 → 不被强制归位
        File.WriteAllText(_path, """
            {"version":1,"selectedProviderId":"opencode","selectedModelId":"mimo-v2.5-free","defaultMigrated":true,"providers":[
              {"id":"custom","name":"小图吧自带模型","baseUrl":"","apiKey":"","isPreset":true,"endpointLocked":false,"defaultModel":"auto","models":[{"id":"auto","label":"自动"}]},
              {"id":"opencode","name":"OpenCode Zen","baseUrl":"https://opencode.ai/zen/v1","isPreset":true,"endpointLocked":true,"defaultModel":"deepseek-v4-flash-free","models":[{"id":"deepseek-v4-flash-free"},{"id":"mimo-v2.5-free"}]}
            ]}
            """);
        ResetStore(_path, legacy: null);

        Assert.Equal(AiProviderStore.OpenCodeZenProviderId, AiProviderStore.SelectedProviderId);
        Assert.Equal("mimo-v2.5-free", AiProviderStore.SelectedModelId);
    }

    [Fact]
    public void Migration_OneTime_ManualSwitchBackSticks()
    {
        // 迁移只执行一次：之后用户手动切回空白自定义，重启不再被强制切走
        AiProviderStore.SetSelected("custom", "auto");
        Assert.Equal("custom", AiProviderStore.SelectedProviderId);

        ResetStore(_path, legacy: null); // 模拟重启

        Assert.Equal("custom", AiProviderStore.SelectedProviderId);
        Assert.Equal("auto", AiProviderStore.SelectedModelId);
        Assert.True(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void ExistingFile_ConfiguredCustom_KeepsSelection()
    {
        var custom = AiProviderStore.GetProvider("custom")!;
        custom.BaseUrl = "https://my-gateway.example/v1";
        custom.ApiKey = "sk-mine";
        AiProviderStore.Save();
        AiProviderStore.SetSelected("custom", "auto");

        ResetStore(_path, legacy: null); // 模拟重启

        Assert.Equal("custom", AiProviderStore.SelectedProviderId);
        Assert.Equal("auto", AiProviderStore.SelectedModelId);
        Assert.False(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void GetConfig_ResolvesSelectedProvider()
    {
        var deepseek = AiProviderStore.GetProvider("deepseek")!;
        deepseek.ApiKey = "sk-deepseek-test";
        AiProviderStore.Save();
        AiProviderStore.SetSelected("deepseek", "deepseek-v4-flash");

        var (endpoint, model, apiKey) = AiService.GetConfig();
        Assert.Equal("https://api.deepseek.com", endpoint);
        Assert.Equal("deepseek-v4-flash", model);
        Assert.Equal("sk-deepseek-test", apiKey);
    }

    [Fact]
    public void GetConfig_CustomBlank_FallsBackToDefaults()
    {
        AiProviderStore.SetSelected("custom", "auto");
        var (endpoint, model, apiKey) = AiService.GetConfig();
        Assert.Equal(AiService.DefaultEndpoint, endpoint);
        Assert.Equal(AiService.DefaultModel, model);
        Assert.Equal(AiService.DefaultApiKey, apiKey);
        Assert.True(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void GetConfig_OpenCodeZen_BlankKeyUsesPublicAnonymous()
    {
        AiProviderStore.SetSelected("opencode");
        var (_, _, apiKey) = AiService.GetConfig();
        Assert.Equal("public", apiKey);
        Assert.False(AiService.IsUsingDefaultModel);
    }

    [Fact]
    public void IsFreeModelId_FiltersFreeModels()
    {
        Assert.True(AiProviderStore.IsFreeModelId("deepseek-v4-flash-free"));
        Assert.True(AiProviderStore.IsFreeModelId("big-pickle"));
        Assert.True(AiProviderStore.IsFreeModelId("MIMO-V2.5-FREE"));
        Assert.False(AiProviderStore.IsFreeModelId("gpt-5.5"));
        Assert.False(AiProviderStore.IsFreeModelId("deepseek-v4-flash"));
        Assert.False(AiProviderStore.IsFreeModelId(""));
    }

    [Fact]
    public void AddCustomProvider_CreatesUniqueIdAndSelects()
    {
        var first = AiProviderStore.AddCustomProvider();
        Assert.StartsWith("custom-", first.Id);
        Assert.Equal(first.Id, AiProviderStore.SelectedProviderId);
        // 新自定义提供商完全留空（地址/模型/默认模型均空）
        Assert.Equal("", first.BaseUrl);
        Assert.Empty(first.Models);
        Assert.Equal("", first.DefaultModel);

        var second = AiProviderStore.AddCustomProvider();
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, AiProviderStore.SelectedProviderId);
        Assert.Equal(6, AiProviderStore.GetProviders().Count);
    }

    [Fact]
    public void ResetProviderDefaults_RestoresPresetAndKeepsKey()
    {
        var deepseek = AiProviderStore.GetProvider("deepseek")!;
        deepseek.ApiKey = "sk-keep-me";
        deepseek.AddModel("user-added-model");
        deepseek.DefaultModel = "user-added-model";
        AiProviderStore.Save();

        AiProviderStore.ResetProviderDefaults("deepseek");

        deepseek = AiProviderStore.GetProvider("deepseek")!;
        Assert.Equal("sk-keep-me", deepseek.ApiKey);
        Assert.Equal("deepseek-v4-pro", deepseek.DefaultModel);
        Assert.DoesNotContain(deepseek.Models, m => m.Id == "user-added-model");
        Assert.Equal(2, deepseek.Models.Count);
    }

    [Fact]
    public void SelectedModel_FallsBackToProviderDefault()
    {
        AiProviderStore.SetSelected("mimo", "not-exist-model");
        Assert.Equal("mimo-v2.5-pro", AiProviderStore.SelectedModelId);
    }
}
