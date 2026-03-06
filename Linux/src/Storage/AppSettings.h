#pragma once

#include "Models/ServerInfo.h"
#include "Models/NotificationSettings.h"
#include <QString>
#include <QJsonObject>

namespace BarkFluff {

/**
 * @brief Настройки приложения (незащищённые данные)
 * 
 * Хранит:last server beacon URI
 * - Last login for autofill
 * - Server name for display
 */
class AppSettings {
public:
    static AppSettings& instance();
    
    // Load/save settings from/to disk
    bool load();
    bool save();
    
    // Last server info (for reconnection)
    struct LastServer {
        QString beaconHost;
        quint16 beaconPort = 0;
        QString name;
        
        bool isValid() const { return !beaconHost.isEmpty() && beaconPort > 0; }
        void clear() { beaconHost.clear(); beaconPort = 0; name.clear(); }
    };
    
    // Getters
    LastServer getLastServer() const { return lastServer_; }
    QString getLastLogin() const { return lastLogin_; }
    bool hasStoredSession() const { return hasStoredSession_; }
    
    // Setters
    void setLastServer(const LastServer& server);
    void setLastServer(const QString& host, quint16 port, const QString& name = QString());
    void setLastLogin(const QString& login);
    void setHasStoredSession(bool has);
    
    // Clear all settings
    void clear();
    
    // Notification settings
    NotificationSettings getNotificationSettings() const { return notificationSettings_; }
    void setNotificationSettings(const NotificationSettings& settings);
    
    // Get file path for debugging
    QString getFilePath() const { return filePath_; }

private:
    AppSettings();
    ~AppSettings() = default;
    
    AppSettings(const AppSettings&) = delete;
    AppSettings& operator=(const AppSettings&) = delete;
    
    QJsonObject toJson() const;
    bool fromJson(const QJsonObject& json);
    
    QString filePath_;
    LastServer lastServer_;
    QString lastLogin_;
    bool hasStoredSession_ = false;
    NotificationSettings notificationSettings_;
};

} // namespace BarkFluff