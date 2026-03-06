#include "UsersClient.h"
#include "Utils/ErrorHandler.h"
#include <QDebug>

namespace BarkFluff {

UsersClient::UsersClient(const ServiceInfo& service)
    : GrpcClient(service.endpoint.host, service.endpoint.port, service.tlsEnabled)
{
    stub_ = barkfluff::users::UsersApi::NewStub(channel_);
}

User UsersClient::getUser(qint64 userId, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::users::GetUserRequest request;
    request.set_user_id(userId);
    
    barkfluff::users::GetUserResponse response;
    grpc::Status status = stub_->GetUser(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "UsersClient::getUser error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    return convertUser(response.user());
}

User UsersClient::convertUser(const barkfluff::users::User& protoUser) {
    User user;
    user.id = protoUser.id();
    user.firstName = QString::fromStdString(protoUser.first_name());
    user.lastName = QString::fromStdString(protoUser.last_name());
    user.username = QString::fromStdString(protoUser.username());
    user.profilePicture = QString::fromStdString(protoUser.profile_picture());
    user.profilePicturePreview = QString::fromStdString(protoUser.profile_picture_preview());
    user.bio = QString::fromStdString(protoUser.bio());
    user.storageLimitGb = protoUser.storage_limit_gb();
    
    // Convert timestamp
    const auto& timestamp = protoUser.registration_date();
    qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
    user.registrationDate = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
    
    // Convert badges
    for (const auto& protoBadge : protoUser.badges()) {
        user.badges.append(convertUserBadge(protoBadge));
    }
    
    return user;
}

Badge UsersClient::convertBadge(const barkfluff::users::Badge& protoBadge) {
    Badge badge;
    badge.id = protoBadge.id();
    badge.name = QString::fromStdString(protoBadge.name());
    badge.description = QString::fromStdString(protoBadge.description());
    badge.imageUrl = QString::fromStdString(protoBadge.image_url());
    badge.isActive = protoBadge.is_active();
    
    const auto& timestamp = protoBadge.created_date();
    qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
    badge.createdDate = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
    
    return badge;
}

UserBadge UsersClient::convertUserBadge(const barkfluff::users::UserBadge& protoUserBadge) {
    UserBadge userBadge;
    userBadge.badge = convertBadge(protoUserBadge.badge());
    userBadge.priority = protoUserBadge.priority();
    
    const auto& timestamp = protoUserBadge.assigned_date();
    qint64 millis = timestamp.seconds() * 1000 + timestamp.nanos() / 1000000;
    userBadge.assignedDate = QDateTime::fromMSecsSinceEpoch(millis, Qt::UTC);
    
    return userBadge;
}

bool UsersClient::checkExistUsername(const QString& username) {
    auto context = createContext();
    
    barkfluff::users::CheckExistUsernameRequest request;
    request.set_username(username.toStdString());
    
    barkfluff::users::CheckExistResponse response;
    grpc::Status status = stub_->CheckExistUsername(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    return response.exist();
}

bool UsersClient::checkExistEmail(const QString& email) {
    auto context = createContext();
    
    barkfluff::users::CheckExistEmailRequest request;
    request.set_email(email.toStdString());
    
    barkfluff::users::CheckExistResponse response;
    grpc::Status status = stub_->CheckExistEmail(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    return response.exist();
}

void UsersClient::changeBio(const QString& bio, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::users::ChangeBioRequest request;
    request.set_bio(bio.toStdString());
    
    barkfluff::users::ChangeBioResponse response;
    grpc::Status status = stub_->ChangeBio(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

void UsersClient::changeName(const QString& firstName, const QString& lastName, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::users::ChangeNameRequest request;
    request.set_first_name(firstName.toStdString());
    request.set_last_name(lastName.toStdString());
    
    barkfluff::users::ChangeNameResponse response;
    grpc::Status status = stub_->ChangeName(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

void UsersClient::changeUsername(const QString& username, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::users::ChangeUsernameRequest request;
    request.set_username(username.toStdString());
    
    barkfluff::users::ChangeUsernameResponse response;
    grpc::Status status = stub_->ChangeUsername(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

void UsersClient::setProfilePicture(const QString& fileId, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::users::SetProfilePictureRequest request;
    request.set_file_id(fileId.toStdString());
    
    barkfluff::users::SetProfilePictureResponse response;
    grpc::Status status = stub_->SetProfilePicture(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

PagedUsersResult UsersClient::searchUsers(const QString& query, const QString& accessToken, qint32 offset, qint32 size) {
    auto context = createContext(accessToken);
    
    barkfluff::users::SearchUsersRequest request;
    request.set_query(query.toStdString());
    request.mutable_pagination()->set_offset(offset);
    request.mutable_pagination()->set_size(size);
    
    barkfluff::users::SearchUsersResponse response;
    grpc::Status status = stub_->SearchUsers(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "UsersClient::searchUsers error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    PagedUsersResult result;
    result.totalCount = response.total_count();
    result.hasMore = (offset + response.users_size()) < result.totalCount;
    
    for (const auto& protoUser : response.users()) {
        result.users.append(convertUser(protoUser));
    }
    
    return result;
}

QList<UserBadge> UsersClient::getUserBadges(qint64 userId, const QString& accessToken, std::optional<qint32> limit) {
    auto context = createContext(accessToken);
    
    barkfluff::users::GetUserBadgesRequest request;
    request.set_user_id(userId);
    if (limit.has_value()) {
        request.set_limit(limit.value());
    }
    
    barkfluff::users::GetUserBadgesResponse response;
    grpc::Status status = stub_->GetUserBadges(context.get(), request, &response);
    
    if (!status.ok()) {
        qWarning() << "UsersClient::getUserBadges error:" << QString::fromStdString(status.error_message());
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    QList<UserBadge> badges;
    for (const auto& protoBadge : response.badges()) {
        badges.append(convertUserBadge(protoBadge));
    }
    
    return badges;
}

} // namespace BarkFluff
