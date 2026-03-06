#include "SessionManager.h"
#include "Connection/BeaconClient.h"
#include "Connection/IdentityClient.h"
#include "Utils/JwtUtils.h"
#include <QDebug>

namespace BarkFluff {

SessionManager::SessionManager() {
}

SessionManager& SessionManager::instance() {
    static SessionManager instance;
    return instance;
}

bool SessionManager::hasStoredSession() const {
    // Check both: settings flag and actual secure storage
    return AppSettings::instance().hasStoredSession() && secureStorage_.hasStoredData();
}

bool SessionManager::isFirstTimeSetup() const {
    // First time if no secure storage exists
    return !secureStorage_.hasStoredData();
}

bool SessionManager::initializeWithPin(const QString& pin) {
    if (!secureStorage_.initialize(pin)) {
        lastError_ = secureStorage_.lastError();
        initialized_ = false;
        return false;
    }
    
    initialized_ = true;
    return true;
}

void SessionManager::saveSession(const UserSession& session, const ServerConfiguration& serverConfig, const QString& login) {
    if (!initialized_) {
        qWarning() << "SessionManager: Cannot save session - not initialized with PIN";
        return;
    }
    
    // Save tokens to secure storage
    secureStorage_.saveTokens(
        session.accessToken.value,
        session.refreshToken.value,
        session.accessToken.expirationDate,
        session.refreshToken.expirationDate
    );
    
    // Save non-sensitive data to app settings
    AppSettings::instance().setLastLogin(login);
    AppSettings::instance().setHasStoredSession(true);
    
    qDebug() << "SessionManager: Session saved for user" << session.username;
}

UserSession SessionManager::restoreSession() {
    UserSession session;
    
    if (!initialized_) {
        lastError_ = "Storage not initialized with PIN";
        return session;
    }
    
    // Get tokens from secure storage
    QString accessToken = secureStorage_.getAccessToken();
    QString refreshToken = secureStorage_.getRefreshToken();
    QDateTime accessExpiry = secureStorage_.getAccessExpiry();
    QDateTime refreshExpiry = secureStorage_.getRefreshExpiry();
    
    if (accessToken.isEmpty() || refreshToken.isEmpty()) {
        lastError_ = "No tokens found in storage";
        return session;
    }
    
    session.accessToken.value = accessToken;
    session.accessToken.expirationDate = accessExpiry;
    session.refreshToken.value = refreshToken;
    session.refreshToken.expirationDate = refreshExpiry;
    
    // Extract user info from JWT
    session.userId = JwtUtils::extractUserId(accessToken);
    session.username = JwtUtils::extractUsername(accessToken);
    
    qDebug() << "SessionManager: Session restored for user" << session.username;
    
    return session;
}

ServerConfiguration SessionManager::getStoredServerConfig() {
    auto lastServer = AppSettings::instance().getLastServer();
    if (!lastServer.isValid()) {
        lastError_ = "No stored server configuration";
        return ServerConfiguration();
    }
    
    // Try to fetch fresh config from beacon
    try {
        BeaconClient beacon(lastServer.beaconHost, lastServer.beaconPort, true, true);
        ServerConfiguration config = beacon.getServerInfo();
        qDebug() << "SessionManager: Got server config from beacon for" << config.name;
        return config;
    } catch (const std::exception& e) {
        lastError_ = QString("Failed to connect to server: %1").arg(e.what());
        qWarning() << "SessionManager:" << lastError_;
        return ServerConfiguration();
    }
}

Token SessionManager::refreshAccessToken() {
    Token newToken;
    
    if (!initialized_) {
        lastError_ = "Storage not initialized with PIN";
        return newToken;
    }
    
    QString refreshToken = secureStorage_.getRefreshToken();
    if (refreshToken.isEmpty()) {
        lastError_ = "No refresh token stored";
        return newToken;
    }
    
    // Get server config
    auto serverConfig = getStoredServerConfig();
    if (!serverConfig.isValid()) {
        // lastError_ already set by getStoredServerConfig
        return newToken;
    }
    
    try {
        IdentityClient identity(serverConfig.identity);
        newToken = identity.createToken(refreshToken);
        
        // Update stored access token
        secureStorage_.saveTokens(
            newToken.value,
            refreshToken,  // Keep same refresh token
            newToken.expirationDate,
            secureStorage_.getRefreshExpiry()
        );
        
        qDebug() << "SessionManager: Access token refreshed, expires at" << newToken.expirationDate;
    } catch (const std::exception& e) {
        lastError_ = QString("Failed to refresh token: %1").arg(e.what());
        qWarning() << "SessionManager:" << lastError_;
    }
    
    return newToken;
}

void SessionManager::clearSession() {
    emit sessionCleared();
    
    secureStorage_.clear();
    AppSettings::instance().clear();
    initialized_ = false;
    
    qDebug() << "SessionManager: Session cleared";
}

QString SessionManager::getLastLogin() const {
    return AppSettings::instance().getLastLogin();
}

QString SessionManager::getLastServerName() const {
    return AppSettings::instance().getLastServer().name;
}

void SessionManager::setPinLength(int length) {
    secureStorage_.setPinLength(length);
}

int SessionManager::getStoredPinLength() const {
    return secureStorage_.getStoredPinLength();
}

} // namespace BarkFluff
