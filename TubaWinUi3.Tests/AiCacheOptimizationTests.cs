using System.Text.Json;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 缓存优化单元测试：
/// 1) usage 缓存命中 token 解析（DeepSeek/GLM / Moonshot-Kimi / OpenAI 标准三种风格）；
/// 2) 系统提示词去易变化（BuildSystemInfoContext 不再含分钟级变化的当前时间，跨调用字节稳定）。
/// </summary>
public class AiCacheOptimizationTests
{
    private static JsonElement Usage(string json)
        => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseCacheTokens_DeepSeekStyle_ParsesHitAndMiss()
    {
        // DeepSeek / GLM 返回 prompt_cache_hit_tokens + prompt_cache_miss_tokens
        var usage = Usage("""{"prompt_tokens":1200,"completion_tokens":80,"total_tokens":1280,"prompt_cache_hit_tokens":1000,"prompt_cache_miss_tokens":200}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(1000, hit);
        Assert.Equal(200, miss);
    }

    [Fact]
    public void ParseCacheTokens_CachedTokensStyle_ParsesHit()
    {
        // Moonshot/Kimi 返回 usage.cached_tokens
        var usage = Usage("""{"prompt_tokens":900,"completion_tokens":60,"total_tokens":960,"cached_tokens":700}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(700, hit);
        Assert.Null(miss);
    }

    [Fact]
    public void ParseCacheTokens_OpenAiPromptTokensDetails_ParsesHit()
    {
        // OpenAI 标准：usage.prompt_tokens_details.cached_tokens
        var usage = Usage("""{"prompt_tokens":500,"completion_tokens":40,"total_tokens":540,"prompt_tokens_details":{"cached_tokens":300}}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(300, hit);
        Assert.Null(miss);
    }

    [Fact]
    public void ParseCacheTokens_NoCacheFields_ReturnsNulls()
    {
        var usage = Usage("""{"prompt_tokens":100,"completion_tokens":10,"total_tokens":110}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Null(hit);
        Assert.Null(miss);
    }

    [Fact]
    public void ParseCacheTokens_PreferDeepSeekOverCachedTokens()
    {
        // 同时出现两种字段时以 DeepSeek 风格为准（prompt_cache_hit_tokens 优先）
        var usage = Usage("""{"prompt_tokens":1200,"completion_tokens":80,"prompt_cache_hit_tokens":1000,"prompt_cache_miss_tokens":200,"cached_tokens":999}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(1000, hit);
        Assert.Equal(200, miss);
    }

    [Fact]
    public void BuildSystemInfoContext_NoCurrentTime()
    {
        var context = AiAssistantService.BuildSystemInfoContext();

        // 当前时间已被移出系统提示词（它是前缀缓存失效的根因）
        Assert.DoesNotContain("当前时间", context);
        Assert.DoesNotContain("yyyy", context);
    }

    [Fact]
    public void BuildSystemInfoContext_StableAcrossCalls()
    {
        // 系统提示词字节级稳定是前缀缓存命中的前提
        var first = AiAssistantService.BuildSystemInfoContext();
        var second = AiAssistantService.BuildSystemInfoContext();

        Assert.Equal(first, second);
    }

    [Fact]
    public void WithCurrentTime_AppendsTimeAtEnd()
    {
        var text = AiAssistantService.WithCurrentTime("帮我查一下内存价格");

        Assert.StartsWith("帮我查一下内存价格", text);
        // 时间以追加段落附在消息末尾（不参与前缀缓存匹配），与正文分隔
        Assert.Contains("\n\n（当前时间：", text);
        Assert.EndsWith("）", text);
    }
}
