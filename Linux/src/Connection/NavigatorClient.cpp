#include "NavigatorClient.h"
#include "Utils/ErrorHandler.h"
#include <QDebug>

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

NavigatorClient::NavigatorClient(const QString& host, int port)
    : GrpcClient(host, port, false)  // No TLS (insecure HTTP/2)
{
    stub_ = barkfluff::navigator::NavigatorApi::NewStub(channel_);
}

QList<ServerInfo> NavigatorClient::listServers() {
    QList<ServerInfo> result;
    
    auto context = createContext();
    barkfluff::navigator::ListServersRequest request;
    barkfluff::navigator::ListServersResponse response;
    
    grpc::Status status = stub_->ListServers(context.get(), request, &response);
    
    if (!status.ok()) {
        throw std::runtime_error(ErrorHandler::handleGrpcError(status).toStdString());
    }
    
    for (const auto& server : response.servers()) {
        ServerInfo info;
        info.name = QString::fromStdString(server.name());
        info.description = QString::fromStdString(server.description());
        info.accountsCount = server.accounts_count();
        info.beaconUri.host = stripUrlScheme(QString::fromStdString(server.beacon_uri().host()));
        info.beaconUri.port = server.beacon_uri().port();
        info.serverPublicName = QString::fromStdString(server.server_public_name());
        info.location = QString::fromStdString(server.location());
        
        // Debug output
        qDebug("Server: %s, Beacon: %s:%d", 
               info.name.toUtf8().constData(),
               info.beaconUri.host.toUtf8().constData(),
               info.beaconUri.port);
        
        if (server.has_color()) {
            info.color.liteHex = QString::fromStdString(server.color().lite_hex());
            info.color.mainHex = QString::fromStdString(server.color().main_hex());
            info.color.hardHex = QString::fromStdString(server.color().hard_hex());
        }
        
        result.append(info);
    }
    
    return result;
}

} // namespace BarkFluff