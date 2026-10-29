using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using TubaWinUi3.Services.EnvVars;

namespace TubaWinUi3.Tests;

/// <summary>
/// 环境变量的纯规则：值类型判定、名称校验、PATH 拆合与去重归一化、改动 diff、备份序列化。
/// 这一层不碰注册表，是「写坏系统」的第一道闸门，所以逐条钉死。
/// </summary>
public class EnvironmentVariableTests
{
    // ---------- 值类型判定：含 % 才是 REG_EXPAND_SZ ----------

    [Theory]
    [InlineData(@"%SystemRoot%\x", true)]
    [InlineData(@"C:\x", false)]
    [InlineData("", false)]
    [InlineData("%", true)]                 // 单个 % 也按 ExpandString——刻意的启发式
    [InlineData("100%", true)]              // 值中间的 % 同样命中
    [InlineData("%%", true)]
    [InlineData("%PATH%", true)]
    [InlineData(@"C:\Program Files\%LocalAppData%", true)]
    public void ShouldStoreAsExpandString_MatchesPresenceOfPercent(string value, bool expected)
    {
        Assert.Equal(expected, EnvVarRules.ShouldStoreAsExpandString(value));
    }

    [Fact]
    public void ShouldStoreAsExpandString_NullIsNotExpand()
    {
        Assert.False(EnvVarRules.ShouldStoreAsExpandString(null));
    }

    // ---------- 名称校验 ----------

    [Fact]
    public void ValidateName_AcceptsOrdinaryName()
    {
        Assert.Null(EnvVarRules.ValidateName("MY_VAR"));
        Assert.Null(EnvVarRules.ValidateName("Path"));
    }

    [Fact]
    public void ValidateName_RejectsEmpty()
    {
        Assert.Equal(EnvFailureCode.NameEmpty, EnvVarRules.ValidateName(""));
    }

    [Theory]
    [InlineData(" MY_VAR")]      // 首部空白
    [InlineData("MY_VAR ")]      // 尾部空白
    public void ValidateName_RejectsSurroundingWhitespace(string name)
    {
        Assert.Equal(EnvFailureCode.NameWhitespace, EnvVarRules.ValidateName(name));
    }

    [Fact]
    public void ValidateName_RejectsEqualsSign()
    {
        Assert.Equal(EnvFailureCode.NameEqualsSign, EnvVarRules.ValidateName("A=B"));
    }

    [Fact]
    public void ValidateName_RejectsControlCharacters()
    {
        Assert.Equal(EnvFailureCode.NameControlChar, EnvVarRules.ValidateName("A\u0001B"));
        Assert.Equal(EnvFailureCode.NameControlChar, EnvVarRules.ValidateName("A\tB"));
    }

    [Fact]
    public void ValidateName_LengthBoundaryIs259()
    {
        Assert.Null(EnvVarRules.ValidateName(new string('A', 259)));
        Assert.Equal(EnvFailureCode.NameTooLong, EnvVarRules.ValidateName(new string('A', 260)));
    }

    // ---------- 值校验 ----------

    [Fact]
    public void ValidateValue_RejectsNullCharacter()
    {
        Assert.Equal(EnvFailureCode.ValueNullChar, EnvVarRules.ValidateValue("A\0B"));
    }

    [Fact]
    public void ValidateValue_AcceptsOrdinaryValue()
    {
        Assert.Null(EnvVarRules.ValidateValue(@"C:\x;D:\y"));
        Assert.Null(EnvVarRules.ValidateValue(""));
    }

    // ---------- 总长度校验：name + '=' + value <= 32766 ----------

    [Fact]
    public void ValidateTotalLength_BoundaryIs32766()
    {
        const string name = "A";                       // 1
        var valueAtLimit = new string('x', 32766 - 1 - 1);   // 1 + 1 + 32764 = 32766
        var valueOverLimit = new string('x', 32766 - 1 - 1 + 1);

        Assert.Null(EnvVarRules.ValidateTotalLength(name, valueAtLimit));
        Assert.Equal(EnvFailureCode.TotalTooLong, EnvVarRules.ValidateTotalLength(name, valueOverLimit));
    }

    // ---------- 列表型变量判定 ----------

    [Theory]
    [InlineData("PATH", true)]
    [InlineData("path", true)]              // 大小写不敏感
    [InlineData("Path", true)]
    [InlineData("PATHEXT", true)]
    [InlineData("PSMODULEPATH", true)]
    [InlineData("_NT_SYMBOL_PATH", true)]
    [InlineData("_NT_ALT_SYMBOL_PATH", true)]
    [InlineData("_NT_SYMCACHE_PATH", true)]
    [InlineData("TEMP", false)]
    [InlineData("MYPATH", false)]
    public void IsListVariable_RecognisesPathLikeVariables(string name, bool expected)
    {
        Assert.Equal(expected, EnvVarRules.IsListVariable(name));
    }

    // ---------- PATH 拆合：空项必须保留 ----------

    [Fact]
    public void SplitList_KeepsEmptyEntries()
    {
        Assert.Equal(4, EnvVarRules.SplitList("a;b;;c").Count);
    }

