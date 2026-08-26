namespace BarkFluff.Identity.Security;

public enum IdentityAbuseOperation
{
    Auth,
    CreateAccount,
    ConfirmAccount,
    ResetPassword,
    ConfirmResetPassword,
    EnableOtpVerification,
    ConfirmOtpVerification,
    DisableOtpVerification
}

public enum IdentityCodeKind
{
    Registration,
    PasswordReset
}

public enum IdentityOtpOperation
{
    Setup,
    Disable
}
