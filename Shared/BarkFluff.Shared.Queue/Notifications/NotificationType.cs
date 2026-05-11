namespace BarkFluff.Shared.Queue.Notifications;

public enum NotificationType
{
    Unknown = 0,

    ConfirmationRegistration = 1,

    ConfirmationOtpEmail = 2,

    ConfirmationAuth = 3,

    ResetPassword = 4,

    FailedLogin = 5,

    SuccessfulRegistration = 6,

    SuccessfulLogin = 7,

    PasswordChanged = 8,

    TwoFactorMethodChanged = 9,

    PasswordChangedByAdmin = 10,
}