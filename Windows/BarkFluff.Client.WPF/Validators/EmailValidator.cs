using System.Text.RegularExpressions;
using BarkFluff.Client.WPF.Services.App;

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
                errorMessage = L.Str("L_Val_Email_Empty");
                return false;
            }

            var trimmedEmail = email.Trim();

            if (trimmedEmail.Length > MaxLength)
            {
                errorMessage = L.F("L_Val_Email_TooLongFull", MaxLength);
                return false;
            }

            if (!EmailPattern.IsMatch(trimmedEmail))
            {
                errorMessage = L.Str("L_Val_Email_Invalid");
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
                return new ValidationResult(false, L.Str("L_Val_Email_Enter"), ValidationState.Empty);
            }

            var trimmedEmail = email.Trim();

            if (trimmedEmail.Length > MaxLength)
            {
                return new ValidationResult(false, L.Str("L_Val_Email_TooLong"), ValidationState.TooLong);
            }

            if (!trimmedEmail.Contains('@'))
            {
                return new ValidationResult(false, L.Str("L_Val_Email_NeedAt"), ValidationState.InvalidCharacters);
            }

            if (!EmailPattern.IsMatch(trimmedEmail))
            {
                return new ValidationResult(false, L.Str("L_Val_Email_BadFormat"), ValidationState.InvalidCharacters);
            }

            return new ValidationResult(true, L.Str("L_Val_Email_Valid"), ValidationState.Valid);
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
