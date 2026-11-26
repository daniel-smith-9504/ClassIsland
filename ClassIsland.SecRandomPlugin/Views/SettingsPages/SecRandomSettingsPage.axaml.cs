using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.SecRandomPlugin.Models;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Views.SettingsPages;

[SettingsPageInfo("classisland.secrandom.settings", "随机点名")]
public partial class SecRandomSettingsPage : SettingsPageBase
{
    public SecRandomSettingsPage(SecRandomService service)
    {
        Service = service;
        InitializeComponent();
        DataContext = this;
    }

    public SecRandomService Service { get; }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Service.SaveAll();
    }

    private void AddStudent_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text?.Trim() ?? "";
        var id = NewIdBox.Text?.Trim() ?? "";
        var group = NewGroupBox.Text?.Trim() ?? "";
        if (name.Length == 0 && id.Length == 0)
        {
            RosterStatusText.Text = "请至少填写姓名或学号。";
            return;
        }

        Service.AddStudent(name, id, group);
        NewNameBox.Text = "";
        NewIdBox.Text = "";
        NewGroupBox.Text = "";
        RosterStatusText.Text = "已添加学生。";
    }

    private void RemoveStudent_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Student student })
        {
            Service.RemoveStudent(student);
        }
    }

    private void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        var count = Service.ImportStudents(ImportBox.Text ?? "");
        RosterStatusText.Text = count > 0 ? $"已导入 {count} 名学生。" : "没有可导入的内容，请检查每行格式。";
        if (count > 0)
        {
            ImportBox.Text = "";
        }
    }

    private void ClearRoster_OnClick(object? sender, RoutedEventArgs e)
    {
        Service.ClearRoster();
        RosterStatusText.Text = "已清空名单。";
    }

    private void Draw_OnClick(object? sender, RoutedEventArgs e)
    {
        var outcome = Service.Draw();
        DrawResultText.Text = SecRandomService.Describe(outcome);
    }

    private void ClearHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        Service.ResetHistory();
        DrawResultText.Text = "已清空抽取历史。";
    }
}
