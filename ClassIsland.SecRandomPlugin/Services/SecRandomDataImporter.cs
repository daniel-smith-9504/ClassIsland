using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using ClassIsland.SecRandomPlugin.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// SecRandom 原项目数据文件中的一个名单档案。
/// </summary>
public sealed class SecRandomImportProfile
{
    public string Name { get; init; } = "";
    public List<Student> Students { get; } = [];
    public List<DrawRecord> History { get; } = [];
    public int TotalRounds { get; init; }
    public int TotalStats { get; init; }
}

/// <summary>
/// 扫描原项目数据源后得到的档案摘要。
/// </summary>
public sealed class SecRandomProfileSummary
{
    public string Name { get; init; } = "";
    public int StudentCount { get; init; }
    public int HistoryCount { get; init; }
    public string DisplayText => $"{Name}（{StudentCount} 名学生，{HistoryCount} 条历史）";
}

public sealed class SecRandomSourceInfo
{
    public string SourcePath { get; init; } = "";
    public List<SecRandomProfileSummary> Profiles { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool HasRoster { get; init; }
    public bool HasHistory { get; init; }
}

/// <summary>
/// 直接读取 SecRandom 原项目的数据文件：<c>roll_call.srl</c> 名单库、<c>*.srhdb</c> 历史库、
/// <c>roll_call_list</c> 下的旧版档案，以及包含 <c>data</c> 目录的备份 zip。
/// 只读取不修改原文件。
/// </summary>
public sealed class SecRandomDataImporter
{
    private readonly ILogger<SecRandomDataImporter> _logger;

    public SecRandomDataImporter(ILogger<SecRandomDataImporter> logger)
    {
        _logger = logger;
    }

    public SecRandomSourceInfo Inspect(string path)
    {
        using var context = Prepare(path);
        var info = new SecRandomSourceInfo
        {
            SourcePath = Path.GetFullPath(path.Trim()),
            HasRoster = context.RosterDatabase != null || context.LegacyRosterFiles.Count > 0,
            HasHistory = context.HistoryFiles.Count > 0
        };

        var summaries = new Dictionary<string, SecRandomProfileSummary>(StringComparer.Ordinal);

        if (context.RosterDatabase != null)
        {
            foreach (var pair in ReadRosterProfiles(context.RosterDatabase, info.Warnings))
            {
                Merge(summaries, pair.Key, pair.Value, 0);
            }
        }

        foreach (var file in context.LegacyRosterFiles)
        {
            var roster = ReadLegacyRoster(file, Path.GetFileNameWithoutExtension(file), info.Warnings);
            if (roster != null)
            {
                Merge(summaries, roster.Name, roster.Students.Count, 0);
            }
        }

        foreach (var file in context.HistoryFiles)
        {
            var history = ReadHistorySummary(file, info.Warnings);
            if (history != null)
            {
                Merge(summaries, history.Value.Name, 0, history.Value.Count);
            }
        }

        info.Profiles.AddRange(summaries.Values.OrderBy(summary => summary.Name, StringComparer.Ordinal));
        return info;
    }

