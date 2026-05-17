using System.Text.RegularExpressions;
using BarkFluff.Client.WPF.Services.App;

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
                errorMessage = L.Str("L_Val_User_Empty");
                return false;
            }

            if (username.Length < MinLength || username.Length > MaxLength)
            {
                errorMessage = L.F("L_Val_User_LengthFull", MinLength, MaxLength);
                return false;
            }

            if (StartsWithInvalidCharPattern.IsMatch(username))
            {
                errorMessage = L.Str("L_Val_User_BadStartFull");
                return false;
            }

            if (username.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                errorMessage = L.Str("L_Val_User_NoBotFull");
                return false;
            }

            if (!ValidUsernamePattern.IsMatch(username))
            {
                errorMessage = L.Str("L_Val_User_BadCharsFull");
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
                return new ValidationResult(false, L.Str("L_Val_User_Enter"), ValidationState.Empty);
            }

            if (username.Length < MinLength)
            {
                return new ValidationResult(false, L.F("L_Val_User_Min", MinLength), ValidationState.TooShort);
            }

            if (username.Length > MaxLength)
            {
                return new ValidationResult(false, L.F("L_Val_User_Max", MaxLength), ValidationState.TooLong);
            }

            if (StartsWithInvalidCharPattern.IsMatch(username))
            {
                return new ValidationResult(false, L.Str("L_Val_User_BadStart"), ValidationState.InvalidStart);
            }

            if (username.IndexOf("bot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ValidationResult(false, L.Str("L_Val_User_NoBot"), ValidationState.ContainsBotWord);
            }

            if (!ValidUsernamePattern.IsMatch(username))
            {
                return new ValidationResult(false, L.Str("L_Val_User_BadChars"), ValidationState.InvalidCharacters);
            }

            return new ValidationResult(true, L.Str("L_Val_User_Valid"), ValidationState.Valid);
        }
    }
}
