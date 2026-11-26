using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Views.Windows;

public partial class SecRandomFloatingWindow : Window
{
    private readonly SecRandomService _service;

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
        _service.DrawCompleted += ServiceOnDrawCompleted;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _service.DrawCompleted -= ServiceOnDrawCompleted;
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

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
