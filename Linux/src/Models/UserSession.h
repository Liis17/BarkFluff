#pragma once

#include <QString>
#include <QDateTime>

namespace BarkFluff {

struct Token {
    QString value;
    QDateTime expirationDate;
    
    bool isValid() const {
        return !value.isEmpty() && expirationDate > QDateTime::currentDateTimeUtc();
    }
    
    bool isExpired() const {
        return expirationDate <= QDateTime::currentDateTimeUtc();
    }
};

struct UserSession {
    Token accessToken;
    Token refreshToken;
    qint64 userId = 0;
    QString username;
    QString email;
    QString firstName;
    QString lastName;
    
    bool isValid() const {
        return accessToken.isValid();
    }
};

struct CreateAccountResult {
    QString codeId;  // For confirmation
};

struct ConfirmAccountResult {
    Token refreshToken;
};

struct AuthResult {
    Token accessToken;
    Token refreshToken;
};

} // namespace BarkFluff