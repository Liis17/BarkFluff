/**
 * @file SettingsCategory.h
 * @brief Категории настроек приложения
 */

#pragma once

#include <QString>
#include <QIcon>

namespace BarkFluff {

/**
 * @brief Категории настроек
 */
enum class SettingsCategory {
    General,        ///< Общие настройки
    Sessions,       ///< Активные сессии
    Security,       ///< Безопасность (2FA, пароль)
    Storage,        ///< Хранилище и кеш
    About           ///< О сервере и приложении
};

/**
 * @brief Хелпер для работы с категориями настроек
 */
namespace SettingsCategoryHelper {

inline QString title(SettingsCategory category) {
    switch (category) {
        case SettingsCategory::General:
            return QObject::tr("Общие");
        case SettingsCategory::Sessions:
            return QObject::tr("Сессии");
        case SettingsCategory::Security:
            return QObject::tr("Безопасность");
        case SettingsCategory::Storage:
            return QObject::tr("Хранилище");
        case SettingsCategory::About:
            return QObject::tr("О сервере");
    }
    return QString();
}

inline QString iconName(SettingsCategory category) {
    switch (category) {
        case SettingsCategory::General:
            return QStringLiteral("preferences-system");
        case SettingsCategory::Sessions:
            return QStringLiteral("computer");
        case SettingsCategory::Security:
            return QStringLiteral("security-high");
        case SettingsCategory::Storage:
            return QStringLiteral("drive-harddisk");
        case SettingsCategory::About:
            return QStringLiteral("dialog-information");
    }
    return QString();
}

inline QString description(SettingsCategory category) {
    switch (category) {
        case SettingsCategory::General:
            return QObject::tr("Уведомления, звук, внешний вид");
        case SettingsCategory::Sessions:
            return QObject::tr("Управление активными устройствами");
        case SettingsCategory::Security:
            return QObject::tr("Двухфакторная аутентификация, пароль");
        case SettingsCategory::Storage:
            return QObject::tr("Очистка кеша, управление данными");
        case SettingsCategory::About:
            return QObject::tr("Информация о сервере и приложении");
    }
    return QString();
}

inline QList<SettingsCategory> allCategories() {
    return {
        SettingsCategory::General,
        SettingsCategory::Sessions,
        SettingsCategory::Security,
        SettingsCategory::Storage,
        SettingsCategory::About
    };
}

} // namespace SettingsCategoryHelper

} // namespace BarkFluff