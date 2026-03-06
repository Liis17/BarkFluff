/**
 * @file OnlinerClient.h
 * @brief gRPC клиент для сервиса статусов онлайн
 */

#pragma once

#include "GrpcClient.h"
#include "Models/ServerConfiguration.h"
#include "onliner_api.grpc.pb.h"

#include <QObject>
#include <QDateTime>
#include <QMutex>
#include <functional>
#include <atomic>

namespace BarkFluff {

/**
 * @brief Статус онлайн пользователя
 */
enum class OnlineStatus {
    Unknown,    ///< Неизвестно
    Online,     ///< В сети
    Offline     ///< Не в сети
};

/**
 * @brief Информация о статусе пользователя
 */
struct UserOnlineStatusInfo {
    qint64 userId = 0;
    OnlineStatus status = OnlineStatus::Unknown;
    QDateTime lastSeen;
    
    /**
     * @brief Текстовое представление статуса
     */
    QString statusText() const;
    
    /**
     * @brief Цвет статуса для отображения
     */
    QString statusColor() const;
};

/**
 * @brief gRPC клиент для сервиса Onliner
 * 
 * Управляет статусами онлайн пользователей.
 * Поддерживает подписку на изменения статуса.
 */
class OnlinerClient : public QObject, public GrpcClient {
    Q_OBJECT
    
    std::unique_ptr<barkfluff::onliner::OnlinerApi::Stub> stub_;
    
    // Streaming state
    std::unique_ptr<grpc::ClientContext> subscriptionContext_;
    std::unique_ptr<grpc::ClientReader<barkfluff::onliner::UserOnlineStatus>> subscriptionReader_;
    
    std::atomic<bool> subscribed_{false};
    std::atomic<bool> stopping_{false};
    
public:
    explicit OnlinerClient(const ServiceInfo& service, QObject* parent = nullptr);
    ~OnlinerClient() override;
    
    /**
     * @brief Установить свой статус "В сети"
     */
    void setOnlineStatus(const QString& accessToken);
    
    /**
     * @brief Получить статусы пользователей
     */
    QList<UserOnlineStatusInfo> getOnlineStatus(
        const QList<qint64>& userIds,
        const QString& accessToken
    );
    
    /**
     * @brief Начать подписку на изменения статуса
     */
    void startSubscription(
        const QList<qint64>& userIds,
        const QString& accessToken
    );
    
    /**
     * @brief Изменить список пользователей в подписке
     */
    void updateSubscription(const QList<qint64>& userIds);
    
    /**
     * @brief Остановить подписку
     */
    void stopSubscription();
    
    /**
     * @brief Активна ли подписка
     */
    bool isSubscribed() const { return subscribed_; }
    
signals:
    /**
     * @brief Изменился статус пользователя
     */
    void statusChanged(const UserOnlineStatusInfo& status);
    
    /**
     * @brief Ошибка подписки
     */
    void subscriptionError(const QString& error);
    
private:
    void readSubscriptionLoop();
};

} // namespace BarkFluff