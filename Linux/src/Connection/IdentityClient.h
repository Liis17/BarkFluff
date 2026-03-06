#pragma once

#include "GrpcClient.h"
#include "Models/UserSession.h"
#include "Models/ServerConfiguration.h"
#include "Models/SessionInfo.h"
#include "identity_api.grpc.pb.h"

namespace BarkFluff {

// OTP Setup info for 2FA
struct OTPSetupInfo {
    QString qrCodeBase64;   // Base64 encoded QR code image
    QString otpCode;        // Manual code for setup
};

class IdentityClient : public GrpcClient {
    std::unique_ptr<barkfluff::identity::IdentityApi::Stub> stub_;
    
public:
    IdentityClient(const ServiceInfo& service);
    
    // Authentication
    AuthResult auth(const QString& login, const QString& password, const QString& otpCode = QString());
    AuthResult fastAuth(const QString& fastAuthId);
    Token createToken(const QString& refreshToken);
    
    // Registration
    CreateAccountResult createAccount(const QString& firstName, const QString& lastName, 
                                       const QString& username, const QString& email);
    ConfirmAccountResult confirmAccount(const QString& codeId, const QString& codeValue);
    
    // Password management
    QString resetPassword(const QString& login);  // Returns reset_id
    AuthResult confirmResetPassword(const QString& resetId, const QString& otpCode);
    void setPassword(const QString& password, const QString& accessToken);
    
    // 2FA management
    OTPSetupInfo enableOTP(const QString& accessToken);
    void confirmOTP(const QString& otpCode, const QString& accessToken);
    OTPStatus getOTPStatus(const QString& accessToken);
    void disableOTP(OTPType otpType, const QString& otpCode, const QString& accessToken);
    
    // Session management
    QList<SessionInfo> getActiveSessions(const QString& accessToken);
    void removeActiveSession(const QString& deviceId, const QString& accessToken);
    
    // Password management (extended)
    void changePassword(const QString& oldPassword, const QString& newPassword, const QString& accessToken);
    
private:
    Token convertToken(const barkfluff::identity::Token& proto);
    SessionInfo convertSession(const barkfluff::identity::GetActiveSessionsResponse::Session& proto);
};

} // namespace BarkFluff
