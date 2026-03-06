#pragma once

#include <QString>
#include <QColor>

namespace BarkFluff {

struct ServerColor {
    QString liteHex;
    QString mainHex;
    QString hardHex;
    
    QColor lite() const { return QColor(liteHex); }
    QColor main() const { return QColor(mainHex); }
    QColor hard() const { return QColor(hardHex); }
};

struct ServiceEndpoint {
    QString host;
    int port = 0;
    
    QString toString() const {
        return QString("%1:%2").arg(host).arg(port);
    }
};

struct ServerInfo {
    QString name;
    QString description;
    qint64 accountsCount = 0;
    ServiceEndpoint beaconUri;
    QString serverPublicName;
    QString location;
    ServerColor color;
};

} // namespace BarkFluff