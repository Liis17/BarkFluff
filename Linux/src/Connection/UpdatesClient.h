#pragma once

#include "GrpcClient.h"
#include "Models/ServerConfiguration.h"
#include "Models/Message.h"
#include "updates_api.grpc.pb.h"

#include <QObject>
#include <QThread>
#include <QMutex>
#include <QWaitCondition>
#include <QFuture>
#include <functional>

namespace BarkFluff {

/**
 * @brief Событие нового сообщения
 */
struct NewMessageEvent {
    QString chatId;
    Message message;
};

/**
 * @brief Событие прочтения сообщения
 */
struct MessageReadEvent {
    QString chatId;
    qint64 messageId;
    QList<qint64> newReadBy;
};

/**
 * @brief gRPC клиент для Updates API (streaming)
 */
class UpdatesClient : public QObject, public GrpcClient {
    Q_OBJECT
    
    std::unique_ptr<barkfluff::updates::UpdatesApi::Stub> stub_;
    
    // Streaming state
    std::unique_ptr<grpc::ClientContext> newMessagesContext_;
    std::unique_ptr<grpc::ClientContext> readEventsContext_;
    std::unique_ptr<grpc::ClientReader<barkfluff::updates::NewMessageEvent>> newMessagesReader_;
    std::unique_ptr<grpc::ClientReader<barkfluff::updates::MessageReadEvent>> readEventsReader_;
    
    mutable QMutex mutex_;
    bool running_ = false;
    QString accessToken_;
    
    // Futures for background threads (for proper shutdown)
    QFuture<void> newMessagesFuture_;
    QFuture<void> readEventsFuture_;
    
public:
    explicit UpdatesClient(const ServiceInfo& service, QObject* parent = nullptr);
    ~UpdatesClient() override;
    
    // Start listening for updates (starts background thread)
    void start(const QString& accessToken);
    
    // Stop listening
    void stop();
    
    // Check if connected
    bool isRunning() const;
    
signals:
    // Emitted when a new message is received
    void newMessage(const NewMessageEvent& event);
    
    // Emitted when a message is read
    void messageRead(const MessageReadEvent& event);
    
    // Emitted on connection error
    void connectionError(const QString& error);
    
    // Emitted when connection state changes
    void connectionStateChanged(bool connected);
    
private:
    // Convert proto Message to model Message (reuse from MessagesClient)
    Message convertMessage(const barkfluff::shared::Message& protoMsg);
    
    // Internal streaming methods
    void startNewMessagesStream();
    void startReadEventsStream();
};

} // namespace BarkFluff