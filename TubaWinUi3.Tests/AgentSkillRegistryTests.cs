using System.Reflection;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

// 技能注册表是独立静态状态，与工具注册表互不影响，单独集合串行
[CollectionDefinition("AgentSkillRegistry")]
public sealed class AgentSkillRegistryCollection
{
}

/// <summary>技能注册表 + 技能提示词生成逻辑测试。</summary>
[Collection("AgentSkillRegistry")]
public class AgentSkillRegistryTests
{
    private static readonly FieldInfo SkillsField =
        typeof(AgentSkillRegistry).GetField("_skills", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void ClearRegistry()
        => ((List<AgentSkill>)SkillsField.GetValue(null)!).Clear();

    private static AgentSkill MakeSkill(string id) => new()
    {
        Id = id,
        DisplayName = $"技能{id}",
        Glyph = "\uE721",
        Description = "测试技能",
        SystemPromptFragment = $"这是 {id} 的指导片段"
    };

    [Fact]
    public void RegisterDefaults_RegistersPcBuildSkill()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        var skill = AgentSkillRegistry.Find(PcBuildSkill.Id);
        Assert.NotNull(skill);
        Assert.Equal("电脑选购", skill!.DisplayName);

        // 电脑选购技能的提示词必须包含浏览器查价序列与登录暂停指引
        Assert.Contains("browser_navigate", skill.SystemPromptFragment);
        Assert.Contains("search.jd.com", skill.SystemPromptFragment);
        Assert.Contains("browser_wait_for_login", skill.SystemPromptFragment);
        Assert.Contains("passport.jd.com", skill.SystemPromptFragment);
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        ClearRegistry();
        var skill = MakeSkill("dup_skill");
        AgentSkillRegistry.Register(skill);
        Assert.Throws<InvalidOperationException>(() => AgentSkillRegistry.Register(skill));
    }

    [Fact]
    public void Register_BlankId_Throws()
    {
        ClearRegistry();
        var skill = MakeSkill("");
        Assert.Throws<InvalidOperationException>(() => AgentSkillRegistry.Register(skill));
    }

    [Fact]
    public void Find_ReturnsRegisteredSkill()
    {
        ClearRegistry();
        AgentSkillRegistry.Register(MakeSkill("skill_a"));
        var skill = AgentSkillRegistry.Find("skill_a");
        Assert.NotNull(skill);
        Assert.Equal("skill_a", skill!.Id);
        Assert.Null(AgentSkillRegistry.Find("missing"));
    }

    [Fact]
    public void BuildActiveSkillsContext_OnlyContainsActiveSkillIndex()
    {
        ClearRegistry();
        AgentSkillRegistry.Register(MakeSkill("skill_a"));
        AgentSkillRegistry.Register(MakeSkill("skill_b"));

        var active = new[] { AgentSkillRegistry.Find("skill_a")! };
        var context = AgentSkillRegistry.BuildActiveSkillsContext(active);

        // 索引：包含激活技能的名称与简介，不注入完整指导片段（按需加载）
        Assert.Contains("## 已加载技能", context);
        Assert.Contains("技能skill_a", context);
        Assert.Contains("测试技能", context);
        Assert.DoesNotContain("这是 skill_a 的指导片段", context);
        Assert.DoesNotContain("skill_b", context);
    }

    [Fact]
    public void BuildActiveSkillsContext_NoActiveSkills_ReturnsEmpty()
    {
        var context = AgentSkillRegistry.BuildActiveSkillsContext([]);
        Assert.Equal("", context);
    }

    [Fact]
    public void BuildTriggerFor_HitsKeyword_ReturnsForceInstruction()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        var trigger = AgentSkillRegistry.BuildTriggerFor("我要组一台5000块钱的台式机，主要玩网游", ["pc_build"]);

        Assert.Contains("电脑选购", trigger);
        Assert.Contains("技能强制触发", trigger);
        Assert.Contains("browser_navigate", trigger);
        Assert.Contains("browser_run_js", trigger);
        Assert.Contains("禁止", trigger);
    }

    [Fact]
    public void BuildTriggerFor_NoKeyword_ReturnsEmpty()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        Assert.Equal("", AgentSkillRegistry.BuildTriggerFor("电脑卡顿怎么办", ["pc_build"]));
    }

    [Fact]
    public void BuildTriggerFor_SkillDisabled_ReturnsEmpty()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        // 技能未激活：即使命中关键词也不触发
        Assert.Equal("", AgentSkillRegistry.BuildTriggerFor("帮我配一台电脑", []));
    }

    [Fact]
    public void BuildTriggerFor_KeywordMatchIsSubstring()
    {
        ClearRegistry();
        AgentSkillRegistry.RegisterDefaults();

        // "装机" 作为子串命中
        Assert.NotEqual("", AgentSkillRegistry.BuildTriggerFor("新手第一次装机要注意什么", ["pc_build"]));
    }
}
