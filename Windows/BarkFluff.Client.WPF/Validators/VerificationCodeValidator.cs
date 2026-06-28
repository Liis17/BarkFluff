using System.Text.RegularExpressions;
using BarkFluff.Client.WPF.Services.App;

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
                errorMessage = L.Str("L_Val_VC_EnterConfirmation");
                return false;
            }

            var trimmedCode = code.Trim();

            if (trimmedCode.Length != CodeLength)
            {
                errorMessage = L.F("L_Val_VC_MustBeDigits", CodeLength);
                return false;
            }

            if (!DigitsOnlyPattern.IsMatch(trimmedCode))
            {
                errorMessage = L.Str("L_Val_VC_OnlyDigitsFull");
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
                return new ValidationResult(false, L.Str("L_Val_VC_EnterCode"), ValidationState.Empty);
            }

            var trimmedCode = code.Trim();

            // Check for non-digit characters
            if (!Regex.IsMatch(trimmedCode, @"^\d*$"))
            {
                return new ValidationResult(false, L.Str("L_Val_VC_OnlyDigits"), ValidationState.InvalidCharacters);
            }

            if (trimmedCode.Length < CodeLength)
            {
                return new ValidationResult(false, L.F("L_Val_VC_NeedMore", CodeLength - trimmedCode.Length), ValidationState.TooShort);
            }

            if (trimmedCode.Length > CodeLength)
            {
                return new ValidationResult(false, L.Str("L_Val_VC_TooMany"), ValidationState.TooLong);
            }

            return new ValidationResult(true, L.Str("L_Val_VC_Valid"), ValidationState.Valid);
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
