/**
 * @file NotificationSettings.h
 * @brief Модель настроек уведомлений
 */

#pragma once

namespace BarkFluff {

/**
 * @brief Настройки уведомлений
 */
struct NotificationSettings {
    bool enabled = true;       ///< Уведомления вкл/выкл
    bool showPreview = true;   ///< Показывать текст сообщения
    bool showAvatar = true;    ///< Показывать аватар отправителя
    // Звук — системный, не настраивается отдельно
    
    bool operator==(const NotificationSettings& other) const {
        return enabled == other.enabled &&
               showPreview == other.showPreview &&
               showAvatar == other.showAvatar;
    }
};

} // namespace BarkFluff