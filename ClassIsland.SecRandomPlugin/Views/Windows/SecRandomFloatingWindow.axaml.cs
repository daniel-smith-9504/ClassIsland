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
        if (x != int.MinValue && y != int.MinValue && IsOnAnyScreen(new PixelPoint(x, y)))
        {
            Position = new PixelPoint(x, y);
            return;
        }

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(Bounds.Width == 0 ? Width : Bounds.Width);
        var height = (int)Math.Ceiling(Bounds.Height == 0 ? 180 : Bounds.Height);
        Position = new PixelPoint(
            Math.Max(area.X, area.Right - width - DefaultMargin),
            Math.Max(area.Y, area.Bottom - height - DefaultMargin));
    }

    private bool IsOnAnyScreen(PixelPoint point) =>
        Screens.All.Any(screen =>
            point.X >= screen.Bounds.X && point.X < screen.Bounds.Right &&
            point.Y >= screen.Bounds.Y && point.Y < screen.Bounds.Bottom);

    private void SavePosition()
    {
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
