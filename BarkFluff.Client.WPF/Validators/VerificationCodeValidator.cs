using System.Text.RegularExpressions;

namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Validator for verification codes (6-digit codes)
    /// </summary>
    public static class VerificationCodeValidator
    {
        public const int CodeLength = 6;
        private static readonly Regex DigitsOnlyPattern = new Regex(@"^\d{6}$", RegexOptions.Compiled);

        /// <summary>
        /// Validates the verification code format (must be exactly 6 digits)
        /// </summary>
        public static bool Validate(string? code, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(code))
            {
                errorMessage = "Введите код подтверждения";
                return false;
            }

            var trimmedCode = code.Trim();

            if (trimmedCode.Length != CodeLength)
            {
                errorMessage = $"Код должен содержать ровно {CodeLength} цифр";
                return false;
            }

            if (!DigitsOnlyPattern.IsMatch(trimmedCode))
            {
                errorMessage = "Код должен содержать только цифры";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates code in real-time (for visual feedback during typing)
        /// </summary>
        public static ValidationResult ValidateRealTime(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new ValidationResult(false, "Введите 6-значный код", ValidationState.Empty);
            }

            var trimmedCode = code.Trim();

            // Check for non-digit characters
            if (!Regex.IsMatch(trimmedCode, @"^\d*$"))
            {
                return new ValidationResult(false, "Только цифры", ValidationState.InvalidCharacters);
            }

            if (trimmedCode.Length < CodeLength)
            {
                return new ValidationResult(false, $"Введите еще {CodeLength - trimmedCode.Length} цифр", ValidationState.TooShort);
            }

            if (trimmedCode.Length > CodeLength)
            {
                return new ValidationResult(false, "Слишком много цифр", ValidationState.TooLong);
            }

            return new ValidationResult(true, "Код введен", ValidationState.Valid);
        }

        /// <summary>
        /// Gets normalized code (trimmed)
        /// </summary>
        public static string Normalize(string? code)
        {
            return code?.Trim() ?? string.Empty;
        }
    }
}
