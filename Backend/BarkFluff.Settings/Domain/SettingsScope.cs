using BarkFluff.Shared.Identity;

namespace BarkFluff.Settings.Domain;

public sealed record SettingsScope(ServiceId ServiceId, string EntityName, string TableName);

public static class SettingsScopes
{
    public static IReadOnlyList<SettingsScope> All { get; } =
    [
        new(ServiceId.Unknown, "GlobalSettings", "GlobalSettings"),
        new(ServiceId.Identity, "IdentitySettings", "IdentitySettings"),
        new(ServiceId.Users, "UsersSettings", "UsersSettings"),
        new(ServiceId.Beacon, "BeaconSettings", "BeaconSettings"),
        new(ServiceId.Notifications, "NotificationsSettings", "NotificationsSettings"),
        new(ServiceId.Files, "FilesSettings", "FilesSettings"),
        new(ServiceId.Messages, "MessagesSettings", "MessagesSettings"),
        new(ServiceId.FastAuth, "FastAuthSettings", "FastAuthSettings"),
        new(ServiceId.Updates, "UpdatesSettings", "UpdatesSettings"),
        new(ServiceId.Onliner, "OnlinerSettings", "OnlinerSettings"),
        new(ServiceId.CloudMessaging, "CloudMessagingSettings", "CloudMessagingSettings"),
        new(ServiceId.Web, "WebSettings", "WebSettings"),
        new(ServiceId.Developers, "DevelopersSettings", "DevelopersSettings"),
        new(ServiceId.Calls, "CallsSettings", "CallsSettings"),
        new(ServiceId.Bots, "BotsSettings", "BotsSettings"),
        new(ServiceId.Federation, "FederationSettings", "FederationSettings")
    ];

    public static SettingsScope Get(ServiceId serviceId)
    {
        return All.FirstOrDefault(scope => scope.ServiceId == serviceId)
            ?? throw new ArgumentOutOfRangeException(nameof(serviceId), serviceId, "Unknown settings scope.");
    }

    public static bool TryGet(ServiceId serviceId, out SettingsScope scope)
    {
        scope = All.FirstOrDefault(item => item.ServiceId == serviceId)!;
        return scope is not null;
    }

    public static bool TryGet(string tableName, out SettingsScope scope)
    {
        scope = All.FirstOrDefault(item => string.Equals(item.TableName, tableName, StringComparison.Ordinal))!;
        return scope is not null;
    }
}
