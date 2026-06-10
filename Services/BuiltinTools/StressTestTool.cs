using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class StressTestTool : IBuiltinTool
{
    public string Id => "stress-test";
    public string Name => "烤机测试";
    public string Description => "CPU/GPU 压力测试，支持单烤和双烤模式，实时监控温度、占用率、频率和功耗。";
    public string Glyph => "\uE8A3";
    public string Category => "监测工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        StressTestWindow.Show();
        return Task.CompletedTask;
    }
}
