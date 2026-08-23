namespace Barkfluff.AdminPanel.Models;

public enum StepUpStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3,
    Used = 4
}

public class PendingStepUp
{
    public string ConfirmationId { get; set; } = Guid.NewGuid().ToString("N");
    public string ActionKey { get; set; } = string.Empty;
    public string Params { get; set; } = string.Empty;
    public Guid TokenId { get; set; }
    public long TargetTelegramUserId { get; set; }
    public string? SessionName { get; set; }
    public string? IpAddress { get; set; }
    public StepUpStatus Status { get; set; } = StepUpStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int? TelegramMessageId { get; set; }
}

/// <summary>
/// Catalog of step-up protected actions: stable keys and human-readable titles for Telegram cards.
/// </summary>
public static class StepUpActions
{
    public const string UsersPasswordSet = "users.password.set";
    public const string Users2FaDisable = "users.2fa.disable";
    public const string UsersSessionsRevokeAll = "users.sessions.revoke-all";
    public const string DockerBranch = "docker.branch";
    public const string DockerRestartAll = "docker.restart-all";
    public const string DockerUpdateAll = "docker.update-all";
    public const string DockerAdminPanelRestart = "docker.admin-panel.restart";
    public const string DockerAdminPanelUpdate = "docker.admin-panel.update";
    public const string RemoteServerSave = "remote.server.save";
    public const string RemoteServerDelete = "remote.server.delete";
    public const string RemoteConsole = "remote.console";
    public const string ConfigUpdate = "config.update";
    public const string S3ConfigUpdate = "config.s3.update";
    public const string FederationKeysRotate = "federation.keys.rotate";
    public const string FederationPeerAdd = "federation.peer.add";
    public const string FederationPeerBlock = "federation.peer.block";
    public const string SeqClear = "seq.clear";
    public const string AdminsRolesUpdate = "admins.roles.update";

    private static readonly IReadOnlyDictionary<string, string> PermissionByAction =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UsersPasswordSet] = AdminPermissions.UsersPasswordSet,
            [Users2FaDisable] = AdminPermissions.Users2FaDisable,
            [UsersSessionsRevokeAll] = AdminPermissions.UsersSessionsRevoke,
            [DockerBranch] = AdminPermissions.DockerDeploy,
            [DockerRestartAll] = AdminPermissions.DockerDeploy,
            [DockerUpdateAll] = AdminPermissions.DockerDeploy,
            [DockerAdminPanelRestart] = AdminPermissions.DockerDeploy,
            [DockerAdminPanelUpdate] = AdminPermissions.DockerDeploy,
            [RemoteServerSave] = AdminPermissions.RemoteServers,
            [RemoteServerDelete] = AdminPermissions.RemoteServers,
            [RemoteConsole] = AdminPermissions.RemoteConsole,
            [ConfigUpdate] = AdminPermissions.ConfigWrite,
            [S3ConfigUpdate] = AdminPermissions.ConfigWrite,
            [FederationKeysRotate] = AdminPermissions.FederationManage,
            [FederationPeerAdd] = AdminPermissions.FederationManage,
            [FederationPeerBlock] = AdminPermissions.FederationManage,
            [SeqClear] = AdminPermissions.SeqDelete,
            [AdminsRolesUpdate] = AdminPermissions.AdminsRoles
        };

    public static bool TryGetPermission(string actionKey, out string permission)
    {
        return PermissionByAction.TryGetValue(actionKey, out permission!);
    }

    public static string Title(string actionKey)
    {
        return actionKey switch
        {
            UsersPasswordSet => "Смена пароля пользователя",
            Users2FaDisable => "Отключение 2FA пользователя",
            UsersSessionsRevokeAll => "Завершение всех сессий пользователя",
            DockerBranch => "Переключение ветки обновлений",
            DockerRestartAll => "Перезапуск всех сервисов",
            DockerUpdateAll => "Обновление всех сервисов",
            DockerAdminPanelRestart => "Перезапуск админ-панели",
            DockerAdminPanelUpdate => "Обновление админ-панели",
            RemoteServerSave => "Сохранение SSH-сервера",
            RemoteServerDelete => "Удаление SSH-сервера",
            RemoteConsole => "Открытие SSH-консоли",
            ConfigUpdate => "Изменение конфигурации",
            S3ConfigUpdate => "Изменение S3-конфигурации",
            FederationKeysRotate => "Ротация ключей федерации",
            FederationPeerAdd => "Добавление пира федерации",
            FederationPeerBlock => "Блокировка пира федерации",
            SeqClear => "Очистка логов Seq",
            AdminsRolesUpdate => "Изменение ролей администратора",
            _ => actionKey
        };
    }

    public static string AuditDetails(string actionKey, string? parameters)
    {
        var title = Title(actionKey);
        var reason = GetParameter(parameters, "reason");
        return string.IsNullOrWhiteSpace(reason)
            ? title
            : $"{title}. Причина: {reason.Trim()}";
    }

    private static string? GetParameter(string? parameters, string name)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        var marker = $"{name}=";
        var start = parameters.StartsWith(marker, StringComparison.Ordinal)
            ? 0
            : parameters.IndexOf($";{marker}", StringComparison.Ordinal) + 1;
        if (start < 0 || start >= parameters.Length ||
            !parameters.AsSpan(start).StartsWith(marker, StringComparison.Ordinal))
        {
            return null;
        }

        return parameters[(start + marker.Length)..];
    }
}
