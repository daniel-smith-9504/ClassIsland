using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Automation.Triggers;

[TriggerInfo("classisland.secrandom.drawCompleted", "随机点名完成", "\uf323")]
public class SecRandomDrawCompletedTrigger(SecRandomService service) : TriggerBase
{
    public override void Loaded()
    {
        service.DrawCompleted += OnDrawCompleted;
    }

    public override void UnLoaded()
    {
        service.DrawCompleted -= OnDrawCompleted;
    }

    private void OnDrawCompleted(object? sender, DrawCompletedEventArgs e) => Trigger();
}
