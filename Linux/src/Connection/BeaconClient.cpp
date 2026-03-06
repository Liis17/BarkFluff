#include "BeaconClient.h"
#include "Utils/ErrorHandler.h"

namespace BarkFluff {

// Helper function to strip URL scheme (https://, http://) from host
static QString stripUrlScheme(const QString& url) {
    QString result = url;
    if (result.startsWith("https://", Qt::CaseInsensitive)) {
        result = result.mid(8);
    } else if (result.startsWith("http://", Qt::CaseInsensitive)) {
        result = result.mid(7);
    }
    // Remove trailing slash if present
    if (result.endsWith('/')) {
        result.chop(1);
    }
    return result;
}

BeaconClient::BeaconClient(const QString& host, int port, bool tls, bool skipTlsVerify)
    : GrpcClient(host, port, tls, skipTlsVerify)
{
    stub_ = barkfluff::beacon::BeaconApi::NewStub(channel_);
}

ServerConfiguration BeaconClient::getServerInfo() {
    ServerConfiguration config;
    
    auto context = createContext();
    barkfluff::beacon::GetServerInfoRequest request;
    barkfluff::beacon::GetServerInfoResponse response;
    
    grpc::Status status = stub_->GetServerInfo(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    config.name = QString::fromStdString(response.name());
    config.description = QString::fromStdString(response.description());
    
    if (response.has_color()) {
        config.color.liteHex = QString::fromStdString(response.color().lite_hex());
        config.color.mainHex = QString::fromStdString(response.color().main_hex());
        config.color.hardHex = QString::fromStdString(response.color().hard_hex());
    }
    
    // Helper lambda to convert Service (strips URL scheme from host)
    auto convertService = [](const barkfluff::beacon::Service& proto) -> ServiceInfo {
        ServiceInfo info;
        info.name = QString::fromStdString(proto.name());
        info.endpoint.host = stripUrlScheme(QString::fromStdString(proto.endpoint().host()));
        info.endpoint.port = proto.endpoint().port();
        info.tlsEnabled = proto.tls_enabled();
        info.status = static_cast<ServiceStatus>(proto.status());
        return info;
    };
    
    if (response.has_identity()) {
        config.identity = convertService(response.identity());
    }
    if (response.has_users()) {
        config.users = convertService(response.users());
    }
    if (response.has_files()) {
        config.files = convertService(response.files());
    }
    if (response.has_messages()) {
        config.messages = convertService(response.messages());
    }
    if (response.has_updates()) {
        config.updates = convertService(response.updates());
    }
    if (response.has_onliner()) {
        config.onliner = convertService(response.onliner());
    }
    
    return config;
}

} // namespace BarkFluff