using System.Net.Http;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class AgentErrorPolicyTests
{
    [Fact]
    public void FormatApiError_UnauthorizedAccess_HintsApiKey()
    {
        var text = AgentErrorPolicy.FormatApiError(new UnauthorizedAccessException("The api key sk-xxx was invalid"));

        Assert.Contains("API Key 无效或没有权限", text);
        Assert.Contains("sk-xxx", text);
        Assert.Contains("设置 → AI 服务", text);
    }

    [Fact]
    public void FormatApiError_HttpRequestWithStatus_ShowsCode()
    {
        var ex = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, System.Net.HttpStatusCode.NotFound);
        var text = AgentErrorPolicy.FormatApiError(ex);

        Assert.Contains("HTTP 404", text);
        Assert.Contains("404", text);
    }

    [Fact]
    public void FormatApiError_IncludesInnerExceptionChain()
    {
        var inner = new InvalidOperationException("模型不存在：model-xyz");
        var outer = new Exception("请求失败", inner);
        var text = AgentErrorPolicy.FormatApiError(outer);

        Assert.Contains("模型不存在：model-xyz", text);
    }

    [Fact]
    public void FormatApiError_NullStatusCode_NetworkHint()
    {
        var ex = new HttpRequestException("连接被拒绝");
        var text = AgentErrorPolicy.FormatApiError(ex);

        Assert.Contains("网络连接失败", text);
    }
}
