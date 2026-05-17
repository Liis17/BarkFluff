using BarkFluff.Client.WPF.Services.App;

namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Validator for password with requirements checking
    /// </summary>
    public static class PasswordValidator
    {
        public const int MinLength = 8;
        public const int MinStrengthScore = 60;

        /// <summary>
        /// Validates the password
        /// </summary>
        public static bool Validate(string? password, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(password))
            {
                errorMessage = L.Str("L_Val_Pwd_Empty");
                return false;
            }

            if (password.Length < MinLength)
            {
                errorMessage = L.F("L_Val_Pwd_MinFull", MinLength);
                return false;
            }

            if (password.Contains(' '))
            {
                errorMessage = L.Str("L_Val_Pwd_NoSpaces");
                return false;
            }

            var strength = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(password);
            if (strength < MinStrengthScore)
            {
                errorMessage = L.Str("L_Val_Pwd_TooSimpleFull");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that passwords match
        /// </summary>
        public static bool ValidateMatch(string? password, string? confirmPassword, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (password != confirmPassword)
            {
                errorMessage = L.Str("L_Val_Pwd_Mismatch");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets password requirements status for real-time display
        /// </summary>
        public static PasswordRequirements GetRequirementsStatus(string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new PasswordRequirements(
                    HasMinLength: false,
                    HasUpperCase: false,
                    HasLowerCase: false,
                    HasDigit: false,
                    HasSpecialChar: false,
                    HasNoSpaces: true,
                    StrengthScore: 0
                );
            }

            return new PasswordRequirements(
                HasMinLength: password.Length >= MinLength,
                HasUpperCase: password.Any(char.IsUpper),
                HasLowerCase: password.Any(char.IsLower),
                HasDigit: password.Any(char.IsDigit),
                HasSpecialChar: password.Any(c => !char.IsLetterOrDigit(c) && c != ' '),
                HasNoSpaces: !password.Contains(' '),
                StrengthScore: BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(password)
            );
        }

        /// <summary>
        /// Validates password in real-time
        /// </summary>
        public static ValidationResult ValidateRealTime(string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new ValidationResult(false, L.Str("L_Val_Pwd_Enter"), ValidationState.Empty);
            }

            if (password.Length < MinLength)
            {
                return new ValidationResult(false, L.F("L_Val_Pwd_Min", MinLength), ValidationState.TooShort);
            }

            if (password.Contains(' '))
            {
                return new ValidationResult(false, L.Str("L_Val_Pwd_Spaces"), ValidationState.InvalidCharacters);
            }

            var strength = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(password);
            if (strength < MinStrengthScore)
            {
                return new ValidationResult(false, L.Str("L_Val_Pwd_TooSimple"), ValidationState.InvalidCharacters);
            }

            return new ValidationResult(true, L.Str("L_Val_Pwd_Valid"), ValidationState.Valid);
        }
    }

    public record PasswordRequirements(
        bool HasMinLength,
        bool HasUpperCase,
        bool HasLowerCase,
        bool HasDigit,
        bool HasSpecialChar,
        bool HasNoSpaces,
        int StrengthScore
    )
    {
        public bool IsValid => HasMinLength && HasNoSpaces && StrengthScore >= PasswordValidator.MinStrengthScore;
    }
}
