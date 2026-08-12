using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 旧持久化格式 <see cref="AiChatMessage"/> 与 Microsoft.Extensions.AI
/// <see cref="ChatMessage"/> 之间的双向转换，保证历史对话 JSON 无缝兼容。
/// </summary>
public static class AgentMessageConverter
{
    public static List<AiChatMessage> ToAiMessages(List<ChatMessage> messages)
    {
        var result = new List<AiChatMessage>(messages.Count);
        foreach (var m in messages)
        {
            if (m.Role == ChatRole.System)
                result.Add(AiChatMessage.System(m.Text ?? ""));
            else if (m.Role == ChatRole.User)
                result.Add(AiChatMessage.User(m.Text ?? ""));
            else if (m.Role == ChatRole.Assistant)
            {
                var toolCalls = new List<AiToolCallItem>();
                foreach (var c in m.Contents.OfType<FunctionCallContent>())
                {
                    toolCalls.Add(new AiToolCallItem
                    {
                        Id = c.CallId ?? "",
                        Name = c.Name ?? "",
                        Arguments = c.Arguments is null ? "" : JsonSerializer.Serialize(c.Arguments)
                    });
                }
                result.Add(AiChatMessage.Assistant(m.Text ?? "", toolCalls.Count > 0 ? toolCalls : null));
            }
            else if (m.Role == ChatRole.Tool)
            {
                var frc = m.Contents.OfType<FunctionResultContent>().FirstOrDefault();
                result.Add(AiChatMessage.Tool(frc?.CallId ?? "", frc?.Result?.ToString() ?? ""));
            }
        }
        return result;
    }

    public static List<ChatMessage> ToChatMessages(List<AiChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "system":
                    result.Add(new ChatMessage(ChatRole.System, m.Content));
                    break;
                case "user":
                    result.Add(new ChatMessage(ChatRole.User, m.Content));
                    break;
                case "assistant":
                {
                    var msg = new ChatMessage(ChatRole.Assistant, m.Content);
                    if (m.ToolCalls is not null)
                    {
                        foreach (var tc in m.ToolCalls)
                        {
                            msg.Contents.Add(new FunctionCallContent(
                                callId: tc.Id,
                                name: tc.Name,
                                arguments: AgentArgsJson.ParseToDictionary(tc.Arguments)));
                        }
                    }
                    result.Add(msg);
                    break;
                }
                case "tool":
                    result.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId: m.ToolCallId ?? "", result: m.Content)]));
                    break;
            }
        }
        return result;
    }
}
