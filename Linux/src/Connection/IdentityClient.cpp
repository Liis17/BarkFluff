#include "IdentityClient.h"
#include "Utils/ErrorHandler.h"
#include <QDateTime>

namespace BarkFluff {

IdentityClient::IdentityClient(const ServiceInfo& service)
    : GrpcClient(service.endpoint.host, service.endpoint.port, service.tlsEnabled)
{
    stub_ = barkfluff::identity::IdentityApi::NewStub(channel_);
}

Token IdentityClient::convertToken(const barkfluff::identity::Token& proto) {
    Token token;
    token.value = QString::fromStdString(proto.value());
    
    if (proto.has_expiration_date()) {
        auto timestamp = proto.expiration_date();
        token.expirationDate = QDateTime::fromSecsSinceEpoch(timestamp.seconds(), Qt::UTC);
    }
    
    return token;
}

AuthResult IdentityClient::auth(const QString& login, const QString& password, const QString& otpCode) {
    auto context = createContext();
    
    barkfluff::identity::AuthRequest request;
    
    // Determine if login is email or username
    if (login.contains('@')) {
        request.set_email(login.toStdString());
    } else {
        request.set_username(login.toStdString());
    }
    
    request.set_password(password.toStdString());
    
    if (!otpCode.isEmpty()) {
        request.set_otp_code(otpCode.toStdString());
    }
    
    barkfluff::identity::AuthResponse response;
    grpc::Status status = stub_->Auth(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    AuthResult result;
    if (response.has_access_token()) {
        result.accessToken = convertToken(response.access_token());
    }
    if (response.has_refresh_token()) {
        result.refreshToken = convertToken(response.refresh_token());
    }
    
    return result;
}

AuthResult IdentityClient::fastAuth(const QString& fastAuthId) {
    auto context = createContext();
    
    barkfluff::identity::FastAuthRequest request;
    request.set_fast_auth_id(fastAuthId.toStdString());
    
    barkfluff::identity::AuthResponse response;
    grpc::Status status = stub_->FastAuth(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    AuthResult result;
    if (response.has_access_token()) {
        result.accessToken = convertToken(response.access_token());
    }
    if (response.has_refresh_token()) {
        result.refreshToken = convertToken(response.refresh_token());
    }
    
    return result;
}

Token IdentityClient::createToken(const QString& refreshToken) {
    auto context = createContext();
    
    barkfluff::identity::CreateTokenRequest request;
    request.set_refresh_token(refreshToken.toStdString());
    
    barkfluff::identity::CreateTokenResponse response;
    grpc::Status status = stub_->CreateToken(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    return convertToken(response.access_token());
}

CreateAccountResult IdentityClient::createAccount(const QString& firstName, const QString& lastName,
                                                   const QString& username, const QString& email) {
    auto context = createContext();
    
    barkfluff::identity::CreateAccountRequest request;
    request.set_first_name(firstName.toStdString());
    request.set_last_name(lastName.toStdString());
    request.set_username(username.toStdString());
    request.set_email(email.toStdString());
    
    barkfluff::identity::CreateAccountResponse response;
    grpc::Status status = stub_->CreateAccount(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    CreateAccountResult result;
    result.codeId = QString::fromStdString(response.code_id());
    return result;
}

ConfirmAccountResult IdentityClient::confirmAccount(const QString& codeId, const QString& codeValue) {
    auto context = createContext();
    
    barkfluff::identity::ConfirmAccountRequest request;
    request.set_code_id(codeId.toStdString());
    request.set_code_value(codeValue.toStdString());
    
    barkfluff::identity::ConfirmAccountResponse response;
    grpc::Status status = stub_->ConfirmAccount(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    ConfirmAccountResult result;
    if (response.has_refresh_token()) {
        result.refreshToken = convertToken(response.refresh_token());
    }
    return result;
}

QString IdentityClient::resetPassword(const QString& login) {
    auto context = createContext();
    
    barkfluff::identity::ResetPasswordRequest request;
    
    if (login.contains('@')) {
        request.set_email(login.toStdString());
    } else {
        request.set_username(login.toStdString());
    }
    
    barkfluff::identity::ResetPasswordResponse response;
    grpc::Status status = stub_->ResetPassword(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    return QString::fromStdString(response.reset_id());
}

AuthResult IdentityClient::confirmResetPassword(const QString& resetId, const QString& otpCode) {
    auto context = createContext();
    
    barkfluff::identity::ConfirmResetPasswordRequest request;
    request.set_reset_id(resetId.toStdString());
    request.set_otp_code(otpCode.toStdString());
    
    barkfluff::identity::ConfirmResetPasswordResponse response;
    grpc::Status status = stub_->ConfirmResetPassword(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    AuthResult result;
    if (response.has_access_token()) {
        result.accessToken = convertToken(response.access_token());
    }
    if (response.has_refresh_token()) {
        result.refreshToken = convertToken(response.refresh_token());
    }
    
    return result;
}

void IdentityClient::setPassword(const QString& password, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::SetPasswordRequest request;
    request.set_password(password.toStdString());
    
    barkfluff::identity::SetPasswordResponse response;
    grpc::Status status = stub_->SetPassword(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

OTPSetupInfo IdentityClient::enableOTP(const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::EnableOtpVerificationRequest request;
    request.set_otp_type(barkfluff::identity::OtpTypeId::Authenticator);
    
    barkfluff::identity::EnableOtpVerificationResponse response;
    grpc::Status status = stub_->EnableOtpVerification(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    OTPSetupInfo info;
    info.qrCodeBase64 = QString::fromStdString(response.otp_qr());
    info.otpCode = QString::fromStdString(response.otp_code());
    
    return info;
}

void IdentityClient::confirmOTP(const QString& otpCode, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::ConfirmOtpVerificationRequest request;
    request.set_otp_code(otpCode.toStdString());
    
    barkfluff::identity::ConfirmOtpVerificationResponse response;
    grpc::Status status = stub_->ConfirmOtpVerification(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

OTPStatus IdentityClient::getOTPStatus(const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::ListOtpVerificationRequest request;
    barkfluff::identity::ListOtpVerificationResponse response;
    grpc::Status status = stub_->ListOtpVerification(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    OTPStatus result;
    result.authenticatorEnabled = response.authenticator_enabled();
    result.emailEnabled = response.email_enabled();
    
    return result;
}

void IdentityClient::disableOTP(OTPType otpType, const QString& otpCode, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::DisableOtpVerificationRequest request;
    
    switch (otpType) {
        case OTPType::Authenticator:
            request.set_otp_type(barkfluff::identity::OtpTypeId::Authenticator);
            break;
        case OTPType::Email:
            request.set_otp_type(barkfluff::identity::OtpTypeId::Email);
            break;
        default:
            request.set_otp_type(barkfluff::identity::OtpTypeId::Unknown);
    }
    
    if (!otpCode.isEmpty()) {
        request.set_otp_code(otpCode.toStdString());
    }
    
    barkfluff::identity::DisableOtpVerificationResponse response;
    grpc::Status status = stub_->DisableOtpVerification(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

QList<SessionInfo> IdentityClient::getActiveSessions(const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::GetActiveSessionsRequest request;
    barkfluff::identity::GetActiveSessionsResponse response;
    grpc::Status status = stub_->GetActiveSessions(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    QList<SessionInfo> sessions;
    for (const auto& session : response.sessions()) {
        sessions.append(convertSession(session));
    }
    
    return sessions;
}

void IdentityClient::removeActiveSession(const QString& deviceId, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::RemoveActiveSessionRequest request;
    request.set_device_id(deviceId.toStdString());
    
    barkfluff::identity::RemoveActiveSessionResponse response;
    grpc::Status status = stub_->RemoveActiveSession(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

void IdentityClient::changePassword(const QString& oldPassword, const QString& newPassword, const QString& accessToken) {
    auto context = createContext(accessToken);
    
    barkfluff::identity::SetPasswordRequest request;
    request.set_password(newPassword.toStdString());
    request.set_old_password(oldPassword.toStdString());
    
    barkfluff::identity::SetPasswordResponse response;
    grpc::Status status = stub_->SetPassword(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
}

SessionInfo IdentityClient::convertSession(const barkfluff::identity::GetActiveSessionsResponse::Session& proto) {
    SessionInfo session;
    session.id = proto.id();
    session.deviceId = QString::fromStdString(proto.device_id());
    session.originalName = QString::fromStdString(proto.original_name());
    session.customName = QString::fromStdString(proto.custom_name());
    session.appName = QString::fromStdString(proto.app_name());
    session.operationSystem = QString::fromStdString(proto.operation_system());
    session.location = QString::fromStdString(proto.location());
    
    if (proto.has_created_at()) {
        auto timestamp = proto.created_at();
        session.createdAt = QDateTime::fromSecsSinceEpoch(timestamp.seconds(), Qt::UTC);
    }
    
    if (proto.has_expiration_at()) {
        auto timestamp = proto.expiration_at();
        session.expirationAt = QDateTime::fromSecsSinceEpoch(timestamp.seconds(), Qt::UTC);
    }
    
    return session;
}

} // namespace BarkFluff
