#include "Validators.h"
#include <QRegularExpression>

namespace BarkFluff {

//=== PersonalInfoValidator ===

ValidationResult PersonalInfoValidator::validateFirstName(const QString& firstName) {
    QString trimmed = firstName.trimmed();
    
    if (trimmed.isEmpty()) {
        return ValidationResult::invalid(QObject::tr("First name cannot be empty"));
    }
    
    if (trimmed.length() < MinFirstNameLength) {
        return ValidationResult::invalid(QObject::tr("First name must be at least %1 characters").arg(MinFirstNameLength));
    }
    
    if (trimmed.length() > MaxNameLength) {
        return ValidationResult::invalid(QObject::tr("First name must not exceed %1 characters").arg(MaxNameLength));
    }
    
    return ValidationResult::valid();
}

ValidationResult PersonalInfoValidator::validateLastName(const QString& lastName) {
    QString trimmed = lastName.trimmed();
    
    // Last name is optional
    if (trimmed.isEmpty()) {
        return ValidationResult::valid();
    }
    
    if (trimmed.length() > MaxNameLength) {
        return ValidationResult::invalid(QObject::tr("Last name must not exceed %1 characters").arg(MaxNameLength));
    }
    
    return ValidationResult::valid();
}

//=== UsernameValidator ===

ValidationResult UsernameValidator::validate(const QString& username) {
    QString trimmed = username.trimmed();
    
    if (trimmed.isEmpty()) {
        return ValidationResult::invalid(QObject::tr("Username cannot be empty"));
    }
    
    if (trimmed.length() < MinLength) {
        return ValidationResult::invalid(QObject::tr("Username must be at least %1 characters").arg(MinLength));
    }
    
    if (trimmed.length() > MaxLength) {
        return ValidationResult::invalid(QObject::tr("Username must not exceed %1 characters").arg(MaxLength));
    }
    
    // Check first character - cannot start with digit or underscore
    QRegularExpression invalidStartPattern("^[0-9_]");
    if (invalidStartPattern.match(trimmed).hasMatch()) {
        return ValidationResult::invalid(QObject::tr("Username cannot start with a digit or underscore"));
    }
    
    // Check for "bot" (case insensitive)
    if (trimmed.toLower().contains("bot")) {
        return ValidationResult::invalid(QObject::tr("Username cannot contain 'bot'"));
    }
    
    // Check valid characters: a-zA-Z0-9_
    QRegularExpression validPattern("^[a-zA-Z0-9_]+$");
    if (!validPattern.match(trimmed).hasMatch()) {
        return ValidationResult::invalid(QObject::tr("Username can only contain letters, digits and underscores"));
    }
    
    return ValidationResult::valid();
}

QString UsernameValidator::normalize(const QString& username) {
    return username.trimmed().toLower();
}

bool UsernameValidator::hasValidFormat(const QString& username) {
    return validate(username).isValid;
}

//=== EmailValidator ===

ValidationResult EmailValidator::validate(const QString& email) {
    QString trimmed = email.trimmed();
    
    if (trimmed.isEmpty()) {
        return ValidationResult::invalid(QObject::tr("Email cannot be empty"));
    }
    
    if (trimmed.length() > MaxLength) {
        return ValidationResult::invalid(QObject::tr("Email is too long"));
    }
    
    // Basic email pattern
    QRegularExpression emailPattern("^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$");
    if (!emailPattern.match(trimmed).hasMatch()) {
        return ValidationResult::invalid(QObject::tr("Invalid email format"));
    }
    
    return ValidationResult::valid();
}

QString EmailValidator::normalize(const QString& email) {
    return email.trimmed().toLower();
}

//=== PasswordValidator ===

ValidationResult PasswordValidator::validate(const QString& password) {
    if (password.isEmpty()) {
        return ValidationResult::invalid(QObject::tr("Password cannot be empty"));
    }
    
    if (password.length() < MinLength) {
        return ValidationResult::invalid(QObject::tr("Password must be at least %1 characters").arg(MinLength));
    }
    
    if (password.contains(' ')) {
        return ValidationResult::invalid(QObject::tr("Password cannot contain spaces"));
    }
    
    int score = calculateStrength(password);
    if (score < MinStrengthScore) {
        return ValidationResult::invalid(QObject::tr("Password is too weak. Add uppercase/lowercase letters, digits and special characters"));
    }
    
    return ValidationResult::valid();
}

ValidationResult PasswordValidator::validateMatch(const QString& password, const QString& confirmPassword) {
    if (password != confirmPassword) {
        return ValidationResult::invalid(QObject::tr("Passwords do not match"));
    }
    return ValidationResult::valid();
}

PasswordRequirements PasswordValidator::getRequirements(const QString& password) {
    PasswordRequirements req;
    
    req.hasMinLength = password.length() >= MinLength;
    req.hasUpperCase = password.contains(QRegularExpression("[A-Z]"));
    req.hasLowerCase = password.contains(QRegularExpression("[a-z]"));
    req.hasDigit = password.contains(QRegularExpression("[0-9]"));
    req.hasSpecialChar = password.contains(QRegularExpression("[^a-zA-Z0-9\\s]"));
    req.hasNoSpaces = !password.contains(' ');
    req.strengthScore = calculateStrength(password);
    
    return req;
}

int PasswordValidator::calculateStrength(const QString& password) {
    int score = 0;
    
    // Length bonuses
    if (password.length() >= 8) score += 20;
    if (password.length() >= 12) score += 10;
    if (password.length() >= 16) score += 10;
    
    // Character variety bonuses
    if (password.contains(QRegularExpression("[A-Z]"))) score += 15;
    if (password.contains(QRegularExpression("[a-z]"))) score += 15;
    if (password.contains(QRegularExpression("[0-9]"))) score += 15;
    if (password.contains(QRegularExpression("[^a-zA-Z0-9\\s]"))) score += 15;
    
    return qMin(100, score);
}

StrengthLevel PasswordValidator::getStrengthLevel(int score) {
    if (score < 30) {
        return {QObject::tr("Very Weak"), QColor("#f44336"), score};
    } else if (score < 60) {
        return {QObject::tr("Weak"), QColor("#ff9800"), score};
    } else if (score < 80) {
        return {QObject::tr("Medium"), QColor("#ffeb3b"), score};
    } else if (score < 100) {
        return {QObject::tr("Good"), QColor("#8bc34a"), score};
    } else {
        return {QObject::tr("Excellent"), QColor("#4caf50"), score};
    }
}

//=== VerificationCodeValidator ===

ValidationResult VerificationCodeValidator::validate(const QString& code) {
    QString trimmed = code.trimmed();
    
    if (trimmed.isEmpty()) {
        return ValidationResult::invalid(QObject::tr("Please enter verification code"));
    }
    
    if (trimmed.length() != CodeLength) {
        return ValidationResult::invalid(QObject::tr("Code must be %1 digits").arg(CodeLength));
    }
    
    if (!isNumeric(trimmed)) {
        return ValidationResult::invalid(QObject::tr("Code must contain only digits"));
    }
    
    return ValidationResult::valid();
}

bool VerificationCodeValidator::isNumeric(const QString& code) {
    return code.contains(QRegularExpression("^[0-9]+$"));
}

} // namespace BarkFluff