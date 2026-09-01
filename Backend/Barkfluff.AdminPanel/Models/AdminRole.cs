namespace Barkfluff.AdminPanel.Models;

public enum AdminRole
{
    Viewer = 0,
    Support = 1,
    ContentAdmin = 2,
    OperationsAdmin = 3,
    SecurityAdmin = 4,
    Owner = 5
}

public static class AdminRoles
{
    public static readonly AdminRole[] ActiveRoles =
    {
        AdminRole.Support,
        AdminRole.ContentAdmin,
        AdminRole.OperationsAdmin,
        AdminRole.SecurityAdmin
    };

    public static List<string> ToNames(IEnumerable<AdminRole> roles)
    {
        return roles
            .Where(r => r != AdminRole.Viewer && Enum.IsDefined(r))
            .Distinct()
            .Select(r => r.ToString())
            .ToList();
    }

    public static HashSet<AdminRole> ParseNames(IEnumerable<string>? names)
    {
        var result = new HashSet<AdminRole>();
        if (names == null)
            return result;

        foreach (var name in names)
        {
            if (Enum.TryParse<AdminRole>(name, true, out var role) && role != AdminRole.Viewer)
                result.Add(role);
        }

        return result;
    }

    public static string DisplayName(AdminRole role)
    {
        return role switch
        {
            AdminRole.Viewer => "Viewer",
            AdminRole.Support => "Support",
            AdminRole.ContentAdmin => "ContentAdmin",
            AdminRole.OperationsAdmin => "OperationsAdmin",
            AdminRole.SecurityAdmin => "SecurityAdmin",
            AdminRole.Owner => "Owner",
            _ => role.ToString()
        };
    }
}
