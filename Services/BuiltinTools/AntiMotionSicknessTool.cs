using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class AntiMotionSicknessTool : IBuiltinTool
{
    public string Id => "anti-motion-sickness";
    public string Name => "游戏防晕3D";
    public string Description => "屏幕中央准星+四边标记辅助，缓解3D眩晕。一键帮你节约13块钱！";
    public string Glyph => "\uE7FC";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new AntiMotionSicknessWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}
