using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Views.Windows;

public partial class SecRandomFloatingWindow : Window
{
    private const int DefaultMargin = 24;

    private readonly SecRandomService _service;
    private bool _dragging;
    private bool _positionApplied;
    private PixelPoint _dragStartPointer;
    private PixelPoint _dragStartWindow;

    public SecRandomFloatingWindow(SecRandomService service)
    {
        _service = service;
        InitializeComponent();
        DataContext = this;
    }

    public SecRandomService Service => _service;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RestoreOrPlacePosition();
        _service.DrawCompleted += ServiceOnDrawCompleted;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _service.DrawCompleted -= ServiceOnDrawCompleted;
        SavePosition();
    }

    private void ServiceOnDrawCompleted(object? sender, DrawCompletedEventArgs e) =>
        ResultText.Text = string.Join("、", e.Students.Select(student => student.Name));

    public void SetResult(string text) => ResultText.Text = text;

    private void Draw_OnClick(object? sender, RoutedEventArgs e)
    {
        var outcome = _service.Draw();
        ResultText.Text = outcome.IsSuccess
            ? string.Join("、", outcome.Selected.Select(student => student.Name))
            : SecRandomService.Describe(outcome);
    }

    private void RestoreOrPlacePosition()
    {
        var x = _service.Config.FloatingWindowX;
        var y = _service.Config.FloatingWindowY;

        // 旧版本可能把未初始化坐标写成 0,0，这里把它当作未设置，避免窗口卡在左上角。
        if (x != int.MinValue && y != int.MinValue && (x != 0 || y != 0))
        {
            Position = ClampToWorkingArea(new PixelPoint(x, y));
            _positionApplied = true;
            return;
        }

        PlaceAtDefaultPosition();
        _positionApplied = true;
    }

    private void PlaceAtDefaultPosition()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null)
        {
            return;
        }

        var area = screen.WorkingArea;
        Position = new PixelPoint(
            Math.Max(area.X, area.Right - WindowWidth - DefaultMargin),
            Math.Max(area.Y, area.Bottom - WindowHeight - DefaultMargin));
    }

    private PixelPoint ClampToWorkingArea(PixelPoint target)
    {
        var screen = Screens.All.FirstOrDefault(candidate => Contains(candidate.Bounds, target))
                     ?? Screens.Primary
                     ?? Screens.All.FirstOrDefault();
        if (screen == null)
        {
            return target;
        }

        var area = screen.WorkingArea;
        return new PixelPoint(
            Math.Clamp(target.X, area.X, Math.Max(area.X, area.Right - WindowWidth)),
            Math.Clamp(target.Y, area.Y, Math.Max(area.Y, area.Bottom - WindowHeight)));
    }

    private int WindowWidth => (int)Math.Ceiling(Width is { } width && !double.IsNaN(width) ? width : 360);

    private int WindowHeight => (int)Math.Ceiling(Height is { } height && !double.IsNaN(height) ? height : 176);

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.X && point.X < rect.Right &&
        point.Y >= rect.Y && point.Y < rect.Bottom;

    private void SavePosition()
    {
        if (!_positionApplied)
        {
            return;
        }

        _service.Config.FloatingWindowX = Position.X;
        _service.Config.FloatingWindowY = Position.Y;
    }

    private void DragHandle_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        _dragStartPointer = this.PointToScreen(e.GetPosition(this));
        _dragStartWindow = Position;
        e.Pointer.Capture(DragHandle);
        e.Handled = true;
    }

    private void DragHandle_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var current = this.PointToScreen(e.GetPosition(this));
        Position = new PixelPoint(
            _dragStartWindow.X + current.X - _dragStartPointer.X,
            _dragStartWindow.Y + current.Y - _dragStartPointer.Y);
        e.Handled = true;
    }

    private void DragHandle_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        SavePosition();
        e.Handled = true;
    }
}
