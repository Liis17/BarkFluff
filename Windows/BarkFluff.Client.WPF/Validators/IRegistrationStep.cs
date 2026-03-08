namespace BarkFluff.Client.WPF.Validators
{
    /// <summary>
    /// Interface for registration step validation
    /// </summary>
    public interface IRegistrationStep
    {
        /// <summary>
        /// Validates the step input
        /// </summary>
        /// <param name="errorMessage">Error message if validation fails</param>
        /// <returns>True if validation passes, false otherwise</returns>
        bool Validate(out string errorMessage);

        /// <summary>
        /// Gets the step number (1-9)
        /// </summary>
        int StepNumber { get; }

        /// <summary>
        /// Gets the step title
        /// </summary>
        string StepTitle { get; }
    }
}