    public SecRandomImportProfile Import(string path, string profileName, bool includeHistory)
    {
        using var context = Prepare(path);
        var warnings = new List<string>();

        List<Student> students = [];
        if (context.RosterDatabase != null &&
            ReadRosterProfiles(context.RosterDatabase, warnings).ContainsKey(profileName))
        {
            students = LoadStudentsFromMaster(context.RosterDatabase, profileName, warnings);
        }
        else
        {
            foreach (var file in context.LegacyRosterFiles)
            {
                var roster = ReadLegacyRoster(file, Path.GetFileNameWithoutExtension(file), warnings);
                if (roster == null || !string.Equals(roster.Name, profileName, StringComparison.Ordinal))
                {
                    continue;
                }

                students = roster.Students;
                break;
            }
        }

        if (students.Count == 0)
        {
            _logger.LogWarning("SecRandom 导入：档案 {Profile} 没有可用名单。", profileName);
        }

        var totalCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var recordCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var historyRecords = new List<DrawRecord>();
        var totalRounds = 0;
        var totalStats = 0;

        var historyFile = FindHistoryFile(context.HistoryFiles, profileName, warnings);
        if (historyFile != null)
        {
            if (historyFile.EndsWith(".srhdb", StringComparison.OrdinalIgnoreCase))
            {
                ReadHistoryDatabase(historyFile, includeHistory, students, totalCounts, recordCounts,
                    historyRecords, warnings, out totalRounds, out totalStats);
            }
            else
            {
                ReadLegacyHistory(historyFile, includeHistory, students, totalCounts, recordCounts,
                    historyRecords, warnings, out totalRounds, out totalStats);
            }
        }

        // 原项目的累计次数以历史库 members.total_count 为准；把没有随历史导入的那部分折算成基线，
        // 保证公平权重在历史被裁剪或选择不导入历史时仍然正确。
        foreach (var student in students)
        {
            var key = KeyOf(student.RecordId);
            var total = totalCounts.GetValueOrDefault(key);
            var imported = includeHistory ? recordCounts.GetValueOrDefault(key) : 0;
            student.BaseDrawCount = Math.Max(0, total - imported);
        }

        var profile = new SecRandomImportProfile
        {
            Name = profileName,
            TotalRounds = totalRounds,
            TotalStats = totalStats
        };
        profile.Students.AddRange(students);
        profile.History.AddRange(historyRecords);

        foreach (var warning in warnings)
        {
            _logger.LogWarning("SecRandom 导入：{Warning}", warning);
        }

        return profile;
    }

    private static void Merge(Dictionary<string, SecRandomProfileSummary> summaries, string name, int students, int history)
    {
        if (summaries.TryGetValue(name, out var existing))
        {
            summaries[name] = new SecRandomProfileSummary
            {
                Name = name,
                StudentCount = Math.Max(existing.StudentCount, students),
                HistoryCount = Math.Max(existing.HistoryCount, history)
            };
            return;
        }

        summaries[name] = new SecRandomProfileSummary
        {
            Name = name,
            StudentCount = students,
            HistoryCount = history
        };
    }

    #region 数据源定位

    private sealed class SourceContext : IDisposable
    {
        public string Root { get; set; } = "";
        public string? RosterDatabase { get; set; }
        public List<string> LegacyRosterFiles { get; } = [];
        public List<string> HistoryFiles { get; } = [];
        public string? TemporaryDirectory { get; set; }

        public void Dispose()
        {
            if (TemporaryDirectory == null)
            {
                return;
            }

            try
            {
                Directory.Delete(TemporaryDirectory, true);
            }
            catch
            {
                // 临时目录清理失败不影响导入结果。
            }
        }
    }

    private static SourceContext Prepare(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("请提供 SecRandom 数据文件或目录路径。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (File.Exists(fullPath))
        {
            if (Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var temp = Path.Combine(Path.GetTempPath(), "SecRandomImport_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                ZipFile.ExtractToDirectory(fullPath, temp, true);
                var context = new SourceContext { Root = temp, TemporaryDirectory = temp };
                Populate(context, temp);
                return context;
            }

            var fileContext = new SourceContext { Root = Path.GetDirectoryName(fullPath) ?? fullPath };
            switch (Path.GetExtension(fullPath).ToLowerInvariant())
            {
                case ".srl":
                    fileContext.RosterDatabase = fullPath;
                    break;
                case ".srhdb":
                    fileContext.HistoryFiles.Add(fullPath);
                    break;
                case ".json":
                    AddJsonFile(fileContext, fullPath);
                    break;
                default:
                    throw new NotSupportedException($"不支持的文件类型：{Path.GetFileName(fullPath)}");
            }

            return fileContext;
        }

        if (!Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到指定的 SecRandom 数据路径。", fullPath);
        }

        var directoryContext = new SourceContext { Root = fullPath };
        Populate(directoryContext, fullPath);
        return directoryContext;
    }

    private static void AddJsonFile(SourceContext context, string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.TryGetProperty("Students", out var students) &&
                students.ValueKind == JsonValueKind.Object)
            {
                context.HistoryFiles.Add(file);
            }
            else
            {
                context.LegacyRosterFiles.Add(file);
            }
        }
        catch
        {
            context.LegacyRosterFiles.Add(file);
        }
    }

    private static void Populate(SourceContext context, string root)
    {
        var roots = new List<string> { root };
        var directory = new DirectoryInfo(root);
        if (directory.Name.Equals("list", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent != null)
        {
            roots.Add(directory.Parent.FullName);
        }

        foreach (var scanRoot in roots.Distinct(StringComparer.Ordinal))
        {
            if (!Directory.Exists(scanRoot))
            {
                continue;
            }

            if (context.RosterDatabase == null)
            {
                context.RosterDatabase = Directory
                    .EnumerateFiles(scanRoot, $"roll_call{s_rosterExtension}", SearchOption.AllDirectories)
                    .OrderByDescending(candidate => candidate.Replace('\\', '/').Contains("/data/list/"))
                    .ThenBy(candidate => candidate.Length)
                    .FirstOrDefault();
            }

            foreach (var legacyDirectory in Directory.EnumerateDirectories(scanRoot, "roll_call_list", SearchOption.AllDirectories))
            {
                foreach (var file in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.EndsWith(".migrated", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (fileName.EndsWith(s_rosterExtension, StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        AddUnique(context.LegacyRosterFiles, file);
                    }
                }
            }

            foreach (var historyDirectory in Directory.EnumerateDirectories(scanRoot, "roll_call_history", SearchOption.AllDirectories))
            {
                foreach (var file in Directory.EnumerateFiles(historyDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.EndsWith(".migrated", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (fileName.EndsWith(s_historyExtension, StringComparison.OrdinalIgnoreCase) ||
                        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        AddUnique(context.HistoryFiles, file);
                    }
                }
            }
        }
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }

    private const string s_rosterExtension = ".srl";
    private const string s_historyExtension = ".srhdb";

    #endregion

    #region 名单读取

    private Dictionary<string, int> ReadRosterProfiles(string databasePath, List<string> warnings)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            if (!HasSqliteHeader(databasePath))
            {
                warnings.Add($"名单文件不是 SQLite 数据库：{Path.GetFileName(databasePath)}");
                return result;
            }

            using var connection = OpenRead(databasePath);
            var meta = ReadMeta(connection);
            if (meta.GetValueOrDefault("format") != "SecRandomRosterDatabase/v2")
            {
                var legacyName = meta.GetValueOrDefault("profile_name") ??
                                 Path.GetFileNameWithoutExtension(databasePath);
                result[legacyName] = CountRows(connection, "members");
                return result;
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT profile_name FROM profiles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = 0;
            }

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT profile_name, COUNT(*) FROM members GROUP BY profile_name";
            using var countReader = countCommand.ExecuteReader();
            while (countReader.Read())
            {
                result[countReader.GetString(0)] = (int)countReader.GetInt64(1);
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"读取名单数据库失败：{exception.Message}");
            _logger.LogWarning(exception, "读取 SecRandom 名单数据库失败：{Path}", databasePath);
        }

        return result;
    }

    private List<Student> LoadStudentsFromMaster(string databasePath, string profileName, List<string> warnings)
    {
        var students = new List<Student>();
        try
        {
            using var connection = OpenRead(databasePath);
            var meta = ReadMeta(connection);
            using var command = connection.CreateCommand();
            if (meta.GetValueOrDefault("format") == "SecRandomRosterDatabase/v2")
            {
                command.CommandText = "SELECT record_id, [exists], id, name, gender, group_name, tags, [count] " +
                                      "FROM members WHERE profile_name = $name ORDER BY ordinal";
                command.Parameters.AddWithValue("$name", profileName);
            }
            else
            {
                command.CommandText = "SELECT record_id, [exists], id, name, gender, group_name, tags, [count] " +
                                      "FROM members ORDER BY ordinal";
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                students.Add(new Student
                {
                    RecordId = ParseGuid(reader.GetString(0)),
                    IsEnabled = reader.GetInt64(1) != 0,
                    Id = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    Group = reader.GetString(5),
                    Tag = NormalizeTags(reader.GetString(6))
                });
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"读取名单档案 {profileName} 失败：{exception.Message}");
            _logger.LogWarning(exception, "读取 SecRandom 名单档案失败：{Path} ({Profile})", databasePath, profileName);
        }

        return students;
    }

    private sealed class LegacyRoster
    {
        public string Name { get; init; } = "";
        public List<Student> Students { get; } = [];
    }

    private LegacyRoster? ReadLegacyRoster(string file, string fallbackName, List<string> warnings)
    {
        try
        {
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var entries = SecRandomLegacyJson.ReadRoster(File.ReadAllText(file));
                var jsonRoster = new LegacyRoster { Name = fallbackName };
                foreach (var entry in entries)
                {
                    jsonRoster.Students.Add(new Student
                    {
                        RecordId = entry.RecordId == Guid.Empty ? Guid.NewGuid() : entry.RecordId,
                        IsEnabled = entry.Exists,
                        Id = entry.Id,
                        Name = entry.Name,
                        Gender = entry.Gender,
                        Group = entry.Group,
                        Tag = NormalizeTags(entry.Tags)
                    });
                }

                if (jsonRoster.Students.Count == 0)
                {
                    warnings.Add($"旧版名单 {Path.GetFileName(file)} 中没有解析到学生，可能是不兼容的旧格式。");
                }

                return jsonRoster;
            }

            if (!HasSqliteHeader(file))
            {
                warnings.Add($"旧版名单文件不是 SQLite 数据库：{Path.GetFileName(file)}");
                return null;
            }

            using var connection = OpenRead(file);
            var meta = ReadMeta(connection);
            var name = meta.GetValueOrDefault("profile_name") ?? fallbackName;
            var roster = new LegacyRoster { Name = name };
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT record_id, [exists], id, name, gender, group_name, tags, [count] " +
                                  "FROM members ORDER BY ordinal";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                roster.Students.Add(new Student
                {
                    RecordId = ParseGuid(reader.GetString(0)),
                    IsEnabled = reader.GetInt64(1) != 0,
                    Id = reader.GetString(2),
                    Name = reader.GetString(3),
                    Gender = reader.GetString(4),
                    Group = reader.GetString(5),
                    Tag = NormalizeTags(reader.GetString(6))
                });
            }

            return roster;
        }
        catch (Exception exception)
        {
            warnings.Add($"读取旧版名单 {Path.GetFileName(file)} 失败：{exception.Message}");
            _logger.LogWarning(exception, "读取 SecRandom 旧版名单失败：{Path}", file);
            return null;
        }
    }

    #endregion

    #region 历史读取

    private (string Name, int Count)? ReadHistorySummary(string file, List<string> warnings)
    {
        try
        {
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var data = SecRandomLegacyJson.ReadHistory(File.ReadAllText(file));
                var count = data.Members.Values.Sum(member => member.Histories.Count);
                return (Path.GetFileNameWithoutExtension(file), count);
            }

            if (!HasSqliteHeader(file))
            {
                warnings.Add($"历史文件不是 SQLite 数据库：{Path.GetFileName(file)}");
                return null;
            }

            using var connection = OpenRead(file);
            var meta = ReadMeta(connection);
            var name = meta.GetValueOrDefault("profile_name") ?? Path.GetFileNameWithoutExtension(file);
            return (name, CountRows(connection, "records"));
        }
        catch (Exception exception)
        {
            warnings.Add($"读取历史 {Path.GetFileName(file)} 失败：{exception.Message}");
            _logger.LogWarning(exception, "读取 SecRandom 历史失败：{Path}", file);
            return null;
        }
    }

    private static string? FindHistoryFile(IEnumerable<string> files, string profileName, List<string> warnings)
    {
        foreach (var file in files)
        {
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (Path.GetFileNameWithoutExtension(file) == profileName)
                {
                    return file;
                }

                continue;
            }

            try
            {
                using var connection = OpenRead(file);
                var meta = ReadMeta(connection);
                if (string.Equals(meta.GetValueOrDefault("profile_name"), profileName, StringComparison.Ordinal))
                {
                    return file;
                }
            }
            catch (Exception exception)
            {
                warnings.Add($"读取历史文件 {Path.GetFileName(file)} 失败：{exception.Message}");
            }
        }

        return null;
    }

    private void ReadHistoryDatabase(
        string file,
        bool includeHistory,
        IReadOnlyList<Student> students,
        Dictionary<string, int> totalCounts,
        Dictionary<string, int> recordCounts,
        List<DrawRecord> records,
        List<string> warnings,
        out int totalRounds,
        out int totalStats)
    {
        totalRounds = 0;
        totalStats = 0;

        try
        {
            using var connection = OpenRead(file);
            var meta = ReadMeta(connection);
            totalRounds = ParseInt(meta.GetValueOrDefault("total_rounds"));
            totalStats = ParseInt(meta.GetValueOrDefault("total_stats"));

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT member_key, total_count FROM members";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    totalCounts[NormalizeKey(reader.GetString(0))] = (int)reader.GetInt64(1);
                }
            }

