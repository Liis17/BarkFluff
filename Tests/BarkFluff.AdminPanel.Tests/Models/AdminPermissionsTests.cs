using Barkfluff.AdminPanel.Models;

using Xunit;

namespace Barkfluff.AdminPanel.Tests.Models;

public class AdminPermissionsTests
{
    public static TheoryData<string, AdminRole[], bool> MatrixCases => new()
    {
        { AdminPermissions.UsersRead, new[] { AdminRole.Support, AdminRole.SecurityAdmin }, true },
        { AdminPermissions.UsersSessionsRevoke, new[] { AdminRole.Support, AdminRole.SecurityAdmin }, true },
        { AdminPermissions.UsersPasswordSet, new[] { AdminRole.Support, AdminRole.SecurityAdmin }, true },
        { AdminPermissions.Users2FaDisable, new[] { AdminRole.SecurityAdmin }, true },
        { AdminPermissions.BadgesManage, new[] { AdminRole.ContentAdmin }, true },
        { AdminPermissions.StickersManage, new[] { AdminRole.ContentAdmin }, true },
        { AdminPermissions.BotsManage, new[] { AdminRole.ContentAdmin }, true },
        { AdminPermissions.ReservedNamesManage, new[] { AdminRole.ContentAdmin }, true },
        { AdminPermissions.S3Browse, new[] { AdminRole.ContentAdmin }, true },
        { AdminPermissions.NotificationsManage, new[] { AdminRole.ContentAdmin }, true },
        { AdminPermissions.DockerControl, new[] { AdminRole.OperationsAdmin }, true },
        { AdminPermissions.DockerDeploy, new[] { AdminRole.OperationsAdmin }, true },
        { AdminPermissions.RemoteServers, new[] { AdminRole.OperationsAdmin }, true },
        { AdminPermissions.RemoteConsole, new[] { AdminRole.OperationsAdmin }, true },
        { AdminPermissions.ConfigRead, new[] { AdminRole.OperationsAdmin, AdminRole.SecurityAdmin }, true },
        { AdminPermissions.ConfigWrite, new[] { AdminRole.OperationsAdmin }, true },
        { AdminPermissions.FederationManage, new[] { AdminRole.SecurityAdmin }, true },
        { AdminPermissions.SeqDelete, new[] { AdminRole.OperationsAdmin, AdminRole.SecurityAdmin }, true },
        { AdminPermissions.MailManage, new[] { AdminRole.Support, AdminRole.SecurityAdmin }, true },
        { AdminPermissions.AdminsRoles, new[] { AdminRole.SecurityAdmin }, true },
        { AdminPermissions.AuditRead, new[] { AdminRole.SecurityAdmin }, true }
    };

    [Theory, MemberData(nameof(MatrixCases))]
    public void IsAllowed_MatchesApprovedMatrix(string permission, AdminRole[] allowedRoles, bool expected)
    {
        var allowed = AdminPermissions.IsAllowed(permission, allowedRoles.ToHashSet());

        Assert.Equal(expected, allowed);

        var otherRoles = AdminRoles.ActiveRoles.Except(allowedRoles).ToHashSet();
        if (otherRoles.Count > 0)
            Assert.False(AdminPermissions.IsAllowed(permission, otherRoles));
    }

    [Fact]
    public void IsAllowed_EmptyRoles_AlwaysDenied()
    {
        foreach (var permission in MatrixCases.Select(m => (string)m[0]).Distinct())
            Assert.False(AdminPermissions.IsAllowed(permission, new HashSet<AdminRole>()));
    }

    [Fact]
    public void IsAllowed_UnknownPermission_Denied()
    {
        Assert.False(AdminPermissions.IsAllowed("does.not.exist", AdminRoles.ActiveRoles.ToHashSet()));
    }
}
