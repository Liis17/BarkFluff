#pragma once

#include <QString>
#include <QByteArray>
#include <QDateTime>

namespace BarkFluff {

class SecureStorage {
public:
    SecureStorage();
    ~SecureStorage();
    
    // Initialize with PIN (creates new or decrypts existing)
    bool initialize(const QString& pin);
    
    // Token management
    void saveTokens(const QString& accessToken, const QString& refreshToken,
                    const QDateTime& accessExpiry, const QDateTime& refreshExpiry);
    
    // PIN length management (must be called before saveTokens)
    void setPinLength(int length);
    int getStoredPinLength() const;
    
    QString getAccessToken() const;
    QString getRefreshToken() const;
    QDateTime getAccessExpiry() const;
    QDateTime getRefreshExpiry() const;
    
    // Check if stored data exists
    bool hasStoredData() const;
    bool isInitialized() const { return initialized_; }
    
    // Change PIN
    bool changePin(const QString& oldPin, const QString& newPin);
    
    // Clear all data (logout)
    void clear();
    
    // Get last error
    QString lastError() const { return lastError_; }

private:
    QByteArray encrypt(const QByteArray& data, const QByteArray& key, QByteArray& iv);
    QByteArray decrypt(const QByteArray& data, const QByteArray& key, const QByteArray& iv);
    QByteArray deriveKey(const QString& pin, const QByteArray& salt);
    bool saveToFile(const QByteArray& data);
    QByteArray loadFromFile();
    
    QString filePath_;
    QByteArray decryptedData_;
    QString currentPin_;
    int pinLength_ = 4;
    bool initialized_ = false;
    QString lastError_;
};

} // namespace BarkFluff