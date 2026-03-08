namespace BarkFluff.Shared.SecurityUtilities
{
    public class SecurityUtilities
    {
        public static int EvaluatePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0;

            int score = 0;

            if (password.Length >= 16)
                score += 30;
            else if (password.Length >= 12)
                score += 20;
            else if (password.Length >= 8)
                score += 10;
            else
                score += 0;

            bool hasLower = password.Any(char.IsLower);
            bool hasUpper = password.Any(char.IsUpper);
            if (hasLower && hasUpper)
                score += 20;
            else if (hasLower || hasUpper)
                score += 10;

            int digitCount = password.Count(char.IsDigit);
            if (digitCount >= 3)
                score += 20;
            else if (digitCount > 0)
                score += 10;

            int specialCount = password.Count(c => !char.IsLetterOrDigit(c));
            if (specialCount >= 2)
                score += 20;
            else if (specialCount == 1)
                score += 10;

            double uniquenessRatio = password.Distinct().Count() / (double)password.Length;
            if (uniquenessRatio > 0.7)
                score += 10;
            else if (uniquenessRatio > 0.4)
                score += 5;

            return Math.Min(score, 100);
        }
        public static (string message, string colorHex) GetPasswordStrengthMessage(int score)
        {
            if (score < 0) score = 0;
            if (score > 100) score = 100;

            if (score < 20)
                return ("Пароль слишком простой", "#FF4C4C");
            if (score < 40)
                return ("Пароль всё ещё слишком лёгкий", "#FF8000");
            if (score < 60)
                return ("Пароль средней сложности", "#FFD700");
            if (score < 80)
                return ("Пароль достаточно надёжный", "#7FFF00");

            return ("Надёжный пароль", "#00CC66");
        }
    }
}
