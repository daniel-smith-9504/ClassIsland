namespace ClassIsland.SecRandomPlugin.Services;

/// <summary>
/// 保存插件数据文件所在的目录。
/// </summary>
public sealed class SecRandomPaths(string configFolder)
{
    public string ConfigFolder { get; } = configFolder;
}
