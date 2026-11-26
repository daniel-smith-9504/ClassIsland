using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.SecRandomPlugin.Services;

namespace ClassIsland.SecRandomPlugin.Automation.Actions;

[ActionInfo(
    "classisland.secrandom.floatingWindow",
    "打开随机点名悬浮窗",
    "\uf323",
    addDefaultToMenu: true,
    defaultGroupToMenu: "SecRandom")]
public class SecRandomOpenFloatingWindowAction(SecRandomFloatingWindowService floatingWindows) : ActionBase
{
    protected override Task OnInvoke()
    {
        floatingWindows.Show();
        return Task.CompletedTask;
    }
}
