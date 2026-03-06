#pragma once

#include "GrpcClient.h"
#include "Models/ServerInfo.h"
#include "navigator_api.grpc.pb.h"
#include <QList>

namespace BarkFluff {

class NavigatorClient : public GrpcClient {
    std::unique_ptr<barkfluff::navigator::NavigatorApi::Stub> stub_;
    
public:
    // Default navigator address
    static constexpr const char* DEFAULT_HOST = "navigator.barkfluff.com";
    static constexpr int DEFAULT_PORT = 64646;
    
    NavigatorClient(const QString& host = DEFAULT_HOST, int port = DEFAULT_PORT);
    
    QList<ServerInfo> listServers();
};

} // namespace BarkFluff