#include "SecureStorage.h"
#include <QFile>
#include <QDir>
#include <QFileInfo>
#include <QJsonDocument>
#include <QJsonObject>
#include <QStandardPaths>
#include <QCryptographicHash>
#include <openssl/evp.h>
#include <openssl/rand.h>

namespace BarkFluff {

SecureStorage::SecureStorage() {
    QString configPath = QStandardPaths::writableLocation(QStandardPaths::ConfigLocation);
    filePath_ = configPath + "/BarkFluff/secure.dat";
}

SecureStorage::~SecureStorage() {
    // Clear sensitive data from memory
    decryptedData_.fill(0);
}

bool SecureStorage::initialize(const QString& pin) {
    currentPin_ = pin;
    
    if (!hasStoredData()) {
        // No existing data, just store PIN for later
        initialized_ = true;
        return true;
    }
    
    QByteArray encrypted = loadFromFile();
    if (encrypted.size() < 32) {
        lastError_ = "Invalid secure storage file";
        return false;
    }
    
    // Extract salt, iv, and ciphertext
    QByteArray salt = encrypted.left(16);
    QByteArray iv = encrypted.mid(16, 16);
    QByteArray ciphertext = encrypted.mid(32);
    
    QByteArray key = deriveKey(pin, salt);
    decryptedData_ = decrypt(ciphertext, key, iv);
    
    if (decryptedData_.isEmpty()) {
        lastError_ = "Invalid PIN or corrupted data";
        return false;
    }
    
    initialized_ = true;
    return true;
}

QByteArray SecureStorage::deriveKey(const QString& pin, const QByteArray& salt) {
    QByteArray key(32, 0);
    
    PKCS5_PBKDF2_HMAC(
        pin.toUtf8().constData(),
        pin.length(),
        reinterpret_cast<const unsigned char*>(salt.constData()),
        salt.size(),
        100000,
        EVP_sha256(),
        32,
        reinterpret_cast<unsigned char*>(key.data())
    );
    
    return key;
}

QByteArray SecureStorage::encrypt(const QByteArray& data, const QByteArray& key, QByteArray& iv) {
    EVP_CIPHER_CTX* ctx = EVP_CIPHER_CTX_new();
    if (!ctx) return QByteArray();
    
    // Generate random IV
    iv.resize(16);
    RAND_bytes(reinterpret_cast<unsigned char*>(iv.data()), 16);
    
    QByteArray result(data.size() + 16, 0);
    int len = 0;
    int ciphertextLen = 0;
    
    EVP_EncryptInit_ex(ctx, EVP_aes_256_cbc(), nullptr,
        reinterpret_cast<const unsigned char*>(key.constData()),
        reinterpret_cast<const unsigned char*>(iv.constData()));
    
    EVP_EncryptUpdate(ctx,
        reinterpret_cast<unsigned char*>(result.data()), &len,
        reinterpret_cast<const unsigned char*>(data.constData()), data.size());
    ciphertextLen = len;
    
    EVP_EncryptFinal_ex(ctx,
        reinterpret_cast<unsigned char*>(result.data()) + len, &len);
    ciphertextLen += len;
    
    result.resize(ciphertextLen);
    EVP_CIPHER_CTX_free(ctx);
    
    return result;
}

QByteArray SecureStorage::decrypt(const QByteArray& data, const QByteArray& key, const QByteArray& iv) {
    EVP_CIPHER_CTX* ctx = EVP_CIPHER_CTX_new();
    if (!ctx) return QByteArray();
    
    QByteArray result(data.size() + 16, 0);
    int len = 0;
    int plaintextLen = 0;
    
    EVP_DecryptInit_ex(ctx, EVP_aes_256_cbc(), nullptr,
        reinterpret_cast<const unsigned char*>(key.constData()),
        reinterpret_cast<const unsigned char*>(iv.constData()));
    
    if (EVP_DecryptUpdate(ctx,
        reinterpret_cast<unsigned char*>(result.data()), &len,
        reinterpret_cast<const unsigned char*>(data.constData()), data.size()) != 1) {
        EVP_CIPHER_CTX_free(ctx);
        return QByteArray();
    }
    plaintextLen = len;
    
    if (EVP_DecryptFinal_ex(ctx,
        reinterpret_cast<unsigned char*>(result.data()) + len, &len) != 1) {
        EVP_CIPHER_CTX_free(ctx);
        return QByteArray();
    }
    plaintextLen += len;
    
    result.resize(plaintextLen);
    EVP_CIPHER_CTX_free(ctx);
    
    return result;
}

bool SecureStorage::saveToFile(const QByteArray& data) {
    QDir().mkpath(QFileInfo(filePath_).absolutePath());
    
    QFile file(filePath_);
    if (!file.open(QIODevice::WriteOnly)) {
        lastError_ = "Cannot open file for writing";
        return false;
    }
    
    file.write(data);
    file.close();
    return true;
}

QByteArray SecureStorage::loadFromFile() {
    QFile file(filePath_);
    if (!file.open(QIODevice::ReadOnly)) {
        return QByteArray();
    }
    
    QByteArray data = file.readAll();
    file.close();
    return data;
}

void SecureStorage::setPinLength(int length) {
    pinLength_ = length;
}

int SecureStorage::getStoredPinLength() const {
    if (decryptedData_.isEmpty()) return 4;  // Default
    
    QJsonDocument doc = QJsonDocument::fromJson(decryptedData_);
    int length = doc.object().value("pin_length").toInt(4);
    return length > 0 ? length : 4;
}

void SecureStorage::saveTokens(const QString& accessToken, const QString& refreshToken,
                                const QDateTime& accessExpiry, const QDateTime& refreshExpiry) {
    QJsonObject json;
    json["access_token"] = accessToken;
    json["refresh_token"] = refreshToken;
    json["access_expiry"] = accessExpiry.toUTC().toString(Qt::ISODate);
    json["refresh_expiry"] = refreshExpiry.toUTC().toString(Qt::ISODate);
    json["pin_length"] = pinLength_;
    
    decryptedData_ = QJsonDocument(json).toJson(QJsonDocument::Compact);
    
    // Generate salt
    QByteArray salt(16, 0);
    RAND_bytes(reinterpret_cast<unsigned char*>(salt.data()), 16);
    
    QByteArray key = deriveKey(currentPin_, salt);
    QByteArray iv;
    QByteArray encrypted = encrypt(decryptedData_, key, iv);
    
    // Save: salt + iv + ciphertext
    QByteArray fileData = salt + iv + encrypted;
    saveToFile(fileData);
}

QString SecureStorage::getAccessToken() const {
    if (decryptedData_.isEmpty()) return QString();
    
    QJsonDocument doc = QJsonDocument::fromJson(decryptedData_);
    return doc.object().value("access_token").toString();
}

QString SecureStorage::getRefreshToken() const {
    if (decryptedData_.isEmpty()) return QString();
    
    QJsonDocument doc = QJsonDocument::fromJson(decryptedData_);
    return doc.object().value("refresh_token").toString();
}

QDateTime SecureStorage::getAccessExpiry() const {
    if (decryptedData_.isEmpty()) return QDateTime();
    
    QJsonDocument doc = QJsonDocument::fromJson(decryptedData_);
    return QDateTime::fromString(
        doc.object().value("access_expiry").toString(), Qt::ISODate);
}

QDateTime SecureStorage::getRefreshExpiry() const {
    if (decryptedData_.isEmpty()) return QDateTime();
    
    QJsonDocument doc = QJsonDocument::fromJson(decryptedData_);
    return QDateTime::fromString(
        doc.object().value("refresh_expiry").toString(), Qt::ISODate);
}

bool SecureStorage::hasStoredData() const {
    return QFile::exists(filePath_);
}

bool SecureStorage::changePin(const QString& oldPin, const QString& newPin) {
    if (!initialized_ || oldPin != currentPin_) {
        lastError_ = "Invalid old PIN";
        return false;
    }
    
    // Re-encrypt with new PIN
    currentPin_ = newPin;
    
    QByteArray salt(16, 0);
    RAND_bytes(reinterpret_cast<unsigned char*>(salt.data()), 16);
    
    QByteArray key = deriveKey(currentPin_, salt);
    QByteArray iv;
    QByteArray encrypted = encrypt(decryptedData_, key, iv);
    
    QByteArray fileData = salt + iv + encrypted;
    return saveToFile(fileData);
}

void SecureStorage::clear() {
    decryptedData_.fill(0);
    decryptedData_.clear();
    currentPin_.clear();
    initialized_ = false;
    
    QFile::remove(filePath_);
}

} // namespace BarkFluff