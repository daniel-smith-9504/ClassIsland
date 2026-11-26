using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.SecRandomPlugin.Models;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Views.SettingsPages;

[SettingsPageInfo("classisland.secrandom.settings", "随机点名")]
public partial class SecRandomSettingsPage : SettingsPageBase
{
    private readonly SecRandomFloatingWindowService _floatingWindows;
    private readonly SecRandomDataImporter _importer;
    private SecRandomSourceInfo? _importSource;

    public SecRandomSettingsPage(SecRandomService service, SecRandomFloatingWindowService floatingWindows,
        SecRandomDataImporter importer)
    {
        Service = service;
        _floatingWindows = floatingWindows;
        _importer = importer;
        InitializeComponent();
        DataContext = this;
    }

    public SecRandomService Service { get; }

    public ObservableCollection<string> ScopeValues { get; } = [];

    public ObservableCollection<SecRandomProfileSummary> ImportProfiles { get; } = [];

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

    private async void PickSecRandomFile_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 SecRandom 数据文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SecRandom 数据")
                {
                    Patterns = ["*.srl", "*.srhdb", "*.zip", "*.json"]
                },
                FilePickerFileTypes.All
            ]
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SecRandomPathBox.Text = path;
        ScanSecRandom();
    }

    private async void PickSecRandomFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 SecRandom 的 data 目录",
            AllowMultiple = false
        });

        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SecRandomPathBox.Text = path;
        ScanSecRandom();
    }

    private void ScanSecRandom_OnClick(object? sender, RoutedEventArgs e) => ScanSecRandom();

    private void ScanSecRandom()
    {
        var path = SecRandomPathBox.Text?.Trim() ?? "";
        if (path.Length == 0)
        {
            SetImportStatus("请先选择或填写 SecRandom 数据路径。");
            return;
        }

        try
        {
            _importSource = _importer.Inspect(path);
            ImportProfiles.Clear();
            foreach (var profile in _importSource.Profiles)
            {
                ImportProfiles.Add(profile);
            }

            SecRandomProfileCombo.SelectedIndex = ImportProfiles.Count > 0 ? 0 : -1;

            var parts = new List<string>
            {
                ImportProfiles.Count > 0 ? $"找到 {ImportProfiles.Count} 个档案" : "没有找到可导入的名单档案"
            };
            if (!_importSource.HasRoster)
            {
                parts.Add("未检测到名单库");
            }

            if (!_importSource.HasHistory)
            {
                parts.Add("未检测到历史库");
            }

            if (_importSource.Warnings.Count > 0)
            {
                parts.Add("提示：" + string.Join("；", _importSource.Warnings.Take(3)));
            }

            SetImportStatus(string.Join("，", parts) + "。");
        }
        catch (Exception ex)
        {
            _importSource = null;
            ImportProfiles.Clear();
            SetImportStatus($"扫描失败：{ex.Message}");
        }
    }

    private void ImportSecRandom_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_importSource == null || SecRandomProfileCombo.SelectedItem is not SecRandomProfileSummary summary)
        {
            SetImportStatus("请先扫描并选择一个档案。");
            return;
        }

        var includeHistory = ImportHistoryCheck.IsChecked == true;
        var replaceExisting = ReplaceRosterCheck.IsChecked == true;
        try
        {
            var profile = _importer.Import(_importSource.SourcePath, summary.Name, includeHistory);
            var outcome = Service.Import(profile, replaceExisting, includeHistory);
            RefreshScopeValues();

            var historyText = includeHistory ? $"，历史 {outcome.ImportedHistory} 条" : "";
            SetImportStatus($"已导入档案「{summary.Name}」：新增 {outcome.AddedStudents} 名学生，更新 {outcome.UpdatedStudents} 名{historyText}。");
            RosterStatusText.Text = $"当前名单共 {Service.Students.Count} 名学生。";
        }
        catch (Exception ex)
        {
            SetImportStatus($"导入失败：{ex.Message}");
        }
    }

    private void SetImportStatus(string text) => SecRandomImportStatusText.Text = text;

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
