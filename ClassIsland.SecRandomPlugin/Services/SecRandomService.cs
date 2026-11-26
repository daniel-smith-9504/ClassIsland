using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Threading;
using ClassIsland.SecRandomPlugin.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SecRandomPlugin.Services;

public sealed class DrawCompletedEventArgs(IReadOnlyList<Student> students, string batchId) : EventArgs
{
    public IReadOnlyList<Student> Students { get; } = students;
    public string BatchId { get; } = batchId;
    public string Summary => string.Join("、", Students.Select(student => student.Name));
}

public sealed class ImportOutcome
{
    public int AddedStudents { get; init; }
    public int UpdatedStudents { get; init; }
    public int ImportedHistory { get; init; }
    public int SkippedHistory { get; init; }
}

/// <summary>
/// 随机点名的运行时服务：维护名单、设置与抽取历史，并负责持久化和抽取事件。
/// </summary>
public class SecRandomService
{
    private const string ConfigFileName = "settings.json";
    private const string RosterFileName = "roster.json";
    private const string HistoryFileName = "history.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SecRandomPaths _paths;
    private readonly ILogger<SecRandomService> _logger;
    private readonly object _gate = new();

    public SecRandomService(SecRandomPaths paths, ILogger<SecRandomService> logger)
    {
        _paths = paths;
        _logger = logger;
        Directory.CreateDirectory(paths.ConfigFolder);

        Config = Load<SecRandomConfig>(ConfigFileName) ?? new SecRandomConfig();
        Config.PropertyChanged += (_, _) => SaveConfig();

        foreach (var student in Load<List<Student>>(RosterFileName) ?? [])
        {
            if (student.RecordId == Guid.Empty)
            {
                student.RecordId = Guid.NewGuid();
            }

            Students.Add(student);
        }

        foreach (var record in (Load<List<DrawRecord>>(HistoryFileName) ?? []).OrderByDescending(record => record.Time))
        {
            History.Add(record);
        }
    }

    public event EventHandler<DrawCompletedEventArgs>? DrawCompleted;

    public SecRandomConfig Config { get; }

    public ObservableCollection<Student> Students { get; } = [];

    public ObservableCollection<DrawRecord> History { get; } = [];

    /// <summary>
    /// 执行一次抽取并写入历史。
    /// </summary>
    /// <param name="countOverride">本次抽取人数。为 null 时使用插件设置中的人数。</param>
    public DrawOutcome Draw(int? countOverride = null)
    {
        lock (_gate)
        {
            var drawCounts = new Dictionary<Guid, int>();
            var lastDrawTimes = new Dictionary<Guid, DateTime>();
            foreach (var student in Students)
            {
                drawCounts[student.RecordId] = Math.Max(0, student.BaseDrawCount);
            }

            foreach (var record in History)
            {
                drawCounts[record.RecordId] = drawCounts.GetValueOrDefault(record.RecordId) + 1;
                if (!lastDrawTimes.TryGetValue(record.RecordId, out var last) || record.Time > last)
                {
                    lastDrawTimes[record.RecordId] = record.Time;
                }
            }

            var outcome = DrawEngine.Draw(
                Students.ToList(),
                drawCounts,
                lastDrawTimes,
                countOverride ?? Config.DrawCount,
                Config,
                DateTime.Now);

            if (!outcome.IsSuccess)
            {
                return outcome;
            }

            var now = DateTime.Now;
            var batchId = Guid.NewGuid().ToString("N");
            var records = outcome.Selected.Select(student => new DrawRecord
            {
                RecordId = student.RecordId,
                Name = student.Name,
                StudentId = student.Id,
                Group = student.Group,
                Time = now,
                BatchId = batchId
            }).ToList();

            RunOnUiThread(() =>
            {
                for (var i = records.Count - 1; i >= 0; i--)
                {
                    History.Insert(0, records[i]);
                }
            });

            SaveHistory();
            SaveConfig();
            DrawCompleted?.Invoke(this, new DrawCompletedEventArgs(outcome.Selected, batchId));
            return outcome;
        }
    }

    public Student AddStudent(string name, string id = "", string group = "")
    {
        var student = new Student
        {
            Name = name.Trim(),
            Id = id.Trim(),
            Group = group.Trim()
        };
        RunOnUiThread(() => Students.Add(student));
        SaveRoster();
        return student;
    }

    public void RemoveStudent(Student student)
    {
        RunOnUiThread(() => Students.Remove(student));
        SaveRoster();
    }

    public void ClearRoster()
    {
        RunOnUiThread(Students.Clear);
        SaveRoster();
    }

    /// <summary>
    /// 从多行文本导入学生。
    /// 每行格式：姓名，或 学号,姓名，或 学号,姓名,分组，或 学号,姓名,分组,标签。
    /// </summary>
    public int ImportStudents(string text)
    {
        var imported = new List<Student>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split([',', '，', '\t'], StringSplitOptions.TrimEntries);
            string id = "", name = "", group = "", tag = "";
            switch (parts.Length)
            {
                case 1:
                    name = parts[0];
                    break;
                case 2:
                    id = parts[0];
                    name = parts[1];
                    break;
                case 3:
                    id = parts[0];
                    name = parts[1];
                    group = parts[2];
                    break;
                default:
                    id = parts[0];
                    name = parts[1];
                    group = parts[2];
                    tag = parts[3];
                    break;
            }

            if (name.Length == 0 && id.Length == 0)
            {
                continue;
            }

            imported.Add(new Student { Id = id, Name = name, Group = group, Tag = tag });
        }

