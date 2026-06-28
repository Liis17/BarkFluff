#include "ErrorHandler.h"
#include <QCoreApplication>

namespace BarkFluff {

QString ErrorHandler::handleGrpcError(const grpc::Status& status) {
    using SC = grpc::StatusCode;
    
    switch (status.error_code()) {
        case SC::UNAUTHENTICATED:
            return QCoreApplication::translate("ErrorHandler", "Invalid credentials or token expired");
        case SC::PERMISSION_DENIED:
            return QCoreApplication::translate("ErrorHandler", "Permission denied");
        case SC::NOT_FOUND:
            return QCoreApplication::translate("ErrorHandler", "Resource not found");
        case SC::ALREADY_EXISTS:
            return QCoreApplication::translate("ErrorHandler", "Already exists");
        case SC::INVALID_ARGUMENT:
            return QCoreApplication::translate("ErrorHandler", "Invalid data: %1")
                .arg(QString::fromStdString(status.error_message()));
        case SC::FAILED_PRECONDITION:
            // Бизнес-ошибки сервера (BaseGrpcException) приходят как FAILED_PRECONDITION
            // с осмысленным текстом — показываем его напрямую.
            // Сюда попадает и UsernameInvalidFormatException (x-error-code E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47).
            return QString::fromStdString(status.error_message());
        case SC::INTERNAL:
            return QCoreApplication::translate("ErrorHandler", "Server error. Please try again later");
        case SC::UNAVAILABLE:
            return QCoreApplication::translate("ErrorHandler", "Server is unavailable. Check your connection");
        case SC::DEADLINE_EXCEEDED:
            return QCoreApplication::translate("ErrorHandler", "Request timeout");
        case SC::CANCELLED:
            return QCoreApplication::translate("ErrorHandler", "Request cancelled");
        default:
            return QCoreApplication::translate("ErrorHandler", "Error: %1")
                .arg(QString::fromStdString(status.error_message()));
    }
}

QString ErrorHandler::connectionError() {
    return QCoreApplication::translate("ErrorHandler", "Connection error. Check your internet connection");
}

QString ErrorHandler::timeoutError() {
    return QCoreApplication::translate("ErrorHandler", "Request timeout. Please try again");
}

} // namespace BarkFluff