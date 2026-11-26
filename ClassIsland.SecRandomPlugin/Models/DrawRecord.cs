using System.Text.Json.Serialization;

namespace ClassIsland.SecRandomPlugin.Models;

/// <summary>
/// 一条抽取历史。记录抽取当时的姓名与学号快照，避免之后修改名单影响历史。
/// </summary>
public class DrawRecord
{
    public Guid RecordId { get; set; }
    public string Name { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Group { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public string BatchId { get; set; } = "";

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var id = string.IsNullOrWhiteSpace(StudentId) ? "" : $"（{StudentId}）";
            return $"{Time:MM-dd HH:mm:ss}  {Name}{id}";
        }
    }
}
