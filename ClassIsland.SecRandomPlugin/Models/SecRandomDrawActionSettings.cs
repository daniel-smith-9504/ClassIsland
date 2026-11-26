using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.SecRandomPlugin.Models;

/// <summary>
/// 自动化「随机点名」行动的设置。
/// </summary>
public partial class SecRandomDrawActionSettings : ObservableObject
{
    /// <summary>
    /// 本次抽取的人数。为 0 时使用插件设置中的人数。
    /// </summary>
    [ObservableProperty] private int _drawCount;

    [ObservableProperty] private bool _showNotification = true;
}
