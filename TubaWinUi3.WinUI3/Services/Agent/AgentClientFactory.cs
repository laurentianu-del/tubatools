using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 创建对接现有 OpenAI 兼容端点（AppSettings：AiApiEndpoint / AiModelName / AiApiKey）
/// 的 IChatClient。使用官方 OpenAI SDK + M.E.AI 适配层（AsIChatClient），
/// 不再手写 SSE 流式解析与 JSON Schema。
/// </summary>
public static class AgentClientFactory
{
    public static IChatClient CreateClient()
    {
        var (endpoint, model, apiKey) = AiService.GetConfig();

        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = "https://" + endpoint;
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.TrimEnd('/'))
        });

        return openAiClient.GetChatClient(model).AsIChatClient();
    }
}
