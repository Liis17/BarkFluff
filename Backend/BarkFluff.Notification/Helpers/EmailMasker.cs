namespace BarkFluff.Notification.Helpers;

public static class EmailMasker
{
    public static string Mask(string email)
    {
        if (string.IsNullOrEmpty(email))
            return "***";

        var at = email.IndexOf('@');
        return at > 0 ? $"***@{email[(at + 1)..]}" : "***";
    }
}
