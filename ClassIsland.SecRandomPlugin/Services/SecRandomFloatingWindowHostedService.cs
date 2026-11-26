using Avalonia.Threading;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// 应用启动后在屏幕上常驻显示随机点名悬浮窗。
/// </summary>
public sealed class SecRandomFloatingWindowHostedService(SecRandomFloatingWindowService floatingWindows)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.Post(floatingWindows.Show, DispatcherPriority.Background);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
