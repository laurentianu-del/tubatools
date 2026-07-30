// EnergyStarTool — built-in tool entry that opens the EnergyStarWindow.
// Underlying engine ported from EnergyStarX (https://github.com/JasonWei512/EnergyStarX)
// Copyright 2022 Bingxing Wang — MIT licensed (see Services/EnergyStar/LICENSE.txt).

namespace TubaWinUi3.Services;

public sealed class EnergyStarTool : IBuiltinTool
{
    public string Id => "energy-star";
    public string Name => "后台节流省电神器";
    public string Description => "通过 Windows 11 效率模式 (EcoQoS) 节流后台进程以省电降温, 前台应用保持流畅 (基于 Energy Star X 内核)。";
    public string Glyph => "\uE83F";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        context.OnProgress?.Invoke("正在打开后台节流省电神器...");

        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            var window = new TubaWinUi3.Pages.EnergyStarWindow();
            window.Activate();
        });

        return Task.CompletedTask;
    }
}
