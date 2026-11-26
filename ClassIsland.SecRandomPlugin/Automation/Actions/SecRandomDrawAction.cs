using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.SecRandomPlugin.Models;
using ClassIsland.SecRandomPlugin.NotificationProviders;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Automation.Actions;

[ActionInfo(
    "classisland.secrandom.draw",
    "SecRandom 随机点名",
    "\uf323",
    addDefaultToMenu: true,
    defaultGroupToMenu: "SecRandom")]
public class SecRandomDrawAction(
    SecRandomService service,
    SecRandomNotificationProvider notificationProvider) : ActionBase<SecRandomDrawActionSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var outcome = service.Draw(Settings.DrawCount > 0 ? Settings.DrawCount : null);
        if (!outcome.IsSuccess || !Settings.ShowNotification)
        {
            return;
        }

        await notificationProvider.ShowResultAsync(outcome.Selected);
    }
}
