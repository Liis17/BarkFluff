#pragma once

#include <grpcpp/grpcpp.h>
#include <memory>
#include <QString>
#include "Models/DeviceMetadata.h"
#include "Utils/SystemInfo.h"

namespace BarkFluff {

class GrpcClient {
protected:
    std::shared_ptr<grpc::Channel> channel_;
    DeviceMetadata deviceMetadata_;
    
    std::unique_ptr<grpc::ClientContext> createContext(const QString& accessToken = QString());
    
    /// Create context without deadline (for server streaming)
    std::unique_ptr<grpc::ClientContext> createContextNoDeadline(const QString& accessToken = QString());
    
public:
    GrpcClient(const QString& host, int port, bool tls = true, bool skipTlsVerify = false);
    virtual ~GrpcClient() = default;
    
    bool isConnected() const;
};

} // namespace BarkFluff