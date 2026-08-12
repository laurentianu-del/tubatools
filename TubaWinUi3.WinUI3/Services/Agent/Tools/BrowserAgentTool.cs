using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 浏览器自动化工具：让 AI 操作真实浏览器（WebView2）。
/// 浏览器插件（browser-use 等）同款思路：DOM 快照 → 索引定位 → 点击/输入/滚动，
/// 完全不需要视觉模型。页面快照仅返回可交互元素的结构化描述。
/// </summary>
public static class BrowserAgentTool
{
    public static void Register()
    {
        Add("browser_open", "打开浏览器", "\uE774", (Func<CancellationToken, Task<string>>)OpenAsync);
        Add("browser_navigate", "打开网页", "\uE774", (Func<string, CancellationToken, Task<string>>)NavigateAsync);
        Add("browser_get_page", "读取页面", "\uE774", (Func<CancellationToken, Task<string>>)GetPageAsync);
        Add("browser_run_js", "执行脚本", "\uE943", (Func<string, CancellationToken, Task<string>>)RunJsAsync);
        Add("browser_click", "点击元素", "\uE71C", (Func<int, CancellationToken, Task<string>>)ClickAsync);
        Add("browser_type", "输入文本", "\uE70F", (Func<int, string, CancellationToken, Task<string>>)TypeAsync);
        Add("browser_press", "按下按键", "\uE756", (Func<int, string, CancellationToken, Task<string>>)PressAsync);
        Add("browser_scroll", "滚动页面", "\uE7C3", (Func<string, CancellationToken, Task<string>>)ScrollAsync);
        Add("browser_get_text", "读取正文", "\uE8D3", (Func<CancellationToken, Task<string>>)GetTextAsync);
        Add("browser_back", "后退", "\uE72B", (Func<CancellationToken, Task<string>>)BackAsync);
        Add("browser_close", "关闭浏览器", "\uE711", (Func<CancellationToken, Task<string>>)CloseAsync);
    }

    [Description("打开真实的浏览器窗口（WebView2）。需要操作网页/表单/下载/登录时先调用它，之后用 browser_get_page 读取页面元素")]
    public static async Task<string> OpenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.OpenAsync();
    }

    [Description("在浏览器中打开指定网址（http/https）")]
    public static async Task<string> NavigateAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(url)) return "错误：缺少 url 参数";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "错误：URL 无效（仅支持 http/https）";

        var result = await BrowserAutomationService.NavigateAsync(uri.ToString());
        return result == "OK"
            ? $"已打开：{uri}"
            : result;
    }

    [Description("读取浏览器当前页面：标题、URL、可交互元素列表（链接/按钮/输入框/复选框/开关等，带索引）。之后用索引调用 browser_click / browser_type / browser_press。页面未打开时自动打开浏览器")]
    public static async Task<string> GetPageAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!BrowserAutomationService.IsOpen)
            await BrowserAutomationService.OpenAsync();
        return await BrowserAutomationService.GetPageStateAsync();
    }

    [Description("在浏览器页面中执行自定义 JavaScript 脚本，实现高强度控制：模拟复杂交互、读取页面深层数据、修改页面状态/样式、注入事件等。脚本必须是立即返回结果的表达式或 IIFE（如 () => { ... }() 或 document.title），返回数字/布尔/null 原样、对象返回 JSON 文本、字符串自动去引号。常用例子：读取 document.title、点击某个元素 el.click()、提取表格数据 Array.from(document.querySelectorAll('tr')).map(...)、给输入框赋值并触发 input 事件。脚本执行失败会返回 ERROR 前缀的错误信息")]
    public static async Task<string> RunJsAsync(string script, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(script))
            return "错误：缺少 script 参数";
        if (!BrowserAutomationService.IsOpen)
            return "错误：浏览器未打开（请先调用 browser_open 或 browser_get_page）";
        return await BrowserAutomationService.RunJsAsync(script);
    }

    [Description("点击页面中的元素（索引来自 browser_get_page 的结果）")]
    public static async Task<string> ClickAsync(int elementIndex, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.ClickAsync(elementIndex);
    }

    [Description("在页面输入框中输入文本（索引来自 browser_get_page 的结果；输入后如需提交请用 browser_press 按 enter）")]
    public static async Task<string> TypeAsync(int elementIndex, string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.TypeAsync(elementIndex, text);
    }

    [Description("在页面元素上按键：enter=提交所在表单 / esc / tab（索引来自 browser_get_page 的结果）")]
    public static async Task<string> PressAsync(int elementIndex, string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(key)) return "错误：缺少 key 参数（enter / esc / tab）";
        return await BrowserAutomationService.PressAsync(elementIndex, key);
    }

    [Description("滚动浏览器页面：up / down / bottom / top")]
    public static async Task<string> ScrollAsync(string direction, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var d = direction.Trim().ToLowerInvariant();
        if (d is not ("up" or "down" or "bottom" or "top"))
            return "错误：direction 仅支持 up / down / bottom / top";
        return await BrowserAutomationService.ScrollAsync(d);
    }

    [Description("读取浏览器当前页面的正文文本（长文章/搜索结果详情用；约 6000 字符）")]
    public static async Task<string> GetTextAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!BrowserAutomationService.IsOpen)
            return "错误：浏览器未打开（请先调用 browser_open 或 browser_get_page）";
        return await BrowserAutomationService.GetPageTextAsync();
    }

    [Description("浏览器后退到上一页")]
    public static async Task<string> BackAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.BackAsync();
    }

    [Description("关闭浏览器窗口")]
    public static async Task<string> CloseAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await BrowserAutomationService.CloseAsync();
    }

    private static void Add(string name, string displayName, string glyph, Delegate method)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name })
        });
    }
}
