using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Navigator.Admin;

public sealed class AdminCredentials
{
    private readonly byte[] _username;
    private readonly byte[] _password;

    public AdminCredentials(IConfiguration configuration)
    {
        var username = configuration["NavigatorAdmin:Username"];
        var password = configuration["NavigatorAdmin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Navigator admin credentials are required. Set NavigatorAdmin__Username and NavigatorAdmin__Password.");
        }

        _username = Encoding.UTF8.GetBytes(username);
        _password = Encoding.UTF8.GetBytes(password);
    }

    public bool IsValid(string? username, string? password)
    {
        if (username == null || password == null)
            return false;

        return CryptographicOperations.FixedTimeEquals(_username, Encoding.UTF8.GetBytes(username))
               & CryptographicOperations.FixedTimeEquals(_password, Encoding.UTF8.GetBytes(password));
    }
}
