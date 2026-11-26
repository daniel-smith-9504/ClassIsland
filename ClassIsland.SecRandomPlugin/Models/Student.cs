using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.SecRandomPlugin.Models;

/// <summary>
/// 名单中的学生。记录以 <see cref="RecordId"/> 作为稳定标识，改名或改学号不会影响历史统计。
/// </summary>
public partial class Student : ObservableObject
{
    [ObservableProperty] private Guid _recordId = Guid.NewGuid();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _group = "";
    [ObservableProperty] private string _gender = "";
    [ObservableProperty] private string _tag = "";
    /// <summary>
    /// 该学生单独的冷却时长（分钟）。为 0 时跟随插件设置中的全局冷却。
    /// </summary>
    [ObservableProperty] private int _cooldownMinutes;
    [ObservableProperty] private bool _isEnabled = true;

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Id) ? Name : $"{Name}（{Id}）";
}
