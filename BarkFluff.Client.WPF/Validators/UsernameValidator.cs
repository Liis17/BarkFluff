using System.Text.RegularExpressions;

namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Validator for username (login)
    /// </summary>
    public static class UsernameValidator
    {
        public const int MinLength = 3;
        public const int MaxLength = 30;

        private static readonly Regex ValidUsernamePattern = new Regex(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);
        private static readonly Regex StartsWithInvalidCharPattern = new Regex(@"^[0-9_-]", RegexOptions.Compiled);

        /// <summary>
        /// Validates the username
        /// </summary>
        public static bool Validate(string? username, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                errorMessage = "Логин не может быть пустым";
                return false;
            }

            if (username.Length < MinLength || username.Length > MaxLength)
            {
                errorMessage = $"Логин должен быть от {MinLength} до {MaxLength} символов";
                return false;
            }

            if (StartsWithInvalidCharPattern.IsMatch(username))
            {
                errorMessage = "Логин не может начинаться с цифры, тире или нижнего подчеркивания";
                return false;
            }

            if (username.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                errorMessage = "Логин не должен содержать 'bot'";
                return false;
            }

            if (!ValidUsernamePattern.IsMatch(username))
            {
                errorMessage = "Логин содержит недопустимые символы. Разрешены: a-z, A-Z, 0-9, _, -";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates username in real-time (for visual feedback during typing)
        /// Returns detailed validation state
        /// </summary>
        public static ValidationResult ValidateRealTime(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return new ValidationResult(false, "Введите логин", ValidationState.Empty);
            }

            if (username.Length < MinLength)
            {
                return new ValidationResult(false, $"Минимум {MinLength} символа", ValidationState.TooShort);
            }

            if (username.Length > MaxLength)
            {
                return new ValidationResult(false, $"Максимум {MaxLength} символов", ValidationState.TooLong);
            }

            if (StartsWithInvalidCharPattern.IsMatch(username))
            {
                return new ValidationResult(false, "Нельзя начинать с цифры, - или _", ValidationState.InvalidStart);
            }

            if (username.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ValidationResult(false, "Нельзя использовать 'bot'", ValidationState.ContainsBotWord);
            }

            if (!ValidUsernamePattern.IsMatch(username))
            {
                return new ValidationResult(false, "Недопустимые символы", ValidationState.InvalidCharacters);
            }

            return new ValidationResult(true, "Логин доступен для проверки", ValidationState.Valid);
        }
    }

    public enum ValidationState
    {
        Empty,
        TooShort,
        TooLong,
        InvalidStart,
        ContainsBotWord,
        InvalidCharacters,
        Valid
    }

    public record ValidationResult(bool IsValid, string Message, ValidationState State);
}
