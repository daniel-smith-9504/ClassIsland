using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// 应用启动完成后在屏幕上常驻显示随机点名悬浮窗，并在托盘菜单中注册入口。
/// </summary>
public sealed class SecRandomFloatingWindowHostedService(
    SecRandomFloatingWindowService floatingWindows,
    ILogger<SecRandomFloatingWindowHostedService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("随机点名悬浮窗宿主服务已启动。");
        Dispatcher.UIThread.Post(() =>
        {
            if (AppBase.CurrentLifetime == ApplicationLifetime.Running)
            {
                floatingWindows.Show();
                return;
            }

            if (Application.Current is AppBase app)
            {
                app.AppStarted += AppOnAppStarted;
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void AppOnAppStarted(object? sender, EventArgs e)
    {
        if (sender is AppBase app)
        {
            app.AppStarted -= AppOnAppStarted;
        }

        logger.LogInformation("应用启动完成，开始显示随机点名悬浮窗。");
        Dispatcher.UIThread.Post(floatingWindows.Show, DispatcherPriority.Background);
    }
}
