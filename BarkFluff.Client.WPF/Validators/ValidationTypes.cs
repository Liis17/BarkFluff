namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Represents the state of validation
    /// </summary>
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

    /// <summary>
    /// Result of a validation operation
    /// </summary>
    public record ValidationResult(bool IsValid, string Message, ValidationState State);
}