    [Theory]
    [InlineData("a;b;;c")]
    [InlineData(";a;")]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(";;")]
    public void SplitThenJoin_RoundTripsExactly(string value)
    {
        Assert.Equal(value, EnvVarRules.JoinList(EnvVarRules.SplitList(value)));
    }

    // ---------- 去重归一化 ----------

    [Fact]
    public void DedupeList_TreatsTrailingSeparatorAndCaseAsSame()
    {
        var result = EnvVarRules.DedupeList([@"C:\A\", @"c:\a"]);
        Assert.Single(result);
        Assert.Equal(@"C:\A\", result[0]);      // 保留首次出现的那条原文
    }

    [Fact]
    public void DedupeList_TreatsUnresolvedVariableAndItsExpansionAsSame()
    {
        var expanded = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\foo");
        var result = EnvVarRules.DedupeList([@"%SystemRoot%\foo", expanded]);

        Assert.Single(result);
        Assert.Equal(@"%SystemRoot%\foo", result[0]);
    }

    [Fact]
    public void DedupeList_KeepsFirstOccurrenceOrder()
    {
        var result = EnvVarRules.DedupeList([@"C:\a", @"C:\b", @"c:\A", @"C:\c"]);
        Assert.Equal([@"C:\a", @"C:\b", @"C:\c"], result);
    }

    [Fact]
    public void DedupeList_KeepsEmptyEntries()
    {
        var result = EnvVarRules.DedupeList(["", @"C:\a", ""]);
        Assert.Equal(3, result.Count);
    }

    // ---------- 改动 diff：只有真正变了的项才写 ----------

    [Fact]
    public void BuildChanges_UnchangedEntriesProduceNoChange()
    {
        var original = new List<EnvVarEntry> { new() { Name = "A", Value = "1" } };
        var working = new List<EnvVarEntry> { new() { Name = "A", Value = "1" } };

        Assert.Empty(EnvVarRules.BuildChanges(original, working));
    }

    [Fact]
    public void BuildChanges_DetectsAddedChangedAndRemoved()
    {
        var original = new List<EnvVarEntry>
        {
            new() { Name = "KEEP", Value = "1" },
            new() { Name = "EDIT", Value = "old" },
            new() { Name = "DROP", Value = "x" },
        };
        var working = new List<EnvVarEntry>
        {
            new() { Name = "KEEP", Value = "1" },
            new() { Name = "EDIT", Value = "new" },
            new() { Name = "NEW", Value = "y" },
        };

        var changes = EnvVarRules.BuildChanges(original, working);

        Assert.Equal(3, changes.Count);
        Assert.Contains(new EnvVarChange("EDIT", EnvChangeKind.Set, "new"), changes);
        Assert.Contains(new EnvVarChange("NEW", EnvChangeKind.Set, "y"), changes);
        Assert.Contains(new EnvVarChange("DROP", EnvChangeKind.Delete, null), changes);
        Assert.DoesNotContain(changes, c => c.Name == "KEEP");   // 没动过的绝不入列
    }

    [Fact]
    public void BuildChanges_RenameBecomesDeletePlusSet()
    {
        var original = new List<EnvVarEntry> { new() { Name = "OLD", Value = "v" } };
        var working = new List<EnvVarEntry> { new() { Name = "RENAMED", Value = "v" } };

        var changes = EnvVarRules.BuildChanges(original, working);

        Assert.Equal(2, changes.Count);
        Assert.Contains(new EnvVarChange("RENAMED", EnvChangeKind.Set, "v"), changes);
        Assert.Contains(new EnvVarChange("OLD", EnvChangeKind.Delete, null), changes);
    }

    [Fact]
    public void BuildChanges_MatchesNamesCaseInsensitively()
    {
        // 注册表里是 Path，工作区写成 PATH——同名改值，不该被当成「删一个建一个」
        var original = new List<EnvVarEntry> { new() { Name = "Path", Value = "1" } };
        var working = new List<EnvVarEntry> { new() { Name = "PATH", Value = "2" } };

        var changes = EnvVarRules.BuildChanges(original, working);

        Assert.Single(changes);
        Assert.Equal(EnvChangeKind.Set, changes[0].Kind);
        Assert.Equal("2", changes[0].Value);
    }

    // ---------- 备份序列化往返 ----------

    [Fact]
    public void BackupFile_RoundTripsThroughJson()
    {
        var backup = new EnvVarBackupFile
        {
            Scope = "User",
            TimeUtc = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc),
            Entries =
            [
                new EnvVarBackupEntry { Name = "HAD", Existed = true, Value = "%SystemRoot%\\x", Kind = "ExpandString" },
                new EnvVarBackupEntry { Name = "MISSING", Existed = false, Value = null, Kind = "" },
            ],
        };

        var json = JsonSerializer.Serialize(backup);
        var restored = JsonSerializer.Deserialize<EnvVarBackupFile>(json);

        Assert.NotNull(restored);
        Assert.Equal("User", restored!.Scope);
        Assert.Equal(backup.TimeUtc, restored.TimeUtc);
        Assert.Equal(2, restored.Entries.Count);
        Assert.True(restored.Entries[0].Existed);
        Assert.Equal("%SystemRoot%\\x", restored.Entries[0].Value);
        Assert.Equal("ExpandString", restored.Entries[0].Kind);
        Assert.False(restored.Entries[1].Existed);
        Assert.Null(restored.Entries[1].Value);
    }

    // ---------- 恢复的写入类型：显式 Kind 优先于 % 启发式 ----------

    [Theory]
    [InlineData("ExpandString", @"C:\Windows", "ExpandString")]     // 无 % 的 REG_EXPAND_SZ 必须原样还原
    [InlineData("String", "100%", "String")]                        // 有 % 的 REG_SZ 不许被改成 ExpandString
    [InlineData("ExpandString", @"%SystemRoot%\x", "ExpandString")]
    [InlineData("ExpandString", "", "ExpandString")]
    [InlineData(null, @"C:\Windows", "String")]                     // 未指定（普通编辑）：仍按 % 启发式
    [InlineData(null, @"%SystemRoot%\x", "ExpandString")]
    [InlineData("", @"C:\Windows", "String")]                       // 空串视为未指定
    [InlineData("expandstring", @"C:\Windows", "ExpandString")]     // 容错大小写：手工改过的备份也能认
    [InlineData("DWord", "%X%", "ExpandString")]                    // 罕见历史类型退回启发式
    public void ResolveValueKindName_ExplicitKindWinsOverPercentHeuristic(string? explicitKind, string value, string expected)
    {
        Assert.Equal(expected, EnvVarRules.ResolveValueKindName(explicitKind, value));
    }

    [Fact]
    public void BuildRestoreChanges_CarriesOriginalValueKind()
    {
        var entries = new List<EnvVarBackupEntry>
        {
            new() { Name = "KEEP", Existed = true, Value = @"C:\Windows", Kind = "ExpandString" },
            new() { Name = "NEW", Existed = false, Value = null, Kind = "" },
        };

        var changes = EnvVarRules.BuildRestoreChanges(entries);

        Assert.Equal(2, changes.Count);
        var set = changes.Single(change => change.Name == "KEEP");
        Assert.Equal(EnvChangeKind.Set, set.Kind);
        Assert.Equal(@"C:\Windows", set.Value);
        Assert.Equal("ExpandString", set.ValueKind);      // 关键：Kind 不许在半路丢掉
        var delete = changes.Single(change => change.Name == "NEW");
        Assert.Equal(EnvChangeKind.Delete, delete.Kind);
        Assert.Null(delete.Value);
    }

    [Fact]
    public void BuildRestoreChanges_NullValueBecomesEmptyString()
    {
        var changes = EnvVarRules.BuildRestoreChanges(
            [new EnvVarBackupEntry { Name = "HAD", Existed = true, Value = null, Kind = "String" }]);

        var change = Assert.Single(changes);
        Assert.Equal("", change.Value);
        Assert.Equal("String", change.ValueKind);
    }

    // ---------- Apply / Restore 端到端（真实 HKCU\Environment） ----------
    //
    // 这几个用例会真的写用户环境变量注册表：变量名一律用 TUBA_ENVVAR_TEST_* 前缀、测试前后都清理，
    // 数据目录（备份落盘处）用临时目录走 DataDirOverride，与 TimeSync / GameTunnel 的测试同一套做法。
    // 注意：TUBA_ENVVAR_TEST_ 前缀为测试保留——用例只碰这几个名字，看到它们被删是正常的。

    private const string ApplyOkVar = "TUBA_ENVVAR_TEST_APPLY_OK";
    private const string NoWriteVarA = "TUBA_ENVVAR_TEST_NOWRITE_A";
    private const string NoWriteVarB = "TUBA_ENVVAR_TEST_NOWRITE_B";
    private const string KindExpandVar = "TUBA_ENVVAR_TEST_KIND_EXPAND";
    private const string KindStringVar = "TUBA_ENVVAR_TEST_KIND_STRING";
    private const string UnsupportedVar = "TUBA_ENVVAR_TEST_MULTISZ";

    [Fact]
    public void Apply_WithWorkingBackupDir_SucceedsWritesAndSnapshotsPreState()
    {
        var dir = NewTempDataDir();
        var previous = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(ApplyOkVar);

        try
        {
            var result = EnvironmentVariableService.Apply(
                EnvScope.User,
                [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "needle")]);

            Assert.True(result.Succeeded, Why(result));
            Assert.NotNull(result.BackupPath);
            Assert.True(File.Exists(result.BackupPath));
            Assert.Empty(Directory.GetFiles(Path.Combine(dir, "backups"), "*.tmp"));   // 原子写不留半成品

            var entry = EnvironmentVariableService.ReadOne(EnvScope.User, ApplyOkVar);
            Assert.NotNull(entry);
            Assert.Equal("needle", entry!.Value);

            // 快照必须反映「写入前」的状态：这个变量是本次新加的，所以 Existed=false
            var backup = JsonSerializer.Deserialize<EnvVarBackupFile>(File.ReadAllText(result.BackupPath));
            Assert.NotNull(backup);
            var snapshot = backup!.Entries.Single(item => item.Name == ApplyOkVar);
            Assert.False(snapshot.Existed);
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previous;
            DeleteUserEnvValue(ApplyOkVar);
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Apply_WithWorkingBackupDir_PrunesStaleBackupTempFiles()
    {
        var dir = NewTempDataDir();
        var previous = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(ApplyOkVar);

        var backupsDir = Path.Combine(dir, "backups");
        Directory.CreateDirectory(backupsDir);
        var stale = Path.Combine(backupsDir, "user-20200101-000000-000.json.tmp");
        File.WriteAllText(stale, "half-written");

        try
        {
            var result = EnvironmentVariableService.Apply(
                EnvScope.User,
                [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "v")]);

            Assert.True(result.Succeeded, Why(result));
            Assert.False(File.Exists(stale));   // 中断留下的半成品不该永远堆在备份目录里
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previous;
            DeleteUserEnvValue(ApplyOkVar);
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Apply_WhenBackupCannotBeCreated_ReturnsFailureForBackupReason()
    {
        // 用一个「文件」占住路径：它下面建不出 backups 目录，备份必然失败
        var blocker = NewTempDataDir();
        File.WriteAllText(blocker, "x");
        var previous = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = blocker;
        DeleteUserEnvValue(NoWriteVarA);

        try
        {
            var result = EnvironmentVariableService.Apply(
                EnvScope.User,
                [new EnvVarChange(NoWriteVarA, EnvChangeKind.Set, "must-not-write")]);

            Assert.False(result.Succeeded);
            Assert.Equal(EnvFailureCode.BackupFailed, result.FailureCode);
            Assert.NotNull(result.Detail);            // 原始原因保留给日志，便于诊断
            Assert.False(result.NeedsElevation);      // 备份失败不是权限问题，别误导用户去提权
            Assert.Null(result.BackupPath);
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previous;
            DeleteUserEnvValue(NoWriteVarA);
            try { File.Delete(blocker); } catch { }
        }
    }

    [Fact]
    public void Apply_WhenBackupCannotBeCreated_LeavesRegistryUntouched()
    {
        var blocker = NewTempDataDir();
        File.WriteAllText(blocker, "x");
        var previous = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = blocker;
        DeleteUserEnvValue(NoWriteVarA);
        DeleteUserEnvValue(NoWriteVarB);

        try
        {
            var before = ReadUserSnapshot();

            var result = EnvironmentVariableService.Apply(EnvScope.User,
            [
                new EnvVarChange(NoWriteVarA, EnvChangeKind.Set, "a"),
                new EnvVarChange(NoWriteVarB, EnvChangeKind.Set, "b"),
            ]);

            Assert.False(result.Succeeded);
            Assert.Equal(before, ReadUserSnapshot());   // 整张表逐项比对：一个值都没动
            Assert.Null(EnvironmentVariableService.ReadOne(EnvScope.User, NoWriteVarA));
            Assert.Null(EnvironmentVariableService.ReadOne(EnvScope.User, NoWriteVarB));
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previous;
            DeleteUserEnvValue(NoWriteVarA);
            DeleteUserEnvValue(NoWriteVarB);
            try { File.Delete(blocker); } catch { }
        }
    }

    // ---------- 中途写入失败：必须自动回滚，回滚失败必须如实上报 ----------

    [Fact]
    public void Apply_WhenOneChangeFailsMidway_RollsBackToPreApplyState()
    {
        var dir = NewTempDataDir();
        var previousDir = EnvironmentVariableService.DataDirOverride;
        var previousInjector = EnvironmentVariableService.WriteFailureInjector;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(ApplyOkVar);
        DeleteUserEnvValue(NoWriteVarA);

        try
        {
            // 种子：A = v0（正常路径写入）
            var seed = EnvironmentVariableService.Apply(
                EnvScope.User, [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "v0")]);
            Assert.True(seed.Succeeded, Why(seed));

            // 调用序：1) 主写入 A 放行、2) 主写入 B 失败；之后回滚的两笔写入都放行
            var calls = 0;
            EnvironmentVariableService.WriteFailureInjector = _ => ++calls == 2 ? "注入的写入失败" : null;

            var result = EnvironmentVariableService.Apply(EnvScope.User,
            [
                new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "v1"),
                new EnvVarChange(NoWriteVarA, EnvChangeKind.Set, "b"),
            ]);

            Assert.False(result.Succeeded);                              // 不能静默报告成功
            Assert.Equal(EnvFailureCode.WriteFailed, result.FailureCode);
            Assert.Equal(EnvRollbackState.Succeeded, result.Rollback);   // 已自动恢复
            Assert.NotNull(result.BackupPath);                           // 快照仍在（可人工再恢复）
            Assert.True(File.Exists(result.BackupPath));
            Assert.Contains("注入的写入失败", result.Detail);             // 原始原因保留给日志

            Assert.Equal("v0", EnvironmentVariableService.ReadOne(EnvScope.User, ApplyOkVar)!.Value);   // A 已回滚
            Assert.Null(EnvironmentVariableService.ReadOne(EnvScope.User, NoWriteVarA));                // B 从未写入
        }
        finally
        {
            EnvironmentVariableService.WriteFailureInjector = previousInjector;
            EnvironmentVariableService.DataDirOverride = previousDir;
            DeleteUserEnvValue(ApplyOkVar);
            DeleteUserEnvValue(NoWriteVarA);
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Apply_WhenRollbackAlsoFails_ReportsPartialStateAndBackupCanStillRestore()
    {
        var dir = NewTempDataDir();
        var previousDir = EnvironmentVariableService.DataDirOverride;
        var previousInjector = EnvironmentVariableService.WriteFailureInjector;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(ApplyOkVar);
        DeleteUserEnvValue(NoWriteVarA);

        try
        {
            var seed = EnvironmentVariableService.Apply(
                EnvScope.User, [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "v0")]);
            Assert.True(seed.Succeeded, Why(seed));

            // 调用序：1) 主写入 A 放行；2) 主写入 B 失败；3) 回滚写入 A 也失败
            var calls = 0;
            EnvironmentVariableService.WriteFailureInjector = _ => ++calls switch
            {
                1 => null,
                2 => "注入的写入失败",
                _ => "注入的回滚失败",
            };

            var result = EnvironmentVariableService.Apply(EnvScope.User,
            [
                new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "v1"),
                new EnvVarChange(NoWriteVarA, EnvChangeKind.Set, "b"),
            ]);

            Assert.False(result.Succeeded);
            Assert.Equal(EnvFailureCode.WriteFailed, result.FailureCode);
            Assert.Equal(EnvRollbackState.Failed, result.Rollback);      // 回滚失败必须如实上报
            Assert.Contains("注入的回滚失败", result.Detail);

            // 状态确实停在「部分修改」：A 是新值、B 没写进去——界面文案会据此提示「部分修改可能仍然存在」
            Assert.Equal("v1", EnvironmentVariableService.ReadOne(EnvScope.User, ApplyOkVar)!.Value);
            Assert.Null(EnvironmentVariableService.ReadOne(EnvScope.User, NoWriteVarA));

            // 快照仍在，用户立刻就能用「恢复上次备份」人工还原（把注入摘掉再恢复）
            Assert.NotNull(result.BackupPath);
            EnvironmentVariableService.WriteFailureInjector = previousInjector;

            var restore = EnvironmentVariableService.RestoreLatest(EnvScope.User);
            Assert.True(restore.Succeeded, Why(restore));
            Assert.Equal("v0", EnvironmentVariableService.ReadOne(EnvScope.User, ApplyOkVar)!.Value);
        }
        finally
        {
            EnvironmentVariableService.WriteFailureInjector = previousInjector;
            EnvironmentVariableService.DataDirOverride = previousDir;
            DeleteUserEnvValue(ApplyOkVar);
            DeleteUserEnvValue(NoWriteVarA);
            TryDeleteDirectory(dir);
        }
    }

    // ---------- 不支持的注册表类型：只读，绝不静默转换成 REG_SZ ----------

    [Fact]
    public void BuildRestoreChanges_SkipsUnsupportedRegistryKinds()
    {
        var entries = new List<EnvVarBackupEntry>
        {
            new() { Name = "OK", Existed = true, Value = @"C:\Windows", Kind = "ExpandString" },
            new() { Name = "WEIRD", Existed = true, Value = "System.String[]", Kind = "MultiString" },
            new() { Name = "GONE", Existed = false, Value = null, Kind = "" },
        };

        var changes = EnvVarRules.BuildRestoreChanges(entries);

        Assert.Equal(2, changes.Count);
        Assert.DoesNotContain(changes, change => change.Name == "WEIRD");   // 快照里只有字符串化文本，写回去就是类型转换
        Assert.Contains(changes, change => change.Name == "OK");
        Assert.Contains(changes, change => change.Name == "GONE" && change.Kind == EnvChangeKind.Delete);
    }

    [Fact]
    public void Apply_OnUnsupportedRegistryKind_RefusesWithoutWriting()
    {
        var dir = NewTempDataDir();
        var previousDir = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(UnsupportedVar);

        try
        {
            // 直接造一个 REG_MULTI_SZ 的「环境变量」（第三方软件可能留下的非常规类型）
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(EnvironmentVariableService.RegistryPathFor(EnvScope.User), writable: true))
            {
                key!.SetValue(UnsupportedVar, new[] { "a", "b" }, RegistryValueKind.MultiString);
            }

            // 读取时被标记为「不支持」：界面据此禁用编辑与删除
            var (entries, failureCode, _, _) = EnvironmentVariableService.ReadAll(EnvScope.User);
            Assert.Equal(EnvFailureCode.None, failureCode);
            Assert.True(entries.Single(entry => entry.Name == UnsupportedVar).KindUnsupported);

            // 改写被硬门禁拦住：整批不写、连快照都不建
            var set = EnvironmentVariableService.Apply(
                EnvScope.User, [new EnvVarChange(UnsupportedVar, EnvChangeKind.Set, "x")]);
            Assert.Equal(EnvFailureCode.UnsupportedKind, set.FailureCode);
            Assert.Null(set.BackupPath);

            // 删除同样被拦：本工具对这类变量只读
            var delete = EnvironmentVariableService.Apply(
                EnvScope.User, [new EnvVarChange(UnsupportedVar, EnvChangeKind.Delete, null)]);
            Assert.Equal(EnvFailureCode.UnsupportedKind, delete.FailureCode);

            // 注册表里原样还在，类型没有被改成 REG_SZ
            var after = EnvironmentVariableService.ReadOne(EnvScope.User, UnsupportedVar);
            Assert.NotNull(after);
            Assert.Equal("MultiString", after!.Kind);
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previousDir;
            DeleteUserEnvValue(UnsupportedVar);
            TryDeleteDirectory(dir);
        }
    }

    // ---------- 广播：失败只记诊断，既不算保存失败也不回滚 ----------

    [Fact]
    public void NotifyEnvironmentChange_ReportsFailureAsDiagnosticOnly()
    {
        var previous = EnvironmentVariableService.BroadcastResultOverride;

        try
        {
            EnvironmentVariableService.BroadcastResultOverride = true;
            EnvironmentVariableService.NotifyEnvironmentChange();
            Assert.Null(EnvironmentVariableService.LastBroadcastDiagnostic);

            // SendMessageTimeoutW 失败不抛异常（返回 0），必须显式检查返回值并留下诊断
            EnvironmentVariableService.BroadcastResultOverride = false;
            EnvironmentVariableService.NotifyEnvironmentChange();
            Assert.NotNull(EnvironmentVariableService.LastBroadcastDiagnostic);
        }
        finally
        {
            EnvironmentVariableService.BroadcastResultOverride = previous;
            EnvironmentVariableService.NotifyEnvironmentChange();   // 复位诊断状态（走真实广播）
        }
    }

    [Fact]
    public void InterpretBroadcastResult_NonZeroMeansDelivered_ZeroBecomesDiagnostic()
    {
        // 非 0 = 至少一个窗口收到：没有诊断
        Assert.Null(EnvironmentVariableService.InterpretBroadcastResult(new IntPtr(1), 0));

        // 0 = 失败/超时：转成带 Win32 错误码的诊断（这条判定不能依赖 C# 异常，SendMessageTimeoutW 不抛）
        var diagnostic = EnvironmentVariableService.InterpretBroadcastResult(IntPtr.Zero, 1460 /* ERROR_TIMEOUT */);
        Assert.NotNull(diagnostic);
        Assert.Contains("1460", diagnostic);
    }

    [Fact]
    public void Apply_SucceedsEvenWhenBroadcastFails()
    {
        var dir = NewTempDataDir();
        var previousDir = EnvironmentVariableService.DataDirOverride;
        var previousBroadcast = EnvironmentVariableService.BroadcastResultOverride;
        EnvironmentVariableService.DataDirOverride = dir;
        EnvironmentVariableService.BroadcastResultOverride = false;   // 广播一定「失败」
        DeleteUserEnvValue(ApplyOkVar);

        try
        {
            var result = EnvironmentVariableService.Apply(
                EnvScope.User, [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, "v")]);

            Assert.True(result.Succeeded, Why(result));   // 注册表写成功 = 保存成功，广播失败不算失败
            Assert.Equal("v", EnvironmentVariableService.ReadOne(EnvScope.User, ApplyOkVar)!.Value);
            Assert.NotNull(EnvironmentVariableService.LastBroadcastDiagnostic);   // 但留下了诊断
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previousDir;
            EnvironmentVariableService.BroadcastResultOverride = previousBroadcast;
            EnvironmentVariableService.NotifyEnvironmentChange();   // 复位诊断状态
            DeleteUserEnvValue(ApplyOkVar);
            TryDeleteDirectory(dir);
        }
    }

    [Theory]
    [InlineData(KindExpandVar, "ExpandString", @"C:\Windows")]     // 无 %：旧行为会按启发式降级成 REG_SZ
    [InlineData(KindStringVar, "String", "100%")]                  // 有 %：旧行为会升级成 REG_EXPAND_SZ
    public void RestoreLatest_PreservesOriginalValueKind(string name, string kind, string value)
    {
        var dir = NewTempDataDir();
        var previous = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(name);

        try
        {
            // 手工造一份「上次保存前」的快照：一个无 % 的 ExpandString + 一个有 % 的 String
            var backupsDir = Path.Combine(dir, "backups");
            Directory.CreateDirectory(backupsDir);
            var backup = new EnvVarBackupFile
            {
                Scope = "User",
                TimeUtc = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc),
                Entries =
                [
                    new EnvVarBackupEntry { Name = KindExpandVar, Existed = true, Value = @"C:\Windows", Kind = "ExpandString" },
                    new EnvVarBackupEntry { Name = KindStringVar, Existed = true, Value = "100%", Kind = "String" },
                ],
            };
            File.WriteAllText(
                Path.Combine(backupsDir, "user-20261004-120000-000.json"),
                JsonSerializer.Serialize(backup));

            var result = EnvironmentVariableService.RestoreLatest(EnvScope.User);

            Assert.True(result.Succeeded, Why(result));
            var restored = EnvironmentVariableService.ReadOne(EnvScope.User, name);
            Assert.NotNull(restored);
            Assert.Equal(value, restored!.Value);
            Assert.Equal(kind, restored.Kind);   // 恢复必须原样保留写入前的值类型
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previous;
            DeleteUserEnvValue(KindExpandVar);
            DeleteUserEnvValue(KindStringVar);
            TryDeleteDirectory(dir);
        }
    }

    /// <summary>
    /// 恢复用户真正会走的那条路：快照由程序自己从注册表「抓」下来（上面的用例是手工造 JSON）。
    /// 抓取环节若丢 Kind，这条用例必须失败——只造快照的用例覆盖不到这一步。
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows", "ExpandString")]     // REG_EXPAND_SZ + 不含 %
    [InlineData("100%", "String")]                  // REG_SZ + 含 %
    [InlineData(@"%SystemRoot%\x", "ExpandString")] // REG_EXPAND_SZ + 含 %
    [InlineData("plain", "String")]                 // REG_SZ + 不含 %
    public void RestoreLatest_RoundTripsSnapshotCapturedFromRegistry(string value, string kind)
    {
        var dir = NewTempDataDir();
        var previous = EnvironmentVariableService.DataDirOverride;
        EnvironmentVariableService.DataDirOverride = dir;
        DeleteUserEnvValue(ApplyOkVar);

        try
        {
            // 1) 种子：把变量按目标类型真实写进注册表
            var seed = EnvironmentVariableService.Apply(
                EnvScope.User,
                [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, value, kind)]);
            Assert.True(seed.Succeeded, Why(seed));

            // 2) 再普通保存一次（不带显式 Kind）：这次写前快照里的 Kind 必须是从注册表抓到的
            var save = EnvironmentVariableService.Apply(
                EnvScope.User,
                [new EnvVarChange(ApplyOkVar, EnvChangeKind.Set, value + "-changed")]);
            Assert.True(save.Succeeded, Why(save));

            // 3) 从这份程序自产快照恢复：值和类型都要原样回来
            var restore = EnvironmentVariableService.RestoreLatest(EnvScope.User);
            Assert.True(restore.Succeeded, Why(restore));

            var entry = EnvironmentVariableService.ReadOne(EnvScope.User, ApplyOkVar);
            Assert.NotNull(entry);
            Assert.Equal(value, entry!.Value);
            Assert.Equal(kind, entry.Kind);
        }
        finally
        {
            EnvironmentVariableService.DataDirOverride = previous;
            DeleteUserEnvValue(ApplyOkVar);
            TryDeleteDirectory(dir);
        }
    }

    private static string NewTempDataDir()
        => Path.Combine(Path.GetTempPath(), "tubawinui3-envvar-" + Guid.NewGuid().ToString("N"));

    private static void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    /// <summary>直接删注册表里的测试变量（不走服务，服务本身出问题时清理也要可靠）。</summary>
    private static void DeleteUserEnvValue(string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(EnvironmentVariableService.RegistryPathFor(EnvScope.User), writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch
        {
            // 尽力而为：清理失败也不能盖住真正的断言结果
        }
    }

    /// <summary>整张用户环境变量表的快照（名/值/类型），用于断言「一个字节都没动」。</summary>
    private static List<(string Name, string Value, string Kind)> ReadUserSnapshot()
    {
        var (entries, failureCode, _, _) = EnvironmentVariableService.ReadAll(EnvScope.User);
        Assert.Equal(EnvFailureCode.None, failureCode);
        return [.. entries.Select(entry => (entry.Name, entry.Value, entry.Kind))];
    }

    /// <summary>失败时的诊断信息（断言失败消息里带上失败代码与原始原因，便于定位）。</summary>
    private static string Why(EnvApplyResult result)
        => $"{result.FailureCode}（{result.FailureName ?? "-"}）{result.Detail ?? ""}".Trim();

    // ---------- 失败文案本地化：服务层只给代码，界面必须有中英双语资源 ----------

    /// <summary>
    /// 服务层（含校验规则）只产出 <see cref="EnvFailureCode"/>，文案全部来自 resw。
    /// 每个失败代码在每个（作用域 × 回滚结果）组合下给出的键都必须存在、必须中英双语、
    /// 英文值里不能混进中文字符，且模板占位符与参数必须匹配（否则 string.Format 会在 toast 里抛）。
    /// </summary>
    [Fact]
    public void FailureCodes_HaveBilingualResourceKeysWithoutChineseInEnglish()
    {
        string repoRoot = FindRepoRoot();
        var zhKeys = LoadReswValues(Path.Combine(repoRoot, "TubaWinUi3.WinUI3", "Strings", "zh-CN", "Resources.resw"));
        var enKeys = LoadReswValues(Path.Combine(repoRoot, "TubaWinUi3.WinUI3", "Strings", "en-US", "Resources.resw"));

        foreach (var code in Enum.GetValues<EnvFailureCode>())
        {
            if (code == EnvFailureCode.None) continue;

            foreach (var scope in new[] { EnvScope.User, EnvScope.System })
            foreach (var rollback in new[] { EnvRollbackState.NotAttempted, EnvRollbackState.Succeeded, EnvRollbackState.Failed })
            {
                var (key, fallback, args) = EnvFailureText.Describe(code, "SAMPLE_VAR", rollback, scope);

                Assert.True(zhKeys.ContainsKey(key), $"zh-CN 缺少失败文案键 {key}（{code}）");
                Assert.True(enKeys.ContainsKey(key), $"en-US 缺少失败文案键 {key}（{code}）");
                Assert.DoesNotMatch("[\\u4e00-\\u9fff]", enKeys[key]);

                // 模板与参数必须匹配：占位符缺参数时 string.Format 会抛，而 toast 在 async void 里
                AssertFormatOk(fallback, args);
                AssertFormatOk(zhKeys[key], args);
                AssertFormatOk(enKeys[key], args);
            }
        }

        // 界面上那条「不支持的注册表类型」只读提示也要双语（Msg 带 {0} 变量名、{1} 类型名）
        foreach (var (key, argCount) in new[] { ("EnvVars_UnsupportedKindTitle", 0), ("EnvVars_UnsupportedKindMsg", 2) })
        {
            Assert.True(zhKeys.ContainsKey(key), $"zh-CN 缺少 {key}");
            Assert.True(enKeys.ContainsKey(key), $"en-US 缺少 {key}");
            Assert.DoesNotMatch("[\\u4e00-\\u9fff]", enKeys[key]);

            var args = Enumerable.Range(0, argCount).Select(index => (object?)$"a{index}").ToArray();
            AssertFormatOk(zhKeys[key], args);
            AssertFormatOk(enKeys[key], args);
        }
    }

    private static void AssertFormatOk(string template, object?[] args)
        => Assert.Null(Record.Exception(() => string.Format(template, args)));

    /// <summary>
    /// 回滚失败时绝不允许继续用基础文案：必须换成「部分修改可能仍然存在」那一句，
    /// 否则界面会把「部分修改可能已落盘」这个事实吞掉（要求：不能假装整个操作失败就等于没发生任何变化）。
    /// </summary>
    [Fact]
    public void FailureText_RollbackFailedUsesPartialStateWording()
    {
        foreach (var scope in new[] { EnvScope.User, EnvScope.System })
        {
            // 写入 / 回读 / 删除失败：只可能带 Succeeded 或 Failed 两种回滚结果（由 FailRolledBack 决定）
            foreach (var code in new[]
                     {
                         EnvFailureCode.WriteFailed,
                         EnvFailureCode.VerifyFailed,
                         EnvFailureCode.DeleteFailed,
                     })
            {
                var rolledBack = EnvFailureText.Describe(code, "X", EnvRollbackState.Succeeded, scope);
                var partial = EnvFailureText.Describe(code, "X", EnvRollbackState.Failed, scope);

                Assert.NotEqual(rolledBack.Key, partial.Key);
                Assert.Contains("部分修改", partial.Fallback);
                Assert.DoesNotContain("部分修改", rolledBack.Fallback);
            }

            // 权限失败：基础文案用于「打开注册表项就被拒、什么都没写」；中途权限丢失且回滚失败要另给一句
            var accessDenied = EnvFailureText.Describe(EnvFailureCode.AccessDenied, "X", EnvRollbackState.NotAttempted, scope);
            var accessDeniedPartial = EnvFailureText.Describe(EnvFailureCode.AccessDenied, "X", EnvRollbackState.Failed, scope);

            Assert.NotEqual(accessDenied.Key, accessDeniedPartial.Key);
            Assert.Contains("部分修改", accessDeniedPartial.Fallback);
        }
    }

    private static Dictionary<string, string> LoadReswValues(string path)
    {
        Assert.True(File.Exists(path), $"缺少资源文件: {path}");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? "",
                StringComparer.Ordinal);
    }

    // ---------- 页面本地化：XAML Uid 必须配带属性后缀的 resw 键 ----------

    /// <summary>
    /// WinUI3Localizer 按 "&lt;Uid&gt;.&lt;属性名&gt;" 解析 resw 键（后缀拼成 TextProperty 再找依赖属性）；
    /// 不带点的键解析不出依赖属性，对 TextBlock / PivotItem / TextBox 这类没注册 LocalizationAction
    /// 的控件会被静默丢弃——英文界面会继续显示 XAML 里写死的中文。本页新增或改名键时必须同步带后缀。
    /// </summary>
    [Fact]
    public void PageXamlUids_AllResolveToSuffixedResourceKeys()
    {
        string[] allowedSuffixes = ["Text", "Content", "Header", "PlaceholderText", "ToolTip", "Title", "Subtitle"];

        string repoRoot = FindRepoRoot();
        string pagePath = Path.Combine(repoRoot, "TubaWinUi3.WinUI3", "Pages", "EnvironmentVariablesPage.xaml");
        Assert.True(File.Exists(pagePath), $"缺少页面: {pagePath}");

        var uids = Regex
            .Matches(File.ReadAllText(pagePath), "l:Uids\\.Uid=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(uids);

        foreach (string language in new[] { "zh-CN", "en-US" })
        {
            string reswPath = Path.Combine(repoRoot, "TubaWinUi3.WinUI3", "Strings", language, "Resources.resw");
            var keys = XDocument.Load(reswPath).Root!.Elements("data")
                .Select(e => (string?)e.Attribute("name"))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

            foreach (string uid in uids)
            {
                Assert.True(
                    keys.Any(k => allowedSuffixes.Any(s => k == $"{uid}.{s}")),
                    $"{language}: Uid '{uid}' 缺少带属性后缀的资源键（如 {uid}.Text）——裸键会被 WinUI3Localizer 静默丢弃");
            }
        }
    }

    /// <summary>向上查找 TubaWinUi3.sln 定位仓库根（与 LocalizationTests 同款，测试各自独立）。</summary>
    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TubaWinUi3.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("未找到仓库根目录（TubaWinUi3.sln）");
    }
}
