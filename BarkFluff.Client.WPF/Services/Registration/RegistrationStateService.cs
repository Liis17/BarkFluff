using System.Security;
using System.Runtime.InteropServices;

namespace BarkFluff.Client.WPF.Services.Registration
{
    /// <summary>
    /// Service for managing registration state between steps
    /// </summary>
    public class RegistrationStateService : IDisposable
    {
        private const int MaxVerificationAttempts = 5;
        private const int VerificationCooldownMinutes = 15;

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CodeId { get; set; } = string.Empty;

        // Password stored securely - should be cleared after use
        private SecureString? _password;
        private SecureString? _confirmPassword;

        // Rate limiting for verification code attempts
        private int _verificationAttempts;
        private DateTime? _lastVerificationAttempt;
        private DateTime? _verificationCooldownEnd;

        public int CurrentStep { get; set; }
        public int TotalSteps { get; } = 9;

        /// <summary>
        /// Gets the current step number (1-based for display)
        /// </summary>
        public int DisplayStep => CurrentStep + 1;

        /// <summary>
        /// Gets the step progress as percentage
        /// </summary>
        public double ProgressPercentage => ((double)CurrentStep / TotalSteps) * 100;

        /// <summary>
        /// Sets the password securely
        /// </summary>
        public void SetPassword(string password)
        {
            ClearPassword();
            _password = new SecureString();
            foreach (char c in password)
            {
                _password.AppendChar(c);
            }
            _password.MakeReadOnly();
        }

        /// <summary>
        /// Sets the confirm password securely
        /// </summary>
        public void SetConfirmPassword(string confirmPassword)
        {
            ClearConfirmPassword();
            _confirmPassword = new SecureString();
            foreach (char c in confirmPassword)
            {
                _confirmPassword.AppendChar(c);
            }
            _confirmPassword.MakeReadOnly();
        }

        /// <summary>
        /// Gets the password as string.
        /// WARNING: The returned string is a managed object and cannot be securely erased from memory.
        /// Use this method only when necessary (e.g., for API calls) and avoid storing the result.
        /// The password is retrieved from SecureString and the intermediate BSTR is properly zeroed.
        /// </summary>
        /// <returns>The password as a string, or empty if not set</returns>
        public string GetPassword()
        {
            if (_password == null) return string.Empty;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(_password);
                return Marshal.PtrToStringBSTR(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(ptr);
            }
        }

        /// <summary>
        /// Gets the confirm password as string.
        /// WARNING: The returned string is a managed object and cannot be securely erased from memory.
        /// Use this method only when necessary (e.g., for API calls) and avoid storing the result.
        /// The password is retrieved from SecureString and the intermediate BSTR is properly zeroed.
        /// </summary>
        /// <returns>The confirm password as a string, or empty if not set</returns>
        public string GetConfirmPassword()
        {
            if (_confirmPassword == null) return string.Empty;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToBSTR(_confirmPassword);
                return Marshal.PtrToStringBSTR(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.ZeroFreeBSTR(ptr);
            }
        }

        /// <summary>
        /// Clears the password from memory
        /// </summary>
        public void ClearPassword()
        {
            _password?.Dispose();
            _password = null;
        }

        /// <summary>
        /// Clears the confirm password from memory
        /// </summary>
        public void ClearConfirmPassword()
        {
            _confirmPassword?.Dispose();
            _confirmPassword = null;
        }

        /// <summary>
        /// Clears all passwords from memory
        /// </summary>
        public void ClearAllPasswords()
        {
            ClearPassword();
            ClearConfirmPassword();
        }

        /// <summary>
        /// Checks if verification attempts are allowed (rate limiting)
        /// </summary>
        public bool CanAttemptVerification(out string errorMessage)
        {
            errorMessage = string.Empty;

            // Check cooldown
            if (_verificationCooldownEnd.HasValue && DateTime.UtcNow < _verificationCooldownEnd.Value)
            {
                var remaining = _verificationCooldownEnd.Value - DateTime.UtcNow;
                errorMessage = $"Подождите {(int)remaining.TotalMinutes} мин. {remaining.Seconds} сек. перед следующей попыткой";
                return false;
            }

            // Reset attempts after cooldown
            if (_verificationCooldownEnd.HasValue && DateTime.UtcNow >= _verificationCooldownEnd.Value)
            {
                _verificationAttempts = 0;
                _verificationCooldownEnd = null;
            }

            return true;
        }

        /// <summary>
        /// Records a verification attempt
        /// </summary>
        public void RecordVerificationAttempt(bool success)
        {
            _lastVerificationAttempt = DateTime.UtcNow;

            if (success)
            {
                _verificationAttempts = 0;
                _verificationCooldownEnd = null;
            }
            else
            {
                _verificationAttempts++;
                if (_verificationAttempts >= MaxVerificationAttempts)
                {
                    _verificationCooldownEnd = DateTime.UtcNow.AddMinutes(VerificationCooldownMinutes);
                }
            }
        }

        /// <summary>
        /// Gets remaining verification attempts
        /// </summary>
        public int RemainingVerificationAttempts => Math.Max(0, MaxVerificationAttempts - _verificationAttempts);

        /// <summary>
        /// Resets all state
        /// </summary>
        public void Reset()
        {
            FirstName = string.Empty;
            LastName = string.Empty;
            Username = string.Empty;
            Email = string.Empty;
            CodeId = string.Empty;
            ClearAllPasswords();
            CurrentStep = 0;
            _verificationAttempts = 0;
            _lastVerificationAttempt = null;
            _verificationCooldownEnd = null;
        }

        public void Dispose()
        {
            ClearAllPasswords();
            GC.SuppressFinalize(this);
        }

        ~RegistrationStateService()
        {
            ClearAllPasswords();
        }
    }
}
