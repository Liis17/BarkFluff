/**
 * @file JwtUtils.h
 * @brief Утилиты для работы с JWT токенами
 */

#pragma once

#include <QString>
#include <QJsonObject>
#include <QJsonDocument>
#include <QByteArray>
#include <QDateTime>
#include <QDebug>

namespace BarkFluff {

/**
 * @brief Декодирование JWT токена без проверки подписи
 * 
 * Извлекает payload из JWT токена для получения user_id и других claims
 */
class JwtUtils {
public:
    /**
     * @brief Декодировать payload JWT токена
     * @param token JWT токен
     * @return QJsonObject с полями из payload, или пустой объект при ошибке
     */
    static QJsonObject decodePayload(const QString& token) {
        if (token.isEmpty()) {
            return QJsonObject();
        }
        
        // JWT формат: header.payload.signature
        auto parts = token.split('.');
        if (parts.size() != 3) {
            return QJsonObject();
        }
        
        // Декодируем payload (вторая часть)
        QString payloadBase64 = parts[1];
        
        // Base64url -> Base64
        payloadBase64.replace('-', '+');
        payloadBase64.replace('_', '/');
        
        // Добавляем padding если нужно
        while (payloadBase64.size() % 4 != 0) {
            payloadBase64.append('=');
        }
        
        QByteArray payloadJson = QByteArray::fromBase64(payloadBase64.toUtf8());
        
        QJsonDocument doc = QJsonDocument::fromJson(payloadJson);
        return doc.object();
    }
    
    /**
     * @brief Извлечь user_id из JWT токена
     * @param token JWT токен
     * @return user_id или 0 при ошибке
     */
    static qint64 extractUserId(const QString& token) {
        auto payload = decodePayload(token);
        if (payload.isEmpty()) {
            qDebug() << "JwtUtils: Failed to decode JWT payload";
            return 0;
        }
        
        // Debug: выводим все ключи payload
        qDebug() << "JwtUtils: JWT payload keys:" << payload.keys();
        
        // Обычно user_id находится в "sub" или "user_id" claim
        // Пробуем различные варианты в порядке приоритета
        QStringList userIdKeys = {
            "x-user-id",  // BarkFluff specific
            "sub",
            "user_id", 
            "userId",
            "id",
            "uid",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
            "nameid",
            "unique_name"
        };
        
        for (const QString& key : userIdKeys) {
            if (payload.contains(key)) {
                const QJsonValue& val = payload[key];
                qint64 value = 0;
                
                // Пробуем разные форматы значения
                if (val.isDouble()) {
                    value = static_cast<qint64>(val.toDouble());
                } else if (val.isString()) {
                    value = val.toString().toLongLong();
                } else {
                    value = val.toVariant().toLongLong();
                }
                
                if (value > 0) {
                    qDebug() << "JwtUtils: Found userId in" << key << "=" << value;
                    return value;
                }
            }
        }
        
        // Если ни один из ключей не найден, пробуем найти числовое значение
        for (const QString& key : payload.keys()) {
            auto value = payload[key].toVariant();
            bool ok = false;
            value.toLongLong(&ok);
            if (ok && value.toLongLong() > 0) {
                // Возможно это ID
                qDebug() << "JwtUtils: Potential userId in" << key << "=" << value;
            }
        }
        
        qDebug() << "JwtUtils: Could not find userId in JWT payload";
        return 0;
    }
    
    /**
     * @brief Извлечь username из JWT токена
     * @param token JWT токен
     * @return username или пустая строка
     */
    static QString extractUsername(const QString& token) {
        auto payload = decodePayload(token);
        if (payload.isEmpty()) {
            return QString();
        }
        
        if (payload.contains("username")) {
            return payload["username"].toString();
        }
        if (payload.contains("preferred_username")) {
            return payload["preferred_username"].toString();
        }
        if (payload.contains("name")) {
            return payload["name"].toString();
        }
        
        return QString();
    }
    
    /**
     * @brief Проверить, истекает ли токен в ближайшее время
     * @param token JWT токен
     * @param marginSeconds запас времени в секундах
     * @return true если токен истекает скоро
     */
    static bool isExpiringSoon(const QString& token, int marginSeconds = 300) {
        auto payload = decodePayload(token);
        if (payload.isEmpty()) {
            return true;
        }
        
        if (payload.contains("exp")) {
            qint64 exp = payload["exp"].toVariant().toLongLong();
            qint64 now = QDateTime::currentSecsSinceEpoch();
            return (exp - now) < marginSeconds;
        }
        
        return true;
    }
};

} // namespace BarkFluff