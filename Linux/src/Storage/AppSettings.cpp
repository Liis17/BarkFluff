#include "AppSettings.h"
#include <QFile>
#include <QDir>
#include <QJsonDocument>
#include <QStandardPaths>
#include <QDebug>

namespace BarkFluff {

AppSettings::AppSettings() {
    QString configPath = QStandardPaths::writableLocation(QStandardPaths::ConfigLocation);
    filePath_ = configPath + "/BarkFluff/settings.json";
    
    // Load on construction
    load();
}

AppSettings& AppSettings::instance() {
    static AppSettings instance;
    return instance;
}

bool AppSettings::load() {
    QFile file(filePath_);
    if (!file.exists()) {
        qDebug() << "AppSettings: No settings file found at" << filePath_;
        return true; // Not an error, just first run
    }
    
    if (!file.open(QIODevice::ReadOnly)) {
        qWarning() << "AppSettings: Cannot open file for reading:" << filePath_;
        return false;
    }
    
    QByteArray data = file.readAll();
    file.close();
    
    QJsonParseError error;
    QJsonDocument doc = QJsonDocument::fromJson(data, &error);
    
    if (error.error != QJsonParseError::NoError) {
        qWarning() << "AppSettings: JSON parse error:" << error.errorString();
        return false;
    }
    
    return fromJson(doc.object());
}

bool AppSettings::save() {
    QDir().mkpath(QFileInfo(filePath_).absolutePath());
    
    QFile file(filePath_);
    if (!file.open(QIODevice::WriteOnly)) {
        qWarning() << "AppSettings: Cannot open file for writing:" << filePath_;
        return false;
    }
    
    QJsonDocument doc(toJson());
    file.write(doc.toJson(QJsonDocument::Indented));
    file.close();
    
    qDebug() << "AppSettings: Saved to" << filePath_;
    return true;
}

QJsonObject AppSettings::toJson() const {
    QJsonObject json;
    
    // Last server
    QJsonObject serverJson;
    serverJson["beacon_host"] = lastServer_.beaconHost;
    serverJson["beacon_port"] = lastServer_.beaconPort;
    serverJson["name"] = lastServer_.name;
    json["last_server"] = serverJson;
    
    // Other settings
    json["last_login"] = lastLogin_;
    json["has_stored_session"] = hasStoredSession_;
    
    // Notification settings
    QJsonObject notifyJson;
    notifyJson["enabled"] = notificationSettings_.enabled;
    notifyJson["show_preview"] = notificationSettings_.showPreview;
    notifyJson["show_avatar"] = notificationSettings_.showAvatar;
    json["notifications"] = notifyJson;
    
    return json;
}

bool AppSettings::fromJson(const QJsonObject& json) {
    // Last server
    if (json.contains("last_server")) {
        QJsonObject serverJson = json["last_server"].toObject();
        lastServer_.beaconHost = serverJson["beacon_host"].toString();
        lastServer_.beaconPort = static_cast<quint16>(serverJson["beacon_port"].toInt());
        lastServer_.name = serverJson["name"].toString();
    }
    
    // Other settings
    lastLogin_ = json["last_login"].toString();
    hasStoredSession_ = json["has_stored_session"].toBool(false);
    
    // Notification settings
    if (json.contains("notifications")) {
        QJsonObject notifyJson = json["notifications"].toObject();
        notificationSettings_.enabled = notifyJson["enabled"].toBool(true);
        notificationSettings_.showPreview = notifyJson["show_preview"].toBool(true);
        notificationSettings_.showAvatar = notifyJson["show_avatar"].toBool(true);
    }
    
    qDebug() << "AppSettings: Loaded - server:" << lastServer_.name 
             << "login:" << lastLogin_ 
             << "hasSession:" << hasStoredSession_
             << "notifications:" << notificationSettings_.enabled;
    
    return true;
}

void AppSettings::setLastServer(const LastServer& server) {
    lastServer_ = server;
    save();
}

void AppSettings::setLastServer(const QString& host, quint16 port, const QString& name) {
    lastServer_.beaconHost = host;
    lastServer_.beaconPort = port;
    lastServer_.name = name;
    save();
}

void AppSettings::setLastLogin(const QString& login) {
    lastLogin_ = login;
    save();
}

void AppSettings::setHasStoredSession(bool has) {
    hasStoredSession_ = has;
    save();
}

void AppSettings::clear() {
    lastServer_.clear();
    lastLogin_.clear();
    hasStoredSession_ = false;
    // Keep notification settings on clear
    save();
}

void AppSettings::setNotificationSettings(const NotificationSettings& settings) {
    notificationSettings_ = settings;
    save();
}

} // namespace BarkFluff
