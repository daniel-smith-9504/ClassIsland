using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
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
    private readonly SecRandomFloatingWindowService _floatingWindows;

    public SecRandomSettingsPage(SecRandomService service, SecRandomFloatingWindowService floatingWindows)
    {
        Service = service;
        _floatingWindows = floatingWindows;
        InitializeComponent();
        DataContext = this;
    }

    public SecRandomService Service { get; }

    public ObservableCollection<string> ScopeValues { get; } = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Service.Students.CollectionChanged += StudentsOnCollectionChanged;
        foreach (var student in Service.Students)
        {
            student.PropertyChanged += StudentOnPropertyChanged;
        }

        ScopeTypeCombo.SelectedIndex = Service.Config.ScopeType;
        RefreshScopeValues();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Service.Students.CollectionChanged -= StudentsOnCollectionChanged;
        foreach (var student in Service.Students)
        {
            student.PropertyChanged -= StudentOnPropertyChanged;
        }

        Service.SaveAll();
    }

    private void StudentsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (Student student in e.OldItems)
            {
                student.PropertyChanged -= StudentOnPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (Student student in e.NewItems)
            {
                student.PropertyChanged += StudentOnPropertyChanged;
            }
        }

        RefreshScopeValues();
    }

    private void StudentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Student.Group) or nameof(Student.Gender) or nameof(Student.Tag))
        {
            RefreshScopeValues();
        }
    }

    public void RefreshScopeValues()
    {
        var values = Service.GetScopeValues(Service.Config.ScopeType);
        ScopeValues.Clear();
        foreach (var value in values)
        {
            ScopeValues.Add(value);
        }

        var current = Service.Config.ScopeValue;
        if (!values.Contains(current))
        {
            current = values.FirstOrDefault() ?? "";
        }

        ScopeValueCombo.SelectedItem = string.IsNullOrEmpty(current) ? null : current;
        Service.Config.ScopeValue = current;
        ScopeValueCombo.IsVisible = Service.Config.ScopeType != SecRandomConstants.ScopeTypeAll;
    }

    private void ScopeType_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ScopeTypeCombo.SelectedIndex < 0)
        {
            return;
        }

        Service.Config.ScopeType = ScopeTypeCombo.SelectedIndex;
        RefreshScopeValues();
    }

    private void ScopeValue_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Service.Config.ScopeValue = ScopeValueCombo.SelectedItem as string ?? "";
    }

    private void AddStudent_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text?.Trim() ?? "";
        var id = NewIdBox.Text?.Trim() ?? "";
        var group = NewGroupBox.Text?.Trim() ?? "";
        var tag = NewTagBox.Text?.Trim() ?? "";
        if (name.Length == 0 && id.Length == 0)
        {
            RosterStatusText.Text = "请至少填写姓名或学号。";
            return;
        }

        var student = Service.AddStudent(name, id, group);
        student.Tag = tag;
        NewNameBox.Text = "";
        NewIdBox.Text = "";
        NewGroupBox.Text = "";
        NewTagBox.Text = "";
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

    private void OpenFloatingWindow_OnClick(object? sender, RoutedEventArgs e) => _floatingWindows.Show();
}
