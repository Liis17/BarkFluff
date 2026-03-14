namespace BarkFluff.Users.Services;

public class ReservedUsernamesService
{
    private readonly HashSet<string> _reservedUsernames;

    public ReservedUsernamesService(IConfiguration configuration)
    {
        var reservedNamesValue = configuration["ReservedNames:Usernames"] ?? "";
        _reservedUsernames = reservedNamesValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.ToLower())
            .ToHashSet();
    }

    public bool IsReserved(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return false;

        return _reservedUsernames.Contains(username.Trim().ToLower());
    }
}
