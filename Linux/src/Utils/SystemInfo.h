#pragma once

#include "Models/DeviceMetadata.h"

namespace BarkFluff {

class SystemInfo {
public:
    static DeviceMetadata getDeviceMetadata();
    static QString getOsInfo();
    static QString getLocalIp();
    static QString getDeviceName();
};

} // namespace BarkFluff