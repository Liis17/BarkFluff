namespace Barkfluff.AdminPanel.Models;

/// <summary>
/// Permission keys and the role matrix.
/// An empty allowed-roles array means the baseline: any authenticated admin (Viewer).
/// </summary>
public static class AdminPermissions
{
    public const string UsersRead = "users.read";
    public const string UsersPasswordSet = "users.password.set";
    public const string Users2FaDisable = "users.2fa.disable";
    public const string BadgesManage = "badges.manage";
    public const string StickersManage = "stickers.manage";
    public const string BotsManage = "bots.manage";
    public const string ReservedNamesManage = "reserved-names.manage";
    public const string S3Browse = "s3.browse";
    public const string NotificationsManage = "notifications.manage";
    public const string DockerControl = "docker.control";
    public const string DockerDeploy = "docker.deploy";
    public const string RemoteServers = "remote.servers";
    public const string RemoteConsole = "remote.console";
    public const string ConfigRead = "config.read";
    public const string ConfigWrite = "config.write";
    public const string FederationManage = "federation.manage";
    public const string SeqDelete = "seq.delete";
    public const string MailManage = "mail.manage";
    public const string AdminsRoles = "admins.roles";
    public const string AuditRead = "audit.read";

    private static readonly Dictionary<string, AdminRole[]> Matrix = new()
    {
        [UsersRead] = new[] { AdminRole.Support, AdminRole.SecurityAdmin },
        [UsersPasswordSet] = new[] { AdminRole.Support, AdminRole.SecurityAdmin },
        [Users2FaDisable] = new[] { AdminRole.SecurityAdmin },
        [BadgesManage] = new[] { AdminRole.ContentAdmin },
        [StickersManage] = new[] { AdminRole.ContentAdmin },
        [BotsManage] = new[] { AdminRole.ContentAdmin },
        [ReservedNamesManage] = new[] { AdminRole.ContentAdmin },
        [S3Browse] = new[] { AdminRole.ContentAdmin },
        [NotificationsManage] = new[] { AdminRole.ContentAdmin },
        [DockerControl] = new[] { AdminRole.OperationsAdmin },
        [DockerDeploy] = new[] { AdminRole.OperationsAdmin },
        [RemoteServers] = new[] { AdminRole.OperationsAdmin },
        [RemoteConsole] = new[] { AdminRole.OperationsAdmin },
        [ConfigRead] = new[] { AdminRole.OperationsAdmin, AdminRole.SecurityAdmin },
        [ConfigWrite] = new[] { AdminRole.OperationsAdmin },
        [FederationManage] = new[] { AdminRole.SecurityAdmin },
        [SeqDelete] = new[] { AdminRole.OperationsAdmin, AdminRole.SecurityAdmin },
        [MailManage] = new[] { AdminRole.Support, AdminRole.SecurityAdmin },
        [AdminsRoles] = new[] { AdminRole.SecurityAdmin },
        [AuditRead] = new[] { AdminRole.SecurityAdmin }
    };

    public static bool IsAllowed(string permission, HashSet<AdminRole> roles)
    {
        if (!Matrix.TryGetValue(permission, out var allowed))
            return false;

        if (allowed.Length == 0)
            return true;

        return allowed.Any(roles.Contains);
    }
}
