using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.SecRandomPlugin.Automation.Actions;
using ClassIsland.SecRandomPlugin.Automation.Triggers;
using ClassIsland.SecRandomPlugin.Controls.Components;
using ClassIsland.SecRandomPlugin.NotificationProviders;
using ClassIsland.SecRandomPlugin.Services;
using ClassIsland.SecRandomPlugin.Views.SettingsPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.SecRandomPlugin;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton(new SecRandomPaths(PluginConfigFolder));
        services.AddSingleton<SecRandomService>();

        services.AddSettingsPage<SecRandomSettingsPage>();
        services.AddComponent<SecRandomComponent, SecRandomComponentSettingsControl>();
        services.AddAction<SecRandomDrawAction, SecRandomDrawActionSettingsControl>();
        services.AddTrigger<SecRandomDrawCompletedTrigger>();
        services.AddNotificationProvider<SecRandomNotificationProvider>();
    }
}
