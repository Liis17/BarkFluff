#include "SystemInfo.h"
#include "DeviceIdGenerator.h"
#include <QSysInfo>
#include <QNetworkInterface>
#include <QHostAddress>

namespace BarkFluff {

DeviceMetadata SystemInfo::getDeviceMetadata() {
    DeviceMetadata meta;
    meta.deviceId = DeviceIdGenerator::getOrCreateDeviceId();
    meta.deviceName = getDeviceName();
    meta.os = getOsInfo();
    meta.ip = getLocalIp();
    return meta;
}

QString SystemInfo::getOsInfo() {
    QString desktop = qgetenv("XDG_CURRENT_DESKTOP");
    QString product = QSysInfo::prettyProductName();
    
    if (!desktop.isEmpty()) {
        return QString("%1 (%2)").arg(product, desktop);
    }
    return product;
}

QString SystemInfo::getLocalIp() {
    const auto interfaces = QNetworkInterface::allInterfaces();
    
    for (const auto& iface : interfaces) {
        // Skip loopback and down interfaces
        if (!(iface.flags() & QNetworkInterface::IsUp) ||
            (iface.flags() & QNetworkInterface::IsLoopBack)) {
            continue;
        }
        
        for (const auto& addr : iface.addressEntries()) {
            if (addr.ip().protocol() == QAbstractSocket::IPv4Protocol &&
                !addr.ip().isLoopback()) {
                return addr.ip().toString();
            }
        }
    }
    
    return "127.0.0.1";
}

QString SystemInfo::getDeviceName() {
    return QSysInfo::machineHostName();
}

} // namespace BarkFluff