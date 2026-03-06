#pragma once

#include "Storage/SecureStorage.h"
#include "Storage/AppSettings.h"
#include "Models/UserSession.h"
#include "Models/ServerConfiguration.h"
#include <QString>
#include <QObject>

namespace BarkFluff {

/**
 * @brief Менеджер сессии - координирует безопасное хранение токенов и настроек
 * 
 * Отвечает за:
 * - Сохранение и восстановление сессии
 * - Управление PIN-кодом
 * - Обновление токенов
 */
class SessionManager : public QObject {
    Q_OBJECT

public:
    static SessionManager& instance();
    
    // Check if there's a stored session that can be restored
    bool hasStoredSession() const;
    
    // Initialize storage with PIN (for first-time setup or unlock)
    // Returns true if successful, false if PIN is incorrect
    bool initializeWithPin(const QString& pin);
    
    // Check if this is first-time setup (no PIN set yet)
    bool isFirstTimeSetup() const;
    
    // Save session after successful login
    void saveSession(const UserSession& session, const ServerConfiguration& serverConfig, const QString& login);
    
    // PIN length management
    void setPinLength(int length);
    int getStoredPinLength() const;
    
    // Restore session (call after initializeWithPin)
    // Returns empty session if failed
    UserSession restoreSession();
    
    // Get stored server configuration
    // Returns invalid config if not stored
    ServerConfiguration getStoredServerConfig();
    
    // Try to refresh access token using refresh token
    // Returns new access token or empty on failure
    Token refreshAccessToken();
    
    // Clear all stored data (logout)
    void clearSession();
    
    // Get last login for autofill
    QString getLastLogin() const;
    
    // Get server name for display
    QString getLastServerName() const;
    
    // Get error message from last operation
    QString lastError() const { return lastError_; }

signals:
    // Emitted when session is about to be cleared
    void sessionCleared();

private:
    SessionManager();
    ~SessionManager() = default;
    
    SessionManager(const SessionManager&) = delete;
    SessionManager& operator=(const SessionManager&) = delete;
    
    SecureStorage secureStorage_;
    QString lastError_;
    bool initialized_ = false;
};

} // namespace BarkFluff