using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.SecRandomPlugin.Models;

/// <summary>
/// 主界面「随机点名」组件的设置。
/// </summary>
public partial class SecRandomComponentSettings : ObservableObject
{
    [ObservableProperty] private string _header = "随机点名";
    [ObservableProperty] private bool _showHeader = true;
    [ObservableProperty] private string _idleText = "点击抽取";
}
