#pragma once

#include "GrpcClient.h"
#include "Models/ServerConfiguration.h"
#include "beacon_api.grpc.pb.h"

namespace BarkFluff {

class BeaconClient : public GrpcClient {
    std::unique_ptr<barkfluff::beacon::BeaconApi::Stub> stub_;
    
public:
    BeaconClient(const QString& host, int port, bool tls = true, bool skipTlsVerify = true);
    
    ServerConfiguration getServerInfo();
};

} // namespace BarkFluff