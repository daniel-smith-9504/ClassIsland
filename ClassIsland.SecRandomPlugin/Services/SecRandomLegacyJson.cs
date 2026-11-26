using System.Globalization;
using System.Text.Json;
using ClassIsland.SecRandomPlugin.Models;

namespace ClassIsland.SecRandomPlugin.Services;

internal sealed class LegacyRosterEntry
{
    public Guid RecordId { get; init; }
    public bool Exists { get; init; } = true;
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public string Gender { get; init; } = "";
    public string Group { get; init; } = "";
    public string Tags { get; init; } = "";
}

internal sealed class LegacyHistoryEntry
{
    public Guid RecordId { get; init; }
    public string Name { get; init; } = "";
    public string Number { get; init; } = "";
    public string Group { get; init; } = "";
    public string RoundId { get; init; } = "";
    public DateTime Time { get; init; }
}

internal sealed class LegacyMemberHistory
{
    public int TotalCount { get; set; }
    public DateTime LastDrawnTime { get; set; }
    public List<LegacyHistoryEntry> Histories { get; } = [];
}

internal sealed class LegacyHistoryData
{
    public int TotalRounds { get; set; }
    public int TotalStats { get; set; }
    public Dictionary<string, LegacyMemberHistory> Members { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// SecRandom 旧版档案的宽松 JSON 解析：旧版本使用过不同的属性命名与嵌套结构，
/// 这里按「归一化属性名 + 结构启发式」读取，避免字段名或大小写差异导致整份档案为空。
/// </summary>
internal static class SecRandomLegacyJson
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly string[] RosterCollectionNames =
        ["students", "studentlist", "members", "roster", "list", "items", "records", "data"];

    private static readonly string[] HistoryCollectionNames =
        ["students", "studenthistories", "histories", "history", "members", "records", "data", "items"];

    public static List<LegacyRosterEntry> ReadRoster(string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        var entries = new List<LegacyRosterEntry>();
        var source = FindCollection(document.RootElement, RosterCollectionNames, LooksLikeStudent).ToList();
        if (source.Count == 0 &&
            document.RootElement.ValueKind == JsonValueKind.Array &&
            document.RootElement.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            source = document.RootElement.EnumerateArray().ToList();
        }

        foreach (var element in source)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    entries.Add(new LegacyRosterEntry { Name = text });
                }

                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var entry = new LegacyRosterEntry
            {
                RecordId = ParseGuid(ReadString(element, "recordid", "guid")),
                Exists = ReadBool(element, "exists", "enabled", "isenabled") ?? true,
                Name = ReadString(element, "name", "studentname", "realname"),
                Id = ReadString(element, "id", "studentid", "number", "studentnumber", "studentno"),
                Gender = ReadString(element, "gender", "sex"),
                Group = ReadString(element, "group", "groupname", "classname", "class"),
                Tags = ReadTags(element)
            };

            if (entry.Name.Length == 0 && entry.Id.Length == 0)
            {
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    public static LegacyHistoryData ReadHistory(string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        var root = document.RootElement;
        var data = new LegacyHistoryData
        {
            TotalRounds = ReadInt(root, "totalrounds", "rounds") ?? 0,
            TotalStats = ReadInt(root, "totalstats", "stats") ?? 0
        };

        foreach (var (key, value) in FindHistoryMembers(root))
        {
            if (!data.Members.TryGetValue(key, out var member))
            {
                member = new LegacyMemberHistory();
                data.Members[key] = member;
            }

            member.LastDrawnTime = ReadTime(value, "lastdrawntime", "lastdrawn", "lasttime");
            var declaredTotal = ReadInt(value, "totalcount", "count") ?? 0;

            if (value.ValueKind == JsonValueKind.Array)
            {
                AddHistoryEntries(member, value, key);
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                var histories = FindArrayProperty(value, "histories", "records", "items");
                if (histories is { } items)
                {
                    AddHistoryEntries(member, items, key);
                }
                else if (LooksLikeHistoryEntry(value))
                {
                    AddHistoryEntry(member, value, key);
                }
            }

            member.TotalCount = Math.Max(member.TotalCount, Math.Max(declaredTotal, member.Histories.Count));
            if (member.LastDrawnTime == default && member.Histories.Count > 0)
            {
                member.LastDrawnTime = member.Histories.Max(entry => entry.Time);
            }
        }

        // 顶层直接是记录数组时，按记录 ID 分组。
        if (data.Members.Count == 0 && root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var key = ReadString(element, "recordid", "memberkey", "guid", "id");
                if (!data.Members.TryGetValue(key, out var member))
                {
                    member = new LegacyMemberHistory();
                    data.Members[key] = member;
                }

                AddHistoryEntry(member, element, key);
                member.TotalCount = Math.Max(member.TotalCount, member.Histories.Count);
            }
        }

        return data;
    }

    private static void AddHistoryEntries(LegacyMemberHistory member, JsonElement array, string memberKey)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                AddHistoryEntry(member, element, memberKey);
            }
        }
    }

    private static void AddHistoryEntry(LegacyMemberHistory member, JsonElement element, string memberKey)
    {
        var entry = new LegacyHistoryEntry
        {
            RecordId = ParseGuid(ReadString(element, "recordid", "memberkey", "guid")),
            Name = ReadString(element, "recordname", "name", "studentname"),
            Number = ReadString(element, "recordnumber", "number", "studentid", "id"),
            Group = ReadString(element, "recordgroup", "group", "groupname"),
            Time = ReadTime(element, "drawtime", "time", "createdat"),
            RoundId = ReadString(element, "drawroundid", "roundid", "batchid")
        };

        if (entry.RecordId == Guid.Empty)
        {
            var fallback = entry.Name.Length > 0 || entry.Number.Length > 0
                ? ""
                : ReadString(element, "recordid", "memberkey", "guid");
            if (fallback.Length > 0)
            {
                entry = new LegacyHistoryEntry
                {
                    RecordId = ParseGuid(fallback),
                    Name = entry.Name,
                    Number = entry.Number,
                    Group = entry.Group,
                    Time = entry.Time,
                    RoundId = entry.RoundId
                };
            }
        }

        member.Histories.Add(entry);
    }

    private static IEnumerable<(string Key, JsonElement Value)> FindHistoryMembers(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray()
                .Select(element => (Key: ReadString(element, "memberkey", "recordid", "guid", "id"), Value: element))
                .Where(pair => pair.Key.Length > 0)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
        }

        var objectMap = FindObjectMap(root, HistoryCollectionNames, depth: 0);
        if (objectMap != null)
        {
            return objectMap;
        }

        var array = FindArray(root, HistoryCollectionNames, LooksLikeHistoryMember);
        if (array != null)
        {
            return array
                .Select(element => (Key: ReadString(element, "memberkey", "recordid", "guid", "id"), Value: element))
                .Where(pair => pair.Key.Length > 0)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
        }

        return [];
    }

    private static List<(string Key, JsonElement Value)>? FindObjectMap(JsonElement element, string[] names, int depth)
    {
        if (depth > 5)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(Normalize(property.Name)) && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var children = property.Value.EnumerateObject().ToList();
                    if (children.Count > 0 && children.All(child =>
                            child.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array))
                    {
                        return children.Select(child => (child.Name, child.Value)).ToList();
                    }
                }

                var nested = FindObjectMap(property.Value, names, depth + 1);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> FindCollection(JsonElement root, string[] names, Func<JsonElement, bool> looksLike)
    {
        var array = FindArray(root, names, looksLike);
        if (array != null)
        {
            return array;
        }

        var objectMap = FindObjectMap(root, names, depth: 0);
        if (objectMap != null)
        {
            return objectMap.Select(pair => pair.Value);
        }

        return [];
    }

    private static List<JsonElement>? FindArray(JsonElement element, string[] names, Func<JsonElement, bool> looksLike)
    {
        var direct = FindNamedArray(element, names, depth: 0);
        if (direct != null)
        {
            return direct;
        }

        List<JsonElement>? best = null;
        FindBestArray(element, looksLike, 0, ref best);
        return best;
    }

    private static List<JsonElement>? FindNamedArray(JsonElement element, string[] names, int depth)
    {
        if (depth > 5)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(Normalize(property.Name)) && property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.EnumerateArray().ToList();
                }

                var nested = FindNamedArray(property.Value, names, depth + 1);
                if (nested != null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindNamedArray(child, names, depth + 1);
                if (nested != null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static void FindBestArray(JsonElement element, Func<JsonElement, bool> looksLike, int depth,
        ref List<JsonElement>? best)
    {
        if (depth > 6)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToList();
            if (items.Count > 0 && items.Any(looksLike) && (best == null || items.Count > best.Count))
            {
                best = items;
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                FindBestArray(property.Value, looksLike, depth + 1, ref best);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                FindBestArray(child, looksLike, depth + 1, ref best);
            }
        }
    }

    private static bool LooksLikeStudent(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = element.EnumerateObject().Select(property => Normalize(property.Name)).ToHashSet();
        return (names.Contains("name") || names.Contains("studentname") || names.Contains("realname")) &&
               (names.Contains("id") || names.Contains("gender") || names.Contains("group") ||
                names.Contains("groupname") || names.Contains("tags") || names.Contains("recordid"));
    }

    private static bool LooksLikeHistoryMember(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.GetArrayLength() > 0;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = element.EnumerateObject().Select(property => Normalize(property.Name)).ToHashSet();
        return names.Contains("histories") || names.Contains("records") || names.Contains("totalcount") ||
               LooksLikeHistoryEntry(element);
    }

    private static bool LooksLikeHistoryEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = element.EnumerateObject().Select(property => Normalize(property.Name)).ToHashSet();
        return names.Contains("drawtime") || names.Contains("recordname") || names.Contains("recordnumber");
    }

    private static JsonElement? FindArrayProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (names.Contains(Normalize(property.Name)) && property.Value.ValueKind == JsonValueKind.Array)
            {
                return property.Value;
            }
        }

        return null;
    }

    private static JsonElement? FindProperty(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var wanted = names.Select(Normalize).ToHashSet();
        foreach (var property in element.EnumerateObject())
        {
            if (wanted.Contains(Normalize(property.Name)))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        var value = FindProperty(element, names);
        if (value == null)
        {
            return "";
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString()?.Trim() ?? "",
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static int? ReadInt(JsonElement element, params string[] names)
    {
        var value = FindProperty(element, names);
        if (value == null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number when value.Value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.Value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement element, params string[] names)
    {
        var value = FindProperty(element, names);
        if (value == null)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.Value.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.Value.GetString(), out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(value.Value.GetString(), out var number) => number != 0,
            _ => null
        };
    }

    private static string ReadTags(JsonElement element)
    {
        var value = FindProperty(element, "tags", "tag", "labels");
        if (value == null)
        {
            return "";
        }

        if (value.Value.ValueKind == JsonValueKind.Array)
        {
            return string.Join(' ', value.Value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        var raw = ReadString(element, "tags", "tag", "labels");
        return string.Join(' ', SecRandomTagHelper.Split(raw));
    }

    private static DateTime ReadTime(JsonElement element, params string[] names)
    {
        var raw = ReadString(element, names);
        if (raw.Length == 0)
        {
            return default;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
        {
            return time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
        }

        return DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var local)
            ? local
            : default;
    }

    private static Guid ParseGuid(string value) =>
        Guid.TryParse(value, out var guid) ? guid : Guid.Empty;

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