        if (imported.Count == 0)
        {
            return 0;
        }

        RunOnUiThread(() =>
        {
            foreach (var student in imported)
            {
                Students.Add(student);
            }
        });
        SaveRoster();
        return imported.Count;
    }

    public void ResetHistory()
    {
        RunOnUiThread(() =>
        {
            History.Clear();
            foreach (var student in Students)
            {
                student.BaseDrawCount = 0;
            }
        });
        SaveRoster();
        SaveHistory();
    }

    /// <summary>
    /// 把从 SecRandom 原项目数据中读取的档案合并进插件。
    /// </summary>
    /// <param name="profile">导入的档案数据。</param>
    /// <param name="replaceExisting">是否先清空现有名单与历史。</param>
    /// <param name="includeHistory">是否导入历史记录（否则仅保留累计次数基线）。</param>
    public ImportOutcome Import(SecRandomImportProfile profile, bool replaceExisting, bool includeHistory)
    {
        lock (_gate)
        {
            var added = 0;
            var updated = 0;
            var importedHistory = 0;
            var skippedHistory = 0;

            RunOnUiThread(() =>
            {
                if (replaceExisting)
                {
                    Students.Clear();
                    History.Clear();
                }

                var existing = Students
                    .GroupBy(student => student.RecordId)
                    .ToDictionary(group => group.Key, group => group.First());

                foreach (var source in profile.Students)
                {
                    if (existing.TryGetValue(source.RecordId, out var target))
                    {
                        target.Name = source.Name;
                        target.Id = source.Id;
                        target.Group = source.Group;
                        target.Gender = source.Gender;
                        target.Tag = source.Tag;
                        target.IsEnabled = source.IsEnabled;
                        target.BaseDrawCount = Math.Max(target.BaseDrawCount, source.BaseDrawCount);
                        updated++;
                        continue;
                    }

                    Students.Add(source);
                    existing[source.RecordId] = source;
                    added++;
                }

                if (includeHistory)
                {
                    var merged = History
                        .Concat(profile.History)
                        .GroupBy(HistoryKey)
                        .Select(group => group.First())
                        .OrderByDescending(record => record.Time)
                        .ToList();
                    importedHistory = merged.Count - History.Count;
                    skippedHistory = profile.History.Count - importedHistory;

                    History.Clear();
                    foreach (var record in merged)
                    {
                        History.Add(record);
                    }
                }
            });

            SaveRoster();
            SaveHistory();

            return new ImportOutcome
            {
                AddedStudents = added,
                UpdatedStudents = updated,
                ImportedHistory = importedHistory,
                SkippedHistory = skippedHistory
            };
        }
    }

    private static string HistoryKey(DrawRecord record) =>
        $"{record.RecordId:N}|{record.Time.Ticks}|{record.BatchId}";

    public void SaveAll()
    {
        SaveConfig();
        SaveRoster();
        SaveHistory();
    }

    public static string Describe(DrawOutcome outcome) => outcome.Status switch
    {
        DrawStatus.Success => $"抽取结果：{string.Join("、", outcome.Selected.Select(student => student.Name))}",
        DrawStatus.NoCandidates => "名单为空，请先添加或导入学生。",
        DrawStatus.NoCandidatesInScope => "当前抽取范围内没有可抽取的学生，请检查抽取范围或名单。",
        DrawStatus.RepeatLimitExhausted => "可用学生不足（可能已达重复上限），请调整重复控制或重置历史。",
        DrawStatus.NoEligibleCandidates => "没有可抽取的学生（可能都在冷却中）。",
        _ => "抽取失败。"
    };

    /// <summary>
    /// 获取当前名单中某个抽取范围下的可选项（分组、性别或标签）。
    /// </summary>
    public IReadOnlyList<string> GetScopeValues(int scopeType)
    {
        IEnumerable<string> values = scopeType switch
        {
            SecRandomConstants.ScopeTypeGroup => Students.Select(student => student.Group),
            SecRandomConstants.ScopeTypeGender => Students.Select(student => student.Gender),
            SecRandomConstants.ScopeTypeTag => Students.SelectMany(student => student.TagValues),
            _ => []
        };

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct()
            .OrderBy(value => value)
            .ToList();
    }

    private void SaveConfig() => Save(ConfigFileName, Config);

    private void SaveRoster() => Save(RosterFileName, Students.ToList());

    private void SaveHistory() => Save(HistoryFileName, History.ToList());

    private void Save<T>(string fileName, T value)
    {
        try
        {
            var path = Path.Combine(_paths.ConfigFolder, fileName);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, path, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存随机点名数据文件 {FileName} 失败。", fileName);
        }
    }

    private T? Load<T>(string fileName) where T : class
    {
        var path = Path.Combine(_paths.ConfigFolder, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载随机点名数据文件 {FileName} 失败。", fileName);
            return null;
        }
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Invoke(action);
        }
    }
}
