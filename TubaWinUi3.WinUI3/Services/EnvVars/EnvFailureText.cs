namespace TubaWinUi3.Services.EnvVars;

/// <summary>
/// 失败代码 → 界面文案的（资源键, 中文兜底, 格式化参数）。
/// 服务层只产出 <see cref="EnvFailureCode"/>，不产出任何用户可见文本；界面拿这里的键走
/// <see cref="LocalizationService"/> 渲染（zh-CN / en-US 各有一份资源，键集一致由 LocalizationTests 把关）。
/// 「每个失败代码都有双语资源键、且英文值不含中文」由
/// <c>EnvironmentVariableTests.FailureCodes_HaveBilingualResourceKeys</c> 钉死。
/// 变量名、注册表项路径属于用户输入 / 机器数据，原样嵌入。
/// </summary>
internal static class EnvFailureText
{
    internal static (string Key, string Fallback, object?[] Args) Describe(
        EnvFailureCode code, string? name, EnvRollbackState rollback, EnvScope scope)
        => code switch
        {
            // ---- 校验类：FailureName = 变量名 ----
            EnvFailureCode.NameEmpty => ("EnvVars_Fail_NameEmpty", "变量「{0}」：名称不能为空", [name ?? ""]),
            EnvFailureCode.NameWhitespace => ("EnvVars_Fail_NameWhitespace", "变量「{0}」：名称首尾不能有空白", [name ?? ""]),
            EnvFailureCode.NameEqualsSign => ("EnvVars_Fail_NameEqualsSign", "变量「{0}」：名称不能包含等号", [name ?? ""]),
            EnvFailureCode.NameControlChar => ("EnvVars_Fail_NameControlChar", "变量「{0}」：名称不能包含控制字符", [name ?? ""]),
            EnvFailureCode.NameTooLong => ("EnvVars_Fail_NameTooLong", "变量「{0}」：名称长度不能超过 {1} 个字符", [name ?? "", EnvVarRules.MaxNameLength]),
            EnvFailureCode.ValueNullChar => ("EnvVars_Fail_ValueNullChar", "变量「{0}」：值不能包含空字符", [name ?? ""]),
            EnvFailureCode.TotalTooLong => ("EnvVars_Fail_TotalTooLong", "变量「{0}」：名称与值的总长度不能超过 {1} 个字符", [name ?? "", EnvVarRules.MaxTotalLength]),

            // ---- 读取 / 打开 ----
            EnvFailureCode.ReadFailed => ("EnvVars_Fail_ReadFailed", "读取环境变量失败，请稍后重试", []),
            EnvFailureCode.KeyUnavailable => ("EnvVars_Fail_KeyUnavailable", "打不开注册表项：{0}", [name ?? ""]),

            // ---- 写入 ----
            EnvFailureCode.AccessDenied => AccessDenied(scope, rollback),
            EnvFailureCode.BackupFailed => ("EnvVars_Fail_BackupFailed", "备份创建失败，本次修改已取消，未对环境变量做任何改动", []),
            // 下面三个代码只会由 FailRolledBack 产生，rollback 必为 Succeeded / Failed；
            // Failed（含 NotAttempted 这个「不该出现」的兜底）一律用「部分修改可能仍然存在」的措辞
            EnvFailureCode.WriteFailed => rollback == EnvRollbackState.Succeeded
                ? ("EnvVars_Fail_WriteFailed_RolledBack", "写入失败，已自动恢复到修改前的状态", [])
                : ("EnvVars_Fail_WriteFailed_Partial", "写入失败，自动恢复未成功，部分修改可能仍然存在；可用「恢复上次备份」还原", []),
            EnvFailureCode.VerifyFailed => rollback == EnvRollbackState.Succeeded
                ? ("EnvVars_Fail_VerifyFailed_RolledBack", "写入后回读校验未通过，已自动恢复到修改前的状态", [])
                : ("EnvVars_Fail_VerifyFailed_Partial", "写入后回读校验未通过，自动恢复未成功，部分修改可能仍然存在；可用「恢复上次备份」还原", []),
            EnvFailureCode.DeleteFailed => rollback == EnvRollbackState.Succeeded
                ? ("EnvVars_Fail_DeleteFailed_RolledBack", "变量删除未生效，已自动恢复到修改前的状态", [])
                : ("EnvVars_Fail_DeleteFailed_Partial", "变量删除未生效，自动恢复未成功，部分修改可能仍然存在；可用「恢复上次备份」还原", []),
            EnvFailureCode.UnsupportedKind => ("EnvVars_Fail_UnsupportedKind", "变量「{0}」使用不支持的注册表类型，为避免被改成 REG_SZ，本工具不会修改它", [name ?? ""]),

            // ---- 恢复 ----
            EnvFailureCode.RestoreNoBackup => ("EnvVars_Fail_RestoreNoBackup", "没有可用的备份", []),
            EnvFailureCode.RestoreUnreadable => ("EnvVars_Fail_RestoreUnreadable", "备份文件已损坏或无法读取，请重试", []),
            EnvFailureCode.RestoreEmpty => ("EnvVars_Fail_RestoreEmpty", "备份文件里没有任何变量", []),

            // None 不该走到这里；真到了也只给一句笼统的通用文案
            _ => ("EnvVars_ApplyFailed", "保存失败", []),
        };

    /// <summary>
    /// 权限失败。中途权限丢失（写入部分已落盘）且自动回滚也失败时，同样要如实提示「部分修改可能仍然存在」——
    /// 不能只报「拒绝访问」把这一句吞掉。
    /// </summary>
    private static (string Key, string Fallback, object?[] Args) AccessDenied(EnvScope scope, EnvRollbackState rollback)
    {
        var system = scope == EnvScope.System;

        if (rollback == EnvRollbackState.Failed)
        {
            return system
                ? ("EnvVars_Fail_AccessDeniedSystem_Partial", "拒绝访问：修改系统环境变量需要管理员权限；自动恢复未成功，部分修改可能仍然存在，可用「恢复上次备份」还原", [])
                : ("EnvVars_Fail_AccessDeniedUser_Partial", "拒绝访问：没有修改该注册表项的权限；自动恢复未成功，部分修改可能仍然存在，可用「恢复上次备份」还原", []);
        }

        return system
            ? ("EnvVars_Fail_AccessDeniedSystem", "拒绝访问：修改系统环境变量需要管理员权限", [])
            : ("EnvVars_Fail_AccessDeniedUser", "拒绝访问：没有修改该注册表项的权限", []);
    }
}
