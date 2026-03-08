using System.Text.RegularExpressions;

namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Validator for email addresses
    /// </summary>
    public static class EmailValidator
    {
        public const int MaxLength = 254; // RFC 5321 max length

        // More strict email regex pattern
        private static readonly Regex EmailPattern = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Validates the email address
        /// </summary>
        public static bool Validate(string? email, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(email))
            {
                errorMessage = "Email не может быть пустым";
                return false;
            }

            var trimmedEmail = email.Trim();

            if (trimmedEmail.Length > MaxLength)
            {
                errorMessage = $"Email слишком длинный (максимум {MaxLength} символов)";
                return false;
            }

            if (!EmailPattern.IsMatch(trimmedEmail))
            {
                errorMessage = "Введите корректный адрес электронной почты";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates email in real-time (for visual feedback during typing)
        /// </summary>
        public static ValidationResult ValidateRealTime(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new ValidationResult(false, "Введите email", ValidationState.Empty);
            }

            var trimmedEmail = email.Trim();

            if (trimmedEmail.Length > MaxLength)
            {
                return new ValidationResult(false, "Email слишком длинный", ValidationState.TooLong);
            }

            if (!trimmedEmail.Contains('@'))
            {
                return new ValidationResult(false, "Требуется символ @", ValidationState.InvalidCharacters);
            }

            if (!EmailPattern.IsMatch(trimmedEmail))
            {
                return new ValidationResult(false, "Некорректный формат email", ValidationState.InvalidCharacters);
            }

            return new ValidationResult(true, "Email корректен", ValidationState.Valid);
        }

        /// <summary>
        /// Gets normalized (lowercase, trimmed) email
        /// </summary>
        public static string Normalize(string? email)
        {
            return email?.Trim().ToLowerInvariant() ?? string.Empty;
        }
    }
}
