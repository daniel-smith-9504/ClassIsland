using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Enums;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// 应用启动完成后在屏幕上常驻显示随机点名悬浮窗，并在托盘菜单中注册入口。
/// </summary>
public sealed class SecRandomFloatingWindowHostedService(SecRandomFloatingWindowService floatingWindows)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
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

        Dispatcher.UIThread.Post(floatingWindows.Show, DispatcherPriority.Background);
    }
}
