using Avalonia.Threading;
using ClassIsland.SecRandomPlugin.Views.Windows;

namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// 管理随机点名悬浮窗的打开与复用。
/// </summary>
public class SecRandomFloatingWindowService(SecRandomService service)
{
    private SecRandomFloatingWindow? _window;

    public void Show()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Show);
            return;
        }

        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        _window = new SecRandomFloatingWindow(service);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
