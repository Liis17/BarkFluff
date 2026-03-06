#pragma once

#include "GrpcClient.h"
#include "Models/ServerConfiguration.h"
#include "Models/User.h"
#include "users_api.grpc.pb.h"

namespace BarkFluff {

/**
 * @brief Результат пагинации для пользователей
 */
struct PagedUsersResult {
    QList<User> users;
    qint32 totalCount = 0;
    bool hasMore = false;
};

class UsersClient : public GrpcClient {
    std::unique_ptr<barkfluff::users::UsersApi::Stub> stub_;
    
public:
    UsersClient(const ServiceInfo& service);
    
    // Get user info by ID
    User getUser(qint64 userId, const QString& accessToken);
    
    // Check if username exists
    bool checkExistUsername(const QString& username);
    
    // Check if email exists
    bool checkExistEmail(const QString& email);
    
    // Change bio (requires authorization)
    void changeBio(const QString& bio, const QString& accessToken);
    
    // Change name (requires authorization)
    void changeName(const QString& firstName, const QString& lastName, const QString& accessToken);
    
    // Change username (requires authorization)
    void changeUsername(const QString& username, const QString& accessToken);
    
    // Set profile picture (requires authorization)
    void setProfilePicture(const QString& fileId, const QString& accessToken);
    
    // Search users by name/username (requires authorization)
    PagedUsersResult searchUsers(const QString& query, const QString& accessToken, qint32 offset = 0, qint32 size = 20);
    
    // Get user badges
    QList<UserBadge> getUserBadges(qint64 userId, const QString& accessToken, std::optional<qint32> limit = std::nullopt);
    
private:
    User convertUser(const barkfluff::users::User& protoUser);
    Badge convertBadge(const barkfluff::users::Badge& protoBadge);
    UserBadge convertUserBadge(const barkfluff::users::UserBadge& protoUserBadge);
};

} // namespace BarkFluff
