using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 命令执行工具：cmd / PowerShell。复用 <see cref="ScriptRunnerService"/>
/// （输出捕获 + 超时强杀），取代 AiAssistantService 内重复的 cmd.exe 实现。
/// 危险操作，需用户确认后执行。
/// </summary>
public static class CommandAgentTool
{
    public static void Register()
    {
        Add("run_command", "执行命令", "\uE756", (Func<string, string, int?, CancellationToken, Task<string>>)RunCommandAsync);
        Add("run_powershell", "执行 PowerShell", "\uE756", (Func<string, string, int?, CancellationToken, Task<string>>)RunPowerShellAsync);
    }

    [Description("执行 cmd 命令（需用户确认后执行；支持超时，默认 60 秒；长时间命令如系统扫描请设置更大 timeout）")]
    public static async Task<string> RunCommandAsync(string cmd, string reason, int? timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return "错误：缺少 cmd 参数";
        var timeoutSec = Math.Clamp(timeout ?? 60, 5, 3600);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            var result = await ScriptRunnerService.RunAsync(new ScriptRunRequest
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                OutputEncoding = Encoding.UTF8
            }, ct: timeoutCts.Token);

            return FormatResult(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "命令执行已取消";
        }
        catch (OperationCanceledException)
        {
            return $"命令执行超时（{timeoutSec} 秒），已强制终止";
        }
        catch (Exception ex)
        {
            return $"执行失败：{ex.Message}";
        }
    }

    [Description("执行 PowerShell 脚本（需用户确认后执行；支持超时，默认 60 秒）")]
    public static async Task<string> RunPowerShellAsync(string script, string reason, int? timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(script)) return "错误：缺少 script 参数";
        var timeoutSec = Math.Clamp(timeout ?? 60, 5, 3600);

        try
        {
            // -EncodedCommand：UTF-16LE Base64，避免引号转义问题
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            var result = await ScriptRunnerService.RunAsync(new ScriptRunRequest
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                OutputEncoding = Encoding.UTF8
            }, ct: timeoutCts.Token);

            return FormatResult(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return "脚本执行已取消";
        }
        catch (OperationCanceledException)
        {
            return $"脚本执行超时（{timeoutSec} 秒），已强制终止";
        }
        catch (Exception ex)
        {
            return $"执行失败：{ex.Message}";
        }
    }

    private static string FormatResult(ScriptRunResult result)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.Output))
            sb.AppendLine(result.Output.Trim());
        if (!string.IsNullOrWhiteSpace(result.Error))
            sb.AppendLine($"[stderr] {result.Error.Trim()}");
        sb.AppendLine($"退出码：{result.ExitCode}");
        return sb.ToString();
    }

    private static void Add(string name, string displayName, string glyph, Delegate method)
    {
        AgentToolRegistry.Register(new AgentTool
        {
            Name = name,
            DisplayName = displayName,
            Glyph = glyph,
            Function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions { Name = name }),
            RequiresConfirmation = true,
            ConfirmKind = "run_command",
            DefaultReason = "AI 请求执行此命令",
        });
    }
}
