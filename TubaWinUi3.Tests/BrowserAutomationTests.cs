using System.Reflection;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class BrowserAutomationTests
{
    [Theory]
    [InlineData("\"搜索 - Microsoft 必应\"", "搜索 - Microsoft 必应")]      // 字符串解包
    [InlineData("42", "42")]                                                // 数字原样
    [InlineData("{\"a\":1}", "{\"a\":1}")]                                  // 对象原样
    [InlineData("null", "null")]                                            // null 原样
    [InlineData("\"\"", "")]                                                // 空字符串解包
    [InlineData("plain", "plain")]                                          // 非 JSON 原样
    public void UnwrapStringResult_HandlesAllResultShapes(string input, string expected)
    {
        var method = typeof(BrowserAutomationService).GetMethod("UnwrapStringResult",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = (string)method.Invoke(null, [input])!;
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("div", "", "button", "按钮")]          // ARIA role 优先
    [InlineData("div", "", "checkbox", "复选框")]
    [InlineData("div", "", "switch", "开关")]
    [InlineData("div", "", "searchbox", "搜索框")]
    [InlineData("a", "", "", "链接")]                   // 标签兜底
    [InlineData("button", "submit", "", "提交按钮")]
    [InlineData("input", "search", "", "搜索框")]
    [InlineData("input", "checkbox", "", "复选框")]
    [InlineData("input", "text", "", "输入框")]
    [InlineData("textarea", "", "", "文本框")]
    [InlineData("summary", "", "", "折叠项")]
    [InlineData("img", "", "", "图片")]
    [InlineData("span", "", "", "span")]               // 未知标签原样
    public void DescribeKind_MapsTagsAndRoles(string tag, string type, string role, string expected)
    {
        var method = typeof(BrowserAutomationService).GetMethod("DescribeKind",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = (string)method.Invoke(null, [tag, type, role])!;
        Assert.Equal(expected, actual);
    }
}