            if (!includeHistory)
            {
                return;
            }

            var names = BuildStudentNames(students);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT member_key, record_number, record_name, record_group, draw_time, draw_round_id " +
                                      "FROM records ORDER BY draw_time";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var key = NormalizeKey(reader.GetString(0));
                    var recordName = reader.GetString(2);
                    if (string.IsNullOrWhiteSpace(recordName))
                    {
                        recordName = names.GetValueOrDefault(key) ?? "";
                    }

                    recordCounts[key] = recordCounts.GetValueOrDefault(key) + 1;
                    records.Add(new DrawRecord
                    {
                        RecordId = ParseGuid(reader.GetString(0)),
                        StudentId = reader.GetString(1),
                        Name = recordName,
                        Group = reader.GetString(3),
                        Time = ParseTime(reader.GetString(4)),
                        BatchId = reader.GetString(5)
                    });
                }
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"读取历史数据库 {Path.GetFileName(file)} 失败：{exception.Message}");
            _logger.LogWarning(exception, "读取 SecRandom 历史数据库失败：{Path}", file);
        }
    }

    private void ReadLegacyHistory(
        string file,
        bool includeHistory,
        IReadOnlyList<Student> students,
        Dictionary<string, int> totalCounts,
        Dictionary<string, int> recordCounts,
        List<DrawRecord> records,
        List<string> warnings,
        out int totalRounds,
        out int totalStats)
    {
        totalRounds = 0;
        totalStats = 0;

        try
        {
            var data = SecRandomLegacyJson.ReadHistory(File.ReadAllText(file));
            totalRounds = data.TotalRounds;
            totalStats = data.TotalStats;

            var byRecordId = students
                .Where(student => student.RecordId != Guid.Empty)
                .GroupBy(student => student.RecordId)
                .ToDictionary(group => group.Key, group => group.First());
            var byName = new Dictionary<string, Student>(StringComparer.Ordinal);
            var byStudentId = new Dictionary<string, Student>(StringComparer.Ordinal);
            foreach (var student in students)
            {
                if (!string.IsNullOrWhiteSpace(student.Name))
                {
                    byName.TryAdd(student.Name.Trim(), student);
                }

                if (!string.IsNullOrWhiteSpace(student.Id))
                {
                    byStudentId.TryAdd(student.Id.Trim(), student);
                }
            }

            Student? ResolveStudent(string memberKey, LegacyMemberHistory member)
            {
                if (Guid.TryParse(memberKey, out var guid) && byRecordId.TryGetValue(guid, out var byId))
                {
                    return byId;
                }

                if (byName.TryGetValue(memberKey.Trim(), out var byMemberName))
                {
                    return byMemberName;
                }

                if (byStudentId.TryGetValue(memberKey.Trim(), out var byNumber))
                {
                    return byNumber;
                }

                foreach (var entry in member.Histories)
                {
                    if (entry.RecordId != Guid.Empty && byRecordId.TryGetValue(entry.RecordId, out var byEntryId))
                    {
                        return byEntryId;
                    }

                    if (entry.Name.Length > 0 && byName.TryGetValue(entry.Name.Trim(), out var byEntryName))
                    {
                        return byEntryName;
                    }

                    if (entry.Number.Length > 0 && byStudentId.TryGetValue(entry.Number.Trim(), out var byEntryNumber))
                    {
                        return byEntryNumber;
                    }
                }

                return null;
            }

            foreach (var (memberKey, member) in data.Members)
            {
                var student = ResolveStudent(memberKey, member);
                var recordId = student?.RecordId ?? Guid.Empty;
                var key = recordId != Guid.Empty ? KeyOf(recordId) : NormalizeKey(memberKey);
                totalCounts[key] = Math.Max(member.TotalCount, member.Histories.Count);

                foreach (var entry in member.Histories)
                {
                    recordCounts[key] = recordCounts.GetValueOrDefault(key) + 1;
                    if (!includeHistory)
                    {
                        continue;
                    }

                    var entryRecordId = entry.RecordId != Guid.Empty
                        ? entry.RecordId
                        : recordId;
                    var recordName = entry.Name;
                    if (string.IsNullOrWhiteSpace(recordName) && student != null)
                    {
                        recordName = student.Name;
                    }

                    records.Add(new DrawRecord
                    {
                        RecordId = entryRecordId,
                        StudentId = entry.Number.Length > 0 ? entry.Number : student?.Id ?? "",
                        Name = recordName,
                        Group = entry.Group.Length > 0 ? entry.Group : student?.Group ?? "",
                        Time = entry.Time == default ? member.LastDrawnTime : entry.Time,
                        BatchId = entry.RoundId
                    });
                }
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"读取旧版历史 {Path.GetFileName(file)} 失败：{exception.Message}");
            _logger.LogWarning(exception, "读取 SecRandom 旧版历史失败：{Path}", file);
        }
    }

    private static Dictionary<string, string> BuildStudentNames(IEnumerable<Student> students)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var student in students.Where(student => student.RecordId != Guid.Empty))
        {
            names[KeyOf(student.RecordId)] = student.Name;
        }

        return names;
    }

    #endregion

    #region 工具

    private static SqliteConnection OpenRead(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM meta";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                meta[reader.GetString(0)] = reader.GetString(1);
            }
        }
        catch (SqliteException)
        {
            // 旧版文件可能没有 meta 表。
        }

        return meta;
    }

    private static int CountRows(SqliteConnection connection, string table)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private static bool HasSqliteHeader(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == header.Length && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private static Guid ParseGuid(string? value) =>
        Guid.TryParse(value, out var guid) ? guid : Guid.NewGuid();

    private static string KeyOf(Guid guid) => guid.ToString("D");

    private static string NormalizeKey(string value) =>
        Guid.TryParse(value, out var guid) ? guid.ToString("D") : value;

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static DateTime ParseTime(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time
            : DateTime.MinValue;

    private static string NormalizeTags(string? tags) =>
        string.Join(' ', SecRandomTagHelper.Split(tags));

    #endregion
}
