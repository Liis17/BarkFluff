#pragma once

#include <QString>
#include <QColor>

namespace BarkFluff {

/**
 * @brief Статус валидации имени пользователя
 */
enum class UsernameValidationStatus {
    Checking,    // Проверка...
    Available,   // Имя доступно
    Taken,       // Имя уже занято
    TooShort,    // < 3 символов
    Invalid,     // недопустимые символы
    Unchanged,   // совпадает с текущим
    None         // нет статуса
};

/**
 * @brief Получить сообщение для статуса валидации
 */
inline QString usernameStatusMessage(UsernameValidationStatus status) {
    switch (status) {
        case UsernameValidationStatus::Checking:
            return QStringLiteral("Проверка...");
        case UsernameValidationStatus::Available:
            return QStringLiteral("Имя доступно");
        case UsernameValidationStatus::Taken:
            return QStringLiteral("Имя уже занято");
        case UsernameValidationStatus::TooShort:
            return QStringLiteral("Минимум 3 символа");
        case UsernameValidationStatus::Invalid:
            return QStringLiteral("Только буквы, цифры и подчёркивание");
        case UsernameValidationStatus::Unchanged:
        case UsernameValidationStatus::None:
            return QString();
    }
    return QString();
}

/**
 * @brief Получить иконку для статуса валидации (Unicode символ)
 */
inline QString usernameStatusIcon(UsernameValidationStatus status) {
    switch (status) {
        case UsernameValidationStatus::Checking:
            return QStringLiteral("⟳");  // Обновление
        case UsernameValidationStatus::Available:
        case UsernameValidationStatus::Unchanged:
            return QStringLiteral("✓");  // Галочка
        case UsernameValidationStatus::Taken:
            return QStringLiteral("✗");  // Крестик
        case UsernameValidationStatus::TooShort:
        case UsernameValidationStatus::Invalid:
            return QStringLiteral("⚠");  // Предупреждение
        case UsernameValidationStatus::None:
            return QString();
    }
    return QString();
}

/**
 * @brief Получить цвет для статуса валидации
 */
inline QColor usernameStatusColor(UsernameValidationStatus status) {
    switch (status) {
        case UsernameValidationStatus::Checking:
            return QColor(128, 128, 128);  // Серый
        case UsernameValidationStatus::Available:
        case UsernameValidationStatus::Unchanged:
            return QColor(46, 125, 50);    // Зелёный
        case UsernameValidationStatus::Taken:
        case UsernameValidationStatus::TooShort:
        case UsernameValidationStatus::Invalid:
            return QColor(198, 40, 40);    // Красный
        case UsernameValidationStatus::None:
            return QColor();
    }
    return QColor();
}

/**
 * @brief Проверить, можно ли сохранять с данным статусом
 */
inline bool canSaveWithStatus(UsernameValidationStatus status) {
    return status == UsernameValidationStatus::Available ||
           status == UsernameValidationStatus::Unchanged ||
           status == UsernameValidationStatus::None;
}

} // namespace BarkFluff