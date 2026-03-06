/**
 * @file NotificationService.cpp
 * @brief Реализация сервиса системных уведомлений
 */

#include "NotificationService.h"
#include <QSystemTrayIcon>
#include <QDBusInterface>
#include <QDBusConnection>
#include <QDBusReply>
#include <QApplication>
#include <QWidget>
#include <QImage>
#include <QDebug>
#include <QDir>
#include <QDateTime>
#include <QUrl>

namespace BarkFluff {

NotificationService::NotificationService() = default;

NotificationService::~NotificationService() {
    delete notifyInterface_;
    delete trayIcon_;
}

NotificationService& NotificationService::instance() {
    static NotificationService instance;
    return instance;
}

void NotificationService::initialize() {
    // Try to connect to D-Bus notifications
    QDBusConnection sessionBus = QDBusConnection::sessionBus();
    
    notifyInterface_ = new QDBusInterface(
        QStringLiteral("org.freedesktop.Notifications"),
        QStringLiteral("/org/freedesktop/Notifications"),
        QStringLiteral("org.freedesktop.Notifications"),
        sessionBus
    );
    
    if (notifyInterface_->isValid()) {
        dbusAvailable_ = true;
        
        // Connect to action signals
        sessionBus.connect(
            QStringLiteral("org.freedesktop.Notifications"),
            QStringLiteral("/org/freedesktop/Notifications"),
            QStringLiteral("org.freedesktop.Notifications"),
            QStringLiteral("ActionInvoked"),
            this,
            SLOT(onDBusActionInvoked(uint,QString))
        );
        
        sessionBus.connect(
            QStringLiteral("org.freedesktop.Notifications"),
            QStringLiteral("/org/freedesktop/Notifications"),
            QStringLiteral("org.freedesktop.Notifications"),
            QStringLiteral("NotificationClosed"),
            this,
            SLOT(onDBusNotificationClosed(uint,uint))
        );
        
        qDebug() << "NotificationService: D-Bus notifications available";
    } else {
        dbusAvailable_ = false;
        qWarning() << "NotificationService: D-Bus not available, using system tray fallback";
        
        // Create system tray icon as fallback
        trayIcon_ = new QSystemTrayIcon();
        trayIcon_->setIcon(QApplication::windowIcon());
        trayIcon_->setVisible(true);
        
        connect(trayIcon_, &QSystemTrayIcon::messageClicked,
                this, &NotificationService::onTrayMessageClicked);
    }
}

void NotificationService::setMainWindow(QWidget* window) {
    mainWindow_ = window;
}

void NotificationService::setCurrentChatId(const QString& chatId) {
    currentChatId_ = chatId;
}

void NotificationService::setSettings(const NotificationSettings& settings) {
    settings_ = settings;
}

NotificationSettings NotificationService::settings() const {
    return settings_;
}

bool NotificationService::shouldShowNotification(const QString& chatId, qint64 senderId, qint64 currentUserId) const {
    // Check if notifications are enabled
    if (!settings_.enabled) {
        return false;
    }
    
    // Don't show for own messages
    if (senderId == currentUserId) {
        return false;
    }
    
    // Don't show if main window is active and chat is open
    if (isMainWindowActive() && isCurrentChat(chatId)) {
        return false;
    }
    
    return true;
}

void NotificationService::showMessageNotification(
    const QString& chatId,
    const QString& chatName,
    const QString& senderName,
    const QString& messageText,
    const QPixmap& avatar
) {
    QString title = chatName.isEmpty() ? senderName : chatName;
    QString body = settings_.showPreview ? messageText : tr("Новое сообщение");
    
    QPixmap displayAvatar = settings_.showAvatar ? avatar : QPixmap();
    
    qDebug() << "NotificationService::showMessageNotification - title:" << title 
             << "avatar.isNull:" << avatar.isNull() 
             << "displayAvatar.isNull:" << displayAvatar.isNull()
             << "dbusAvailable:" << dbusAvailable_;
    
    if (dbusAvailable_) {
        if (tryDBusNotification(chatId, title, body, displayAvatar)) {
            return;
        }
    }
    
    // Fallback to system tray
    showTrayNotification(chatId, title, body, displayAvatar);
}

bool NotificationService::tryDBusNotification(
    const QString& chatId,
    const QString& title,
    const QString& body,
    const QPixmap& avatar
) {
    if (!notifyInterface_ || !notifyInterface_->isValid()) {
        return false;
    }
    
    // Prepare hints
    QVariantMap hints;
    
    // Add image hint if avatar is available
    if (!avatar.isNull()) {
        QVariantMap imageHint = createImageHint(avatar);
        // Merge image hint into hints
    for (auto it = imageHint.constBegin(); it != imageHint.constEnd(); ++it) {
        hints.insert(it.key(), it.value());
    }
    }
    
    // System sound hint
    hints[QStringLiteral("sound-name")] = QStringLiteral("message-new-instant");
    
    // Desktop entry for proper app identification
    hints[QStringLiteral("desktop-entry")] = QStringLiteral("BarkFluff");
    
    // Urgency: 0=low, 1=normal, 2=critical
    hints[QStringLiteral("urgency")] = 1;
    
    // Actions: default (click), mark-read button
    QStringList actions;
    actions << QStringLiteral("default") << tr("Открыть");
    actions << QStringLiteral("mark-read") << tr("Прочитано");
    
    // Store chat ID for callback
    lastNotificationChatId_ = chatId;
    
    // Call Notify method
    QDBusReply<uint> reply = notifyInterface_->call(
        QStringLiteral("Notify"),
        QStringLiteral("BarkFluff"),      // app_name
        lastNotificationId_,               // replaces_id (0 = new, or replace existing)
        QString(),                         // app_icon (empty, using image-data)
        title,                             // summary
        body,                              // body
        actions,                           // actions
        hints,                             // hints
        -1                                 // expire_timeout (-1 = default)
    );
    
    if (reply.isValid()) {
        lastNotificationId_ = reply.value();
        qDebug() << "NotificationService: D-Bus notification shown, id:" << lastNotificationId_;
        return true;
    } else {
        qWarning() << "NotificationService: D-Bus Notify failed:" << reply.error().message();
        return false;
    }
}

void NotificationService::showTrayNotification(
    const QString& chatId,
    const QString& title,
    const QString& body,
    const QPixmap& avatar
) {
    if (!trayIcon_) {
        qWarning() << "NotificationService: No system tray available";
        return;
    }
    
    lastTrayChatId_ = chatId;
    
    QIcon icon;
    if (!avatar.isNull()) {
        icon = QIcon(avatar);
    } else {
        icon = QApplication::windowIcon();
    }
    
    trayIcon_->showMessage(title, body, icon, 5000);
    qDebug() << "NotificationService: Tray notification shown";
}

void NotificationService::onDBusActionInvoked(uint notificationId, const QString& actionKey) {
    Q_UNUSED(notificationId);
    
    qDebug() << "NotificationService: Action invoked:" << actionKey;
    
    if (actionKey == QStringLiteral("default")) {
        emit notificationClicked(lastNotificationChatId_);
    } else if (actionKey == QStringLiteral("mark-read")) {
        emit markAsReadRequested(lastNotificationChatId_);
    }
}

void NotificationService::onDBusNotificationClosed(uint notificationId, uint reason) {
    Q_UNUSED(notificationId);
    Q_UNUSED(reason);
    
    // Clear stored chat ID
    lastNotificationChatId_.clear();
}

void NotificationService::onTrayMessageClicked() {
    emit notificationClicked(lastTrayChatId_);
}

bool NotificationService::isMainWindowActive() const {
    return mainWindow_ && mainWindow_->isActiveWindow();
}

bool NotificationService::isCurrentChat(const QString& chatId) const {
    return currentChatId_ == chatId;
}

QVariantMap NotificationService::createImageHint(const QPixmap& pixmap) const {
    QVariantMap hint;
    
    qDebug() << "NotificationService::createImageHint - pixmap.isNull:" << pixmap.isNull()
             << "size:" << pixmap.size();
    
    if (pixmap.isNull()) {
        return hint;
    }
    
    // Scale to reasonable notification icon size (max 128x128)
    QPixmap scaled = pixmap;
    if (scaled.width() > 128 || scaled.height() > 128) {
        scaled = scaled.scaled(128, 128, Qt::KeepAspectRatio, Qt::SmoothTransformation);
    }
    
    // Save to temporary file - this is the most reliable way to pass image to D-Bus
    QString tempPath = QDir::tempPath() + QDir::separator() + 
                       QStringLiteral("barkfluff_avatar_%1.png").arg(QDateTime::currentMSecsSinceEpoch());
    
    if (scaled.save(tempPath, "PNG")) {
        // Use image-path hint (supported by many notification daemons)
        hint[QStringLiteral("image-path")] = QUrl::fromLocalFile(tempPath).toString();
        
        qDebug() << "NotificationService::createImageHint - saved to:" << tempPath;
    } else {
        qWarning() << "NotificationService::createImageHint - failed to save temp image";
    }
    
    return hint;
}

} // namespace BarkFluff