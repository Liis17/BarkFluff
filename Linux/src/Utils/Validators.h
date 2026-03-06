#pragma once

#include <QString>
#include <QColor>

namespace BarkFluff {

// Result of validation
struct ValidationResult {
    bool isValid;
    QString errorMessage;
    
    static ValidationResult valid() { return {true, ""}; }
    static ValidationResult invalid(const QString& message) { return {false, message}; }
    
    operator bool() const { return isValid; }
};

// Password strength requirements
struct PasswordRequirements {
    bool hasMinLength = false;      // >= 8 chars
    bool hasUpperCase = false;       // A-Z
    bool hasLowerCase = false;       // a-z
    bool hasDigit = false;           // 0-9
    bool hasSpecialChar = false;     // !@#$%^&* etc
    bool hasNoSpaces = true;         // no spaces
    int strengthScore = 0;           // 0-100
    
    bool isValid() const { return hasMinLength && hasNoSpaces && strengthScore >= 60; }
};

// Strength level for display
struct StrengthLevel {
    QString message;
    QColor color;
    int percentage;
};

class PersonalInfoValidator {
public:
    static constexpr int MinFirstNameLength = 3;
    static constexpr int MaxNameLength = 40;
    
    static ValidationResult validateFirstName(const QString& firstName);
    static ValidationResult validateLastName(const QString& lastName);
};

class UsernameValidator {
public:
    static constexpr int MinLength = 3;
    static constexpr int MaxLength = 30;
    
    static ValidationResult validate(const QString& username);
    static QString normalize(const QString& username);
    static bool hasValidFormat(const QString& username);
};

class EmailValidator {
public:
    static constexpr int MaxLength = 254;
    
    static ValidationResult validate(const QString& email);
    static QString normalize(const QString& email);
};

class PasswordValidator {
public:
    static constexpr int MinLength = 8;
    static constexpr int MinStrengthScore = 60;
    
    static ValidationResult validate(const QString& password);
    static ValidationResult validateMatch(const QString& password, const QString& confirmPassword);
    static PasswordRequirements getRequirements(const QString& password);
    static int calculateStrength(const QString& password);
    static StrengthLevel getStrengthLevel(int score);
};

class VerificationCodeValidator {
public:
    static constexpr int CodeLength = 6;
    
    static ValidationResult validate(const QString& code);
    static bool isNumeric(const QString& code);
};

} // namespace BarkFluff