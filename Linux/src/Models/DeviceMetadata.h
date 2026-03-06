#pragma once

#include <QString>

namespace BarkFluff {

struct DeviceMetadata {
    QString deviceId;
    QString deviceName;
    QString os;
    QString ip;
    QString appName = "BarkFluff.Qt";
    QString appVersion = "1.0.0";
};

} // namespace BarkFluff