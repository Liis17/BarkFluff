#include "UpdatesClient.h"
#include "Utils/ErrorHandler.h"

#include <QDateTime>
#include <QDebug>
#include <QtConcurrent>

namespace BarkFluff {

UpdatesClient::UpdatesClient(const ServiceInfo& service, QObject* parent)
    : QObject(parent)
    , GrpcClient(service.endpoint.host, service.endpoint.port, service.tlsEnabled)
{
    stub_ = barkfluff::updates::UpdatesApi::NewStub(channel_);
}

UpdatesClient::~UpdatesClient() {
    stop();
}

void UpdatesClient::start(const QString& accessToken) {
    QMutexLocker locker(&mutex_);
    
    if (running_) {
        return;
    }
    
    accessToken_ = accessToken;
    running_ = true;
    
    emit connectionStateChanged(true);
    
    // Start streams in background threads using QtConcurrent
    // Store futures for proper shutdown
    newMessagesFuture_ = QtConcurrent::run([this]() {
        startNewMessagesStream();
    });
    
    readEventsFuture_ = QtConcurrent::run([this]() {
        startReadEventsStream();
    });
}

void UpdatesClient::stop() {
    {
        QMutexLocker locker(&mutex_);
        
        if (!running_) {
            return;
        }
        
        running_ = false;
        
        // Cancel the contexts to abort the streams
        if (newMessagesContext_) {
            newMessagesContext_->TryCancel();
        }
        if (readEventsContext_) {
            readEventsContext_->TryCancel();
        }
    }
    // Mutex unlocked here - streams can now check running_ and exit
    
    // Wait for background threads to finish (prevents segfault)
    if (newMessagesFuture_.isRunning()) {
        newMessagesFuture_.waitForFinished();
    }
    if (readEventsFuture_.isRunning()) {
        readEventsFuture_.waitForFinished();
    }
    
    emit connectionStateChanged(false);
}

bool UpdatesClient::isRunning() const {
    QMutexLocker locker(&mutex_);
    return running_;
}

void UpdatesClient::startNewMessagesStream() {
    newMessagesContext_ = createContextNoDeadline(accessToken_);
    barkfluff::updates::SubscribeNewMessagesRequest request;
    
    newMessagesReader_ = stub_->SubscribeNewMessages(newMessagesContext_.get(), request);
    
    barkfluff::updates::NewMessageEvent event;
    while (true) {
        {
            QMutexLocker locker(&mutex_);
            if (!running_) break;
        }
        
        if (!newMessagesReader_->Read(&event)) {
            // Stream ended or error
            grpc::Status status = newMessagesReader_->Finish();
            if (!status.ok() && running_) {
                qWarning() << "UpdatesClient::newMessages error:" << QString::fromStdString(status.error_message());
                emit connectionError(QString::fromStdString(status.error_message()));
            }
            break;
        }
        
        // Convert and emit signal
        NewMessageEvent qtEvent;
        qtEvent.chatId = QString::fromStdString(event.chat_id());
        qtEvent.message = convertMessage(event.message());
        
        emit newMessage(qtEvent);
    }
    
    newMessagesReader_.reset();
    newMessagesContext_.reset();
}

void UpdatesClient::startReadEventsStream() {
    readEventsContext_ = createContextNoDeadline(accessToken_);
    barkfluff::updates::SubscribeMessagesReadRequest request;
    
    readEventsReader_ = stub_->SubscribeMessagesRead(readEventsContext_.get(), request);
    
    barkfluff::updates::MessageReadEvent event;
    while (true) {
        {
            QMutexLocker locker(&mutex_);
            if (!running_) break;
        }
        
        if (!readEventsReader_->Read(&event)) {
            // Stream ended or error
            grpc::Status status = readEventsReader_->Finish();
            if (!status.ok() && running_) {
                qWarning() << "UpdatesClient::readEvents error:" << QString::fromStdString(status.error_message());
                emit connectionError(QString::fromStdString(status.error_message()));
            }
            break;
        }
        
        // Convert and emit signal
        MessageReadEvent qtEvent;
        qtEvent.chatId = QString::fromStdString(event.chat_id());
        qtEvent.messageId = event.message_id();
        
        for (auto uid : event.new_read_by()) {
            qtEvent.newReadBy.append(uid);
        }
        
        emit messageRead(qtEvent);
    }
    
    readEventsReader_.reset();
    readEventsContext_.reset();
}

Message UpdatesClient::convertMessage(const barkfluff::shared::Message& protoMsg) {
    Message msg;
    msg.id = protoMsg.id();
    msg.senderId = protoMsg.sender_id();
    
    // Convert timestamp from protobuf
    const auto& timestamp = protoMsg.sent_at();
    qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
    msg.sentAt = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
    
    // Convert read_by
    for (auto uid : protoMsg.read_by()) {
        msg.readBy.append(uid);
    }
    
    // Convert content
    const auto& protoContent = protoMsg.content();
    msg.content.text = QString::fromStdString(protoContent.text());
    
    for (const auto& protoAtt : protoContent.attachments()) {
        MessageAttachment att;
        att.id = protoAtt.id();
        att.type = static_cast<MessageAttachmentType>(protoAtt.type());
        att.fileId = QString::fromStdString(protoAtt.file_id());
        att.previewUrl = QString::fromStdString(protoAtt.preview_url());
        att.attachmentSize = protoAtt.attachment_size();
        att.previewFileId = QString::fromStdString(protoAtt.preview_file_id());
        att.fileName = QString::fromStdString(protoAtt.file_name());
        msg.content.attachments.append(att);
    }
    
    // Convert message type
    msg.content.type = static_cast<MessageContentType>(protoMsg.type());
    
    return msg;
}

} // namespace BarkFluff