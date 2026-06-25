using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed class AiChatMessage
{
    public string Role { get; init; } = "";
    public string Content { get; init; } = "";
}

public sealed class AiChatResponse
{
    public string Content { get; init; } = "";
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}

public static class AiService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly HttpClient _streamHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static bool IsConfigured
    {
        get
        {
            var endpoint = AppSettings.Get("AiApiEndpoint");
            var model = AppSettings.Get("AiModelName");
            var key = AppSettings.Get("AiApiKey");
            return !string.IsNullOrWhiteSpace(endpoint)
                && !string.IsNullOrWhiteSpace(model)
                && !string.IsNullOrWhiteSpace(key);
        }
    }

    public static (string? Endpoint, string? Model, string? ApiKey) GetConfig()
    {
        return (
            AppSettings.Get("AiApiEndpoint"),
            AppSettings.Get("AiModelName"),
            AppSettings.Get("AiApiKey")
        );
    }

    public static void SetConfig(string? endpoint, string? model, string? apiKey)
    {
        if (endpoint is not null) AppSettings.Set("AiApiEndpoint", endpoint);
        if (model is not null) AppSettings.Set("AiModelName", model);
        if (apiKey is not null) AppSettings.Set("AiApiKey", apiKey);
    }

    public static async Task ChatStreamAsync(
        List<AiChatMessage> messages,
        Action<string> onChunk,
        Action<string>? onError = null,
        CancellationToken ct = default,
        double temperature = 0.3,
        int? maxTokens = null)
    {
        var (endpoint, model, apiKey) = GetConfig();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
        {
            onError?.Invoke("AI 服务未配置，请在设置中配置 API 地址、模型名和 API Key");
            return;
        }

        var url = endpoint.TrimEnd('/') + "/chat/completions";

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new Dictionary<string, string>
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToList(),
            ["temperature"] = temperature,
            ["stream"] = true
        };

        if (maxTokens.HasValue)
            body["max_tokens"] = maxTokens.Value;

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;

        try
        {
            request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            response = await _streamHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                if (!line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase)) continue;
                var data = line.Substring(6).Trim();

                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta) &&
                            delta.TryGetProperty("content", out var contentProp))
                        {
                            var chunk = contentProp.GetString();
                            if (chunk is not null)
                            {
                                onChunk(chunk);
                            }
                        }
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException)
        {
            onError?.Invoke("已取消");
        }
        catch (HttpRequestException ex)
        {
            onError?.Invoke($"请求失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    public static async Task<AiChatResponse> ChatAsync(
        List<AiChatMessage> messages,
        CancellationToken ct = default,
        double temperature = 0.3,
        int? maxTokens = null)
    {
        var (endpoint, model, apiKey) = GetConfig();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
            return new AiChatResponse { Success = false, Error = "AI 服务未配置，请在设置中配置 API 地址、模型名和 API Key" };

        var url = endpoint.TrimEnd('/') + "/chat/completions";

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new Dictionary<string, string>
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToList(),
            ["temperature"] = temperature
        };

        if (maxTokens.HasValue)
            body["max_tokens"] = maxTokens.Value;

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errMsg = TryExtractError(responseBody) ?? $"HTTP {(int)response.StatusCode}";
                return new AiChatResponse { Success = false, Error = errMsg };
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var content = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            int? promptTokens = null, completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : null;
            }

            return new AiChatResponse
            {
                Content = content,
                Success = true,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens
            };
        }
        catch (OperationCanceledException)
        {
            return new AiChatResponse { Success = false, Error = "已取消" };
        }
        catch (Exception ex)
        {
            return new AiChatResponse { Success = false, Error = ex.Message };
        }
    }

    public static async Task<AiChatResponse> ChatSingleAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default,
        double temperature = 0.3,
        int? maxTokens = null)
    {
        return await ChatAsync(
        [
            new AiChatMessage { Role = "system", Content = systemPrompt },
            new AiChatMessage { Role = "user", Content = userMessage }
        ], ct, temperature, maxTokens);
    }

    public static async Task<AiChatResponse> TestConnectionAsync(CancellationToken ct = default)
    {
        return await ChatSingleAsync(
            "You are a helpful assistant. Reply with exactly: OK",
            "Hello, please confirm you are working.",
            ct,
            temperature: 0,
            maxTokens: 10);
    }

    private static string? TryExtractError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString();
                return err.ToString();
            }
        }
        catch { }
        return null;
    }
}
