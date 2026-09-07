using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 助手会话存储（AiAssistantService）单元测试：重命名 / 删除 / 列表。
/// 使用临时目录 + 静态路径覆盖（HistoryDirOverride），不触碰真实用户数据。
/// </summary>
public class AiAssistantConversationStoreTests : IDisposable
{
    private readonly string _dir;

    public AiAssistantConversationStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TubaAiConvTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        AiAssistantService.HistoryDirOverride = _dir;
    }

    public void Dispose()
    {
        AiAssistantService.HistoryDirOverride = null;
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void DeleteConversation_RemovesAllFourArtifacts()
    {
        const string id = "abc123";
        // 构造 4 个关联文件（含 display 与 memory —— 旧实现会遗留的孤儿文件）
        foreach (var suffix in new[] { ".meta.json", ".messages.json", ".display.json", ".memory.md" })
            File.WriteAllText(Path.Combine(_dir, id + suffix), "{}");

        AiAssistantService.DeleteConversation(id);

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void DeleteConversation_MissingFiles_DoesNotThrow()
    {
        AiAssistantService.DeleteConversation("nonexistent-id");
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void RenameConversation_UpdatesTitleAndPreservesCreatedAt()
    {
        const string id = "conv-1";
        var createdAt = new DateTime(2026, 8, 1, 10, 30, 0);
        AiAssistantService.SaveConversation(id, "旧标题", [AiChatMessage.User("你好")]);
        var metaPath = Path.Combine(_dir, $"{id}.meta.json");
        var before = System.Text.Json.JsonSerializer.Deserialize<ConversationMeta>(
            File.ReadAllText(metaPath), new System.Text.Json.JsonSerializerOptions());
        Assert.NotNull(before);
        // 把 CreatedAt 改成过去时间，验证重命名后不被刷新
        before!.CreatedAt = createdAt;
        File.WriteAllText(metaPath, System.Text.Json.JsonSerializer.Serialize(before));

        AiAssistantService.RenameConversation(id, "  新标题  ");

        var after = AiAssistantService.ListConversations().Single();
        Assert.Equal("新标题", after.Title);          // trim 后生效
        Assert.Equal(createdAt, after.CreatedAt);      // CreatedAt 保留
        Assert.Equal(1, after.MessageCount);           // MessageCount 保留
    }

    [Fact]
    public void RenameConversation_MissingMeta_IsNoOp()
    {
        AiAssistantService.RenameConversation("ghost-id", "新名字");
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void SaveAndList_RoundTripsAndSortsByCreatedAtDesc()
    {
        AiAssistantService.SaveConversation("a", "第一条", [AiChatMessage.User("hi")]);
        Thread.Sleep(10); // CreatedAt 精度到秒，间隔写入保证排序稳定
        AiAssistantService.SaveConversation("b", "第二条", []);

        var list = AiAssistantService.ListConversations();

        Assert.Equal(2, list.Count);
        Assert.Equal("b", list[0].Id); // 最新的在前
        Assert.Equal("a", list[1].Id);
        Assert.Equal(1, list[1].MessageCount);
        Assert.Equal("第二条", list[0].Title);
    }
}
