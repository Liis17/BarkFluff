using System.Text;

namespace BarkFluff.Identity.Services;

public static class CodeGenerator
{
    private static readonly Random _random = new();
    private static readonly object _syncLock = new();

    public static string GenerateDigitalCode(int length)
    {
        if (length <= 0)
            throw new ArgumentException("Длина должна быть положительным числом.", nameof(length));

        var codeBuilder = new StringBuilder(length);
        lock (_syncLock)
        {
            for (int i = 0; i < length; i++)
            {
                codeBuilder.Append(_random.Next(0, 10));
            }
        }
        return codeBuilder.ToString();
    }
}