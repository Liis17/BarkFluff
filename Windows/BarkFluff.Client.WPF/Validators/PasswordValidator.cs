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
                errorMessage = "Пароль не может быть пустым";
                return false;
            }

            if (password.Length < MinLength)
            {
                errorMessage = $"Пароль должен содержать минимум {MinLength} символов";
                return false;
            }

            if (password.Contains(' '))
            {
                errorMessage = "Пароль не должен содержать пробелы";
                return false;
            }

            var strength = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(password);
            if (strength < MinStrengthScore)
            {
                errorMessage = "Пароль слишком простой. Добавьте буквы разного регистра, цифры и специальные символы";
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
                errorMessage = "Пароли не совпадают";
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
                return new ValidationResult(false, "Введите пароль", ValidationState.Empty);
            }

            if (password.Length < MinLength)
            {
                return new ValidationResult(false, $"Минимум {MinLength} символов", ValidationState.TooShort);
            }

            if (password.Contains(' '))
            {
                return new ValidationResult(false, "Пробелы недопустимы", ValidationState.InvalidCharacters);
            }

            var strength = BarkFluff.Shared.SecurityUtilities.SecurityUtilities.EvaluatePasswordStrength(password);
            if (strength < MinStrengthScore)
            {
                return new ValidationResult(false, "Пароль слишком простой", ValidationState.InvalidCharacters);
            }

            return new ValidationResult(true, "Пароль подходит", ValidationState.Valid);
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
