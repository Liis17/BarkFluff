#pragma once

#include <QString>

namespace BarkFluff {

class DeviceIdGenerator {
public:
    static QString getOrCreateDeviceId();
    static void resetDeviceId();
    
private:
    static constexpr const char* CONFIG_KEY = "device/id";
};

} // namespace BarkFluff