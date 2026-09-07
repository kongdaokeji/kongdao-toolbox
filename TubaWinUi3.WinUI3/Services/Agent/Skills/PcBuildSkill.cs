namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 「电脑选购」技能：用户咨询配电脑/装机时，自动用浏览器从京东获取实时价格；
/// 遇到登录拦截时调用 browser_wait_for_login 暂停，提示用户在浏览器窗口完成登录后继续。
/// </summary>
public static class PcBuildSkill
{
    public const string Id = "pc_build";

    public static void Register()
        => AgentSkillRegistry.Register(new AgentSkill
        {
            Id = Id,
            DisplayName = "电脑选购",
            Glyph = "\uE8F1",
            Description = "配电脑/装机时自动上京东查实时价格，登录拦截自动暂停等待",
            TriggerKeywords = ["配电脑", "配台电脑", "配一台", "装机", "装电脑", "台式机", "攒机", "买电脑", "电脑配置", "主机配置", "配置单", "升级电脑", "换电脑", "组一台"],
            SystemPromptFragment = """
            用户咨询配电脑、装机、预算装机、升级配置时，必须按以下流程执行：

            **流程**
            1. 先确认需求：预算、用途（游戏/办公/生产力/直播）、是否需要显示器与外设、是否含操作系统。
            2. 给出配置方案：CPU、主板、显卡、内存、SSD、电源、散热、机箱（按需求匹配，不盲目堆料）。
            3. **必须操作浏览器查询京东实时价格后给出配置**。

            **⚠️ 查价方式（本技能的硬性要求）**
            - **必须使用浏览器工具操作京东页面查价**，禁止只用 web_search / fetch_page 代替——搜索返回的文章价格不是当前可购买的真实价格。
            - 查价标准序列（每个关键配件一次，逐个执行）：
              1. browser_navigate 打开 `https://search.jd.com/Search?keyword=商品名`（keyword 必须 URL 编码，如 `R5 7500F` → `R5%207500F`）
              2. browser_get_page 获取页面元素；价格拿不到时用 browser_run_js 提取，常见类名：`.gl-item`（商品项）、`.p-name`（商品名）、`.p-price`（价格）、`.p-icons`（自营标识），示例：
                 `Array.from(document.querySelectorAll('.gl-item')).slice(0,5).map(i=>({name:i.querySelector('.p-name em')?.innerText, price:i.querySelector('.p-price i')?.innerText}))`
            - 优先「京东自营」商品；同一商品最多查 1 次，价格到手立即收尾。
            - 浏览器查价失败（无结果/风控/页面异常）换关键词重试最多 1 次，再失败就说明情况并改用估算价。

            **登录拦截处理（重要）**
            - 页面跳转到 passport.jd.com / login.jd.com 等登录地址，或出现登录弹窗/「请登录」提示时：**立即调用 browser_wait_for_login（site 填"京东"）**。系统会暂停并向用户提示在浏览器窗口完成登录，浏览器窗口保持打开；用户登录确认后会自动继续执行。
            - 用户拒绝登录时不要反复尝试，改用估算价，并在回复中注明「价格仅供参考，未登录无法获取京东实时价」。

            **输出**
            - 给出配置清单（各配件 + 京东参考价 + 链接）、总价、性价比说明与购买建议；先给结论再给明细。
            """
        });
}
