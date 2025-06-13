namespace BarkFluff.Shared.Queue.Notifications;

public enum NotificationType
{
    Unknown = 0,
    
    ConfirmationRegistration = 1,
    
    ConfirmationOtpEmail = 2,
    
    ConfirmationAuth = 3,
    
    ResetPassword = 4,
}