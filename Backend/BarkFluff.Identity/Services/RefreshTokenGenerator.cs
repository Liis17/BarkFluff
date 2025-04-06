namespace BarkFluff.Identity.Services;

public static class RefreshTokenGenerator
{
    public static string GenerateRefreshToken()
    {
        var random = new Random();
        var stringChars = new char[20];

        for (var i = 0; i < stringChars.Length; i++)
        {
            // Генерируем случайное число от 48 ('0') до 122 ('z')
            var randomChar = (char)random.Next(48, 123);

            // Повторяем генерацию, если символ не является буквой или цифрой
            while (!char.IsLetterOrDigit(randomChar))
            {
                randomChar = (char)random.Next(48, 123);
            }

            stringChars[i] = randomChar;
        }

        return new string(stringChars);
    }
}