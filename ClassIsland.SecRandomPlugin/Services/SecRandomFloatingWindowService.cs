using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.SecRandomPlugin.Views.Windows;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// 管理随机点名悬浮窗的打开、复用与托盘菜单入口。
/// </summary>
public class SecRandomFloatingWindowService
{
    private readonly SecRandomService _service;
    private readonly ILogger<SecRandomFloatingWindowService> _logger;
    private SecRandomFloatingWindow? _window;
    private bool _trayInitialized;

    public SecRandomFloatingWindowService(SecRandomService service, ILogger<SecRandomFloatingWindowService> logger)
    {
        _service = service;
        _logger = logger;
    }

    public void Show()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Show);
            return;
        }

        try
        {
            if (_window is { IsVisible: true })
            {
                _window.Activate();
                InitializeTrayMenu();
                return;
            }

            _window = new SecRandomFloatingWindow(_service);
            _window.Closed += (_, _) => _window = null;
            _window.Show();
            _logger.LogInformation("随机点名悬浮窗已显示：位置 {X},{Y}，尺寸 {Width}x{Height}",
                _window.Position.X, _window.Position.Y, _window.Width, _window.Height);
            InitializeTrayMenu();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "显示随机点名悬浮窗失败。");
            _window = null;
        }
    }

    /// <summary>
    /// 把入口挂到 ClassIsland 的托盘「更多选项」菜单中，方便在窗口被遮挡时重新打开。
    /// </summary>
    public void InitializeTrayMenu()
    {
        if (_trayInitialized)
        {
            return;
        }

        try
        {
            var taskBarIconService = IAppHost.TryGetService<ITaskBarIconService>();
            if (taskBarIconService == null)
            {
                return;
            }

            var menuItem = new NativeMenuItem("随机点名");
            var submenu = new NativeMenu();
            var showItem = new NativeMenuItem("显示悬浮窗");
            showItem.Click += (_, _) => Show();
            var drawItem = new NativeMenuItem("立即抽取");
            drawItem.Click += (_, _) => DrawFromTray();
            submenu.Items.Add(showItem);
            submenu.Items.Add(drawItem);
            menuItem.Menu = submenu;

            taskBarIconService.MoreOptionsMenuItems.Add(menuItem);
            _trayInitialized = true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "注册随机点名托盘菜单失败。");
        }
    }

    private void DrawFromTray()
    {
        Show();
        var outcome = _service.Draw();
        if (!outcome.IsSuccess)
        {
            _window?.SetResult(SecRandomService.Describe(outcome));
        }
    }
}
