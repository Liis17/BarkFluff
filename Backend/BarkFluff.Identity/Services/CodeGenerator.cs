using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.Identity.Services;

public static class CodeGenerator
{
    public static string GenerateDigitalCode(int length)
    {
        if (length <= 0)
            throw new ArgumentException("Длина должна быть положительным числом.", nameof(length));

        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(RandomNumberGenerator.GetInt32(0, 10));
        }
        return sb.ToString();
    }
}
