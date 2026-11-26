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
    /// <summary>
    /// 从 SecRandom 导入时的历史次数基线（累计次数减去已导入的记录数），用于在历史被裁剪时仍然保持公平权重。
    /// </summary>
    [ObservableProperty] private int _baseDrawCount;

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Id) ? Name : $"{Name}（{Id}）";

    /// <summary>
    /// 学生的标签以空白分隔存储，与 SecRandom 一致；这里按标签逐个拆分以便范围筛选。
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> TagValues => SecRandomTagHelper.Split(Tag);
}

public static class SecRandomTagHelper
{
    private static readonly char[] Separators = [' ', ',', '，', ';', '；', '|', '/', '\\', '\n', '\t'];

    public static IReadOnlyList<string> Split(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .ToList();
}
