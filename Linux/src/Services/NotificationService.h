/**
 * @file NotificationService.h
 * @brief Сервис системных уведомлений
 */

#pragma once

#include "Models/NotificationSettings.h"
#include <QObject>
#include <QPixmap>
#include <QVariantMap>

class QSystemTrayIcon;
class QDBusInterface;
class QWidget;

namespace BarkFluff {

/**
 * @brief Сервис системных уведомлений
 * 
 * Использует D-Bus (org.freedesktop.Notifications) как основной метод,
 * QSystemTrayIcon как fallback.
 */
class NotificationService : public QObject {
    Q_OBJECT
    
public:
    static NotificationService& instance();
    
    /// Инициализация (вызвать при старте приложения)
    void initialize();
    
    /// Установить главное окно (для проверки активности)
    void setMainWindow(QWidget* window);
    
    /// Установить текущий открытый чат
    void setCurrentChatId(const QString& chatId);
    
    /// Показать уведомление о новом сообщении
    void showMessageNotification(
        const QString& chatId,
        const QString& chatName,
        const QString& senderName,
        const QString& messageText,
        const QPixmap& avatar = QPixmap()
    );
    
    /// Настройки
    void setSettings(const NotificationSettings& settings);
    NotificationSettings settings() const;
    
    /// Проверка, нужно ли показывать уведомление
    bool shouldShowNotification(const QString& chatId, qint64 senderId, qint64 currentUserId) const;
    
signals:
    /// Клик по уведомлению (открыть чат)
    void notificationClicked(const QString& chatId);
    
    /// Запрос "прочитано" из уведомления
    void markAsReadRequested(const QString& chatId);

private slots:
    void onDBusActionInvoked(uint notificationId, const QString& actionKey);
    void onDBusNotificationClosed(uint notificationId, uint reason);
    void onTrayMessageClicked();

private:
    NotificationService();
    ~NotificationService() override;
    
    NotificationService(const NotificationService&) = delete;
    NotificationService& operator=(const NotificationService&) = delete;
    
    /// Попытка показать через D-Bus
    bool tryDBusNotification(
        const QString& chatId,
        const QString& title,
        const QString& body,
        const QPixmap& avatar
    );
    
    /// Показ через QSystemTrayIcon (fallback)
    void showTrayNotification(
        const QString& chatId,
        const QString& title,
        const QString& body,
        const QPixmap& avatar
    );
    
    /// Проверка, активно ли главное окно
    bool isMainWindowActive() const;
    
    /// Проверка, является ли чат текущим открытым
    bool isCurrentChat(const QString& chatId) const;
    
    /// Преобразование QPixmap в D-Bus image hint
    QVariantMap createImageHint(const QPixmap& pixmap) const;
    
    QWidget* mainWindow_ = nullptr;
    QString currentChatId_;
    NotificationSettings settings_;
    
    // D-Bus
    QDBusInterface* notifyInterface_ = nullptr;
    uint lastNotificationId_ = 0;
    QString lastNotificationChatId_;
    bool dbusAvailable_ = false;
    
    // Fallback
    QSystemTrayIcon* trayIcon_ = nullptr;
    QString lastTrayChatId_;
};

} // namespace BarkFluff