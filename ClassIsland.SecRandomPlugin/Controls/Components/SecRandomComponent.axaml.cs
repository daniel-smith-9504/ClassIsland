using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.SecRandomPlugin.Models;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Controls.Components;

[ComponentInfo(
    "3D6E5A7C-9B24-4F8B-9D61-5C2A7E4B8F13",
    "随机点名",
    "\uf323",
    "显示随机点名结果，并支持一键抽取。")]
public partial class SecRandomComponent : ComponentBase<SecRandomComponentSettings>
{
    private readonly SecRandomService _service;

    public SecRandomComponent(SecRandomService service)
    {
        _service = service;
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResultText.Text = string.IsNullOrWhiteSpace(Settings.IdleText) ? "点击抽取" : Settings.IdleText;
        _service.DrawCompleted += ServiceOnDrawCompleted;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _service.DrawCompleted -= ServiceOnDrawCompleted;
    }

    private void ServiceOnDrawCompleted(object? sender, DrawCompletedEventArgs e) =>
        SetResultText(string.Join("、", e.Students.Select(student => student.Name)));

    private void DrawButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var outcome = _service.Draw();
        SetResultText(outcome.IsSuccess
            ? string.Join("、", outcome.Selected.Select(student => student.Name))
            : SecRandomService.Describe(outcome));
    }

    private void SetResultText(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ResultText.Text = text;
        }
        else
        {
            Dispatcher.UIThread.Post(() => ResultText.Text = text);
        }
    }
}
