using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.SecRandomPlugin.Models;

namespace ClassIsland.SecRandomPlugin.NotificationProviders;

[NotificationProviderInfo(
    "2F6B7C8D-1A3E-4B5C-9D0E-7F8A9B0C1D2E",
    "SecRandom 随机点名",
    "\uf323",
    "显示随机点名的抽取结果。")]
public class SecRandomNotificationProvider : NotificationProviderBase
{
    public async Task ShowResultAsync(IReadOnlyList<Student> students, TimeSpan? duration = null)
    {
        var text = string.Join("、", students.Select(student => student.Name));
        var request = new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask("随机点名", hasRightIcon: false),
            OverlayContent = NotificationContent.CreateSimpleTextContent(
                text,
                content => content.Duration = duration ?? TimeSpan.FromSeconds(10))
        };
        await ShowNotificationAsync(request);
    }
}
