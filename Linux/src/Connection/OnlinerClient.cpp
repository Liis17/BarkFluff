/**
 * @file OnlinerClient.cpp
 * @brief Реализация клиента статусов онлайн
 */

#include "OnlinerClient.h"
#include <QDateTime>
#include <QTimeZone>
#include <QDebug>
#include <QtConcurrent>

namespace BarkFluff {

// ============================================================================
// UserOnlineStatusInfo
// ============================================================================

QString UserOnlineStatusInfo::statusText() const {
    switch (status) {
        case OnlineStatus::Online:
            return QObject::tr("В сети");
        case OnlineStatus::Offline:
            if (lastSeen.isValid()) {
                qint64 secsAgo = lastSeen.secsTo(QDateTime::currentDateTime());
                if (secsAgo < 60) {
                    return QObject::tr("Был(а) только что");
                } else if (secsAgo < 3600) {
                    int mins = secsAgo / 60;
                    return QObject::tr("Был(а) %n мин. назад", "", mins);
                } else if (secsAgo < 86400) {
                    int hours = secsAgo / 3600;
                    return QObject::tr("Был(а) %n ч. назад", "", hours);
                } else {
                    return QObject::tr("Был(а) %1").arg(lastSeen.toString("dd.MM.yyyy"));
                }
            }
            return QObject::tr("Не в сети");
        case OnlineStatus::Unknown:
        default:
            return QObject::tr("Статус неизвестен");
    }
}

QString UserOnlineStatusInfo::statusColor() const {
    switch (status) {
        case OnlineStatus::Online:
            return "#4CAF50";  // Green
        case OnlineStatus::Offline:
            return "#9E9E9E";  // Grey
        case OnlineStatus::Unknown:
        default:
            return "transparent";
    }
}

// ============================================================================
// OnlinerClient
// ============================================================================

OnlinerClient::OnlinerClient(const ServiceInfo& service, QObject* parent)
    : QObject(parent)
    , GrpcClient(service.endpoint.host, service.endpoint.port, service.tlsEnabled)
{
    stub_ = barkfluff::onliner::OnlinerApi::NewStub(channel_);
}

OnlinerClient::~OnlinerClient() {
    stopSubscription();
}

void OnlinerClient::setOnlineStatus(const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::onliner::SetOnlineStatusRequest request;
    barkfluff::onliner::SetOnlineStatusResponse response;
    
    grpc::Status status = stub_->SetOnlineStatus(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "OnlinerClient::setOnlineStatus failed:" 
                   << QString::fromStdString(status.error_message());
    }
}

QList<UserOnlineStatusInfo> OnlinerClient::getOnlineStatus(
    const QList<qint64>& userIds,
    const QString& accessToken
) {
    QList<UserOnlineStatusInfo> result;
    
    auto context = createContext(accessToken);
    
    barkfluff::onliner::GetOnlineStatusRequest request;
    for (qint64 id : userIds) {
        request.add_user_ids(id);
    }
    
    barkfluff::onliner::GetOnlineStatusResponse response;
    grpc::Status status = stub_->GetOnlineStatus(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "OnlinerClient::getOnlineStatus failed:" 
                   << QString::fromStdString(status.error_message());
        return result;
    }
    
    for (const auto& protoStatus : response.users_statuses()) {
        UserOnlineStatusInfo info;
        info.userId = protoStatus.user_id();
        
        switch (protoStatus.status()) {
            case barkfluff::onliner::STATUS_ONLINE:
                info.status = OnlineStatus::Online;
                break;
            case barkfluff::onliner::STATUS_OFFLINE:
                info.status = OnlineStatus::Offline;
                break;
            default:
                info.status = OnlineStatus::Unknown;
        }
        
        if (protoStatus.has_last_seen()) {
            const auto& timestamp = protoStatus.last_seen();
            qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
            info.lastSeen = QDateTime::fromMSecsSinceEpoch(millis, QTimeZone::UTC);
        }
        
        result.append(info);
    }
    
    return result;
}

void OnlinerClient::startSubscription(
    const QList<qint64>& userIds,
    const QString& accessToken
) {
    if (subscribed_) {
        updateSubscription(userIds);
        return;
    }
    
    stopping_ = false;
    subscriptionContext_ = createContextNoDeadline(accessToken);
    
    barkfluff::onliner::SubscribeToOnlineStatusRequest request;
    for (qint64 id : userIds) {
        request.add_user_ids(id);
    }
    
    subscriptionReader_ = stub_->SubscribeToOnlineStatus(subscriptionContext_.get(), request);
    subscribed_ = true;
    
    // Start reading in background thread using QtConcurrent
    QtConcurrent::run([this]() {
        readSubscriptionLoop();
    });
}

void OnlinerClient::updateSubscription(const QList<qint64>& userIds) {
    if (!subscribed_) {
        return;
    }
    
    auto context = createContext();
    
    barkfluff::onliner::ChangeUsersInSubscriptionRequest request;
    for (qint64 id : userIds) {
        request.add_user_ids(id);
    }
    
    barkfluff::onliner::ChangeUsersInSubscriptionResponse response;
    grpc::Status status = stub_->ChangeUsersInSubscription(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "OnlinerClient::updateSubscription failed:" 
                   << QString::fromStdString(status.error_message());
    }
}

void OnlinerClient::stopSubscription() {
    if (!subscribed_) {
        return;
    }
    
    stopping_ = true;
    if (subscriptionContext_) {
        subscriptionContext_->TryCancel();
    }
    subscribed_ = false;
}

void OnlinerClient::readSubscriptionLoop() {
    barkfluff::onliner::UserOnlineStatus protoStatus;
    
    while (subscribed_ && !stopping_ && subscriptionReader_) {
        if (!subscriptionReader_->Read(&protoStatus)) {
            if (!stopping_) {
                grpc::Status status = subscriptionReader_->Finish();
                if (!status.ok()) {
                    QString error = QString::fromStdString(status.error_message());
                    qWarning() << "OnlinerClient subscription ended:" << error;
                    emit subscriptionError(error);
                }
            }
            break;
        }
        
        UserOnlineStatusInfo info;
        info.userId = protoStatus.user_id();
        
        switch (protoStatus.status()) {
            case barkfluff::onliner::STATUS_ONLINE:
                info.status = OnlineStatus::Online;
                break;
            case barkfluff::onliner::STATUS_OFFLINE:
                info.status = OnlineStatus::Offline;
                break;
            default:
                info.status = OnlineStatus::Unknown;
        }
        
        if (protoStatus.has_last_seen()) {
            const auto& timestamp = protoStatus.last_seen();
            qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
            info.lastSeen = QDateTime::fromMSecsSinceEpoch(millis, QTimeZone::UTC);
        }
        
        emit statusChanged(info);
    }
    
    subscribed_ = false;
}

} // namespace BarkFluff