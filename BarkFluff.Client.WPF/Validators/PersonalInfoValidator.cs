using System.Text.RegularExpressions;

namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Validator for personal information step (FirstName, LastName)
    /// </summary>
    public static class PersonalInfoValidator
    {
        public const int MinFirstNameLength = 3;
        public const int MaxNameLength = 40;

        /// <summary>
        /// Validates the first name
        /// </summary>
        public static bool ValidateFirstName(string? firstName, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(firstName))
            {
                errorMessage = "Имя не может быть пустым";
                return false;
            }

            var trimmedName = firstName.Trim();

            if (trimmedName.Length < MinFirstNameLength)
            {
                errorMessage = $"Имя должно содержать минимум {MinFirstNameLength} символа";
                return false;
            }

            if (trimmedName.Length > MaxNameLength)
            {
                errorMessage = $"Имя не должно превышать {MaxNameLength} символов";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates the last name (optional)
        /// </summary>
        public static bool ValidateLastName(string? lastName, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(lastName))
            {
                return true; // LastName is optional
            }

            if (lastName.Length > MaxNameLength)
            {
                errorMessage = $"Фамилия не должна превышать {MaxNameLength} символов";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets trimmed first name
        /// </summary>
        public static string GetTrimmedFirstName(string? firstName)
        {
            return firstName?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Gets trimmed last name
        /// </summary>
        public static string GetTrimmedLastName(string? lastName)
        {
            return lastName?.Trim() ?? string.Empty;
        }
    }
}
