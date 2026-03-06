#pragma once

#include <grpcpp/grpcpp.h>
#include <QString>

namespace BarkFluff {

class ErrorHandler {
public:
    static QString handleGrpcError(const grpc::Status& status);
    static QString connectionError();
    static QString timeoutError();
};

} // namespace BarkFluff