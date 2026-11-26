using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.SecRandomPlugin.Models;

/// <summary>
/// 插件级抽取设置。字段以 <see cref="SecRandomConstants"/> 中的常量作为枚举值。
/// </summary>
public partial class SecRandomConfig : ObservableObject
{
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private int _drawType = SecRandomConstants.DrawTypeFair;
    [ObservableProperty] private int _repeatMode = SecRandomConstants.RepeatModeNoRepeat;
    [ObservableProperty] private int _halfRepeat = 2;
    [ObservableProperty] private bool _shieldEnabled;
    [ObservableProperty] private int _shieldMinutes = 5;
}

/// <summary>
/// 抽取相关枚举值。使用整数以便直接绑定到 ComboBox 的 SelectedIndex。
/// </summary>
public static class SecRandomConstants
{
    public const int DrawTypeRandom = 0;
    public const int DrawTypeFair = 1;

    public const int RepeatModeRepeat = 0;
    public const int RepeatModeNoRepeat = 1;
    public const int RepeatModeHalfRepeat = 2;
}
