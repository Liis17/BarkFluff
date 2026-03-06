#pragma once

#include <QString>
#include <QByteArray>
#include <QDateTime>

namespace BarkFluff {

// Registration step enum
enum class RegistrationStep {
    PersonalInfo = 1,
    Username = 2,
    Email = 3,
    ConfirmEmail = 4,
    Password = 5,
    Avatar = 6,
    Bio = 7,
    TwoFA = 8,
    Completion = 9
};

// Get step title
inline QString stepTitle(RegistrationStep step) {
    switch (step) {
        case RegistrationStep::PersonalInfo: return QObject::tr("Personal Information");
        case RegistrationStep::Username: return QObject::tr("Username");
        case RegistrationStep::Email: return QObject::tr("Email Address");
        case RegistrationStep::ConfirmEmail: return QObject::tr("Confirm Email");
        case RegistrationStep::Password: return QObject::tr("Create Password");
        case RegistrationStep::Avatar: return QObject::tr("Profile Picture");
        case RegistrationStep::Bio: return QObject::tr("About You");
        case RegistrationStep::TwoFA: return QObject::tr("Two-Factor Authentication");
        case RegistrationStep::Completion: return QObject::tr("Registration Complete");
        default: return "";
    }
}

// Get next step
inline RegistrationStep nextStep(RegistrationStep current) {
    int next = static_cast<int>(current) + 1;
    if (next > static_cast<int>(RegistrationStep::Completion)) {
        return RegistrationStep::Completion;
    }
    return static_cast<RegistrationStep>(next);
}

// Get previous step
inline RegistrationStep previousStep(RegistrationStep current) {
    int prev = static_cast<int>(current) - 1;
    if (prev < static_cast<int>(RegistrationStep::PersonalInfo)) {
        return RegistrationStep::PersonalInfo;
    }
    return static_cast<RegistrationStep>(prev);
}

// Check if step is skippable
inline bool isStepSkippable(RegistrationStep step) {
    return step == RegistrationStep::Avatar || 
           step == RegistrationStep::Bio || 
           step == RegistrationStep::TwoFA;
}

// Registration state - holds all data between steps
struct RegistrationState {
    // User data
    QString firstName;
    QString lastName;
    QString username;
    QString email;
    QString codeId;            // From CreateAccount
    QString refreshToken;      // From ConfirmAccount
    QString accessToken;       // From CreateToken
    QString password;          // User password (cleared after use)
    QString bio;
    QByteArray avatarData;
    QString avatarFileId;
    
    // 2FA data
    QString otpQrCode;         // Base64 QR code
    QString otpSecretCode;     // Manual code
    
    // Current state
    RegistrationStep currentStep = RegistrationStep::PersonalInfo;
    bool isLoading = false;
    QString errorMessage;
    
    // Rate limiting for verification codes
    static constexpr int MaxVerificationAttempts = 5;
    static constexpr int CooldownMinutes = 15;
    
    int verificationAttempts = 0;
    QDateTime cooldownEndTime;
    
    // Check if can attempt verification
    bool canAttemptVerification() const {
        if (!cooldownEndTime.isValid()) return true;
        return QDateTime::currentDateTime() >= cooldownEndTime;
    }
    
    // Get remaining attempts
    int remainingAttempts() const {
        return qMax(0, MaxVerificationAttempts - verificationAttempts);
    }
    
    // Get cooldown remaining in seconds
    int cooldownRemainingSeconds() const {
        if (!cooldownEndTime.isValid()) return 0;
        qint64 remaining = QDateTime::currentDateTime().secsTo(cooldownEndTime);
        return qMax(0, static_cast<int>(remaining));
    }
    
    // Record verification attempt
    void recordVerificationAttempt(bool success) {
        if (success) {
            verificationAttempts = 0;
            cooldownEndTime = QDateTime();
        } else {
            verificationAttempts++;
            if (verificationAttempts >= MaxVerificationAttempts) {
                cooldownEndTime = QDateTime::currentDateTime().addSecs(CooldownMinutes * 60);
            }
        }
    }
    
    // Progress
    static constexpr int TotalSteps = 9;
    
    int stepNumber() const {
        return static_cast<int>(currentStep);
    }
    
    double progressPercentage() const {
        return (static_cast<double>(stepNumber()) / TotalSteps) * 100.0;
    }
    
    QString stepIndicator() const {
        return QObject::tr("Step %1 of %2").arg(stepNumber()).arg(TotalSteps);
    }
    
    // Reset state
    void reset() {
        firstName.clear();
        lastName.clear();
        username.clear();
        email.clear();
        codeId.clear();
        refreshToken.clear();
        accessToken.clear();
        password.clear();
        bio.clear();
        avatarData.clear();
        avatarFileId.clear();
        otpQrCode.clear();
        otpSecretCode.clear();
        currentStep = RegistrationStep::PersonalInfo;
        isLoading = false;
        errorMessage.clear();
        verificationAttempts = 0;
        cooldownEndTime = QDateTime();
    }
};

} // namespace BarkFluff