/**
 * @file SessionInfo.h
 * @brief Модель данных активной сессии пользователя
 */

#pragma once

#include <QString>
#include <QDateTime>
#include <QList>

namespace BarkFluff {

/**
 * @brief Информация об активной сессии
 */
struct SessionInfo {
    qint64 id = 0;                      ///< Идентификатор сессии
    QDateTime createdAt;                ///< Когда был первый вход
    QDateTime expirationAt;             ///< Когда сессия станет недействительной
    QString deviceId;                   ///< Идентификатор устройства (GUID)
    QString originalName;               ///< Оригинальное имя устройства
    QString customName;                 ///< Кастомное имя устройства
    QString appName;                    ///< Имя приложения
    QString operationSystem;            ///< Операционная система
    QString location;                   ///< Страна/город
    
    /**
     * @brief Получить отображаемое имя устройства
     */
    QString displayName() const {
        if (!customName.isEmpty()) {
            return customName;
        }
        if (!originalName.isEmpty()) {
            return originalName;
        }
        return QObject::tr("Неизвестное устройство");
    }
    
    /**
     * @brief Проверить, является ли сессия текущей
     */
    bool isCurrentSession(const QString& currentDeviceId) const {
        return deviceId == currentDeviceId;
    }
};

/**
 * @brief Статус двухфакторной аутентификации
 */
struct OTPStatus {
    bool authenticatorEnabled = false;  ///< Включен ли 2FA по аутентификатору
    bool emailEnabled = false;          ///< Включен ли 2FA по почте
    
    /**
     * @brief Проверить, включен ли какой-либо 2FA
     */
    bool isAnyEnabled() const {
        return authenticatorEnabled || emailEnabled;
    }
};

/**
 * @brief Тип OTP аутентификации
 */
enum class OTPType {
    Unknown = 0,
    Authenticator = 1,
    Email = 2
};

} // namespace BarkFluff

Q_DECLARE_METATYPE(BarkFluff::SessionInfo)
Q_DECLARE_METATYPE(BarkFluff::OTPStatus)