#include "DeviceIdGenerator.h"
#include <QUuid>
#include <QSettings>

namespace BarkFluff {

QString DeviceIdGenerator::getOrCreateDeviceId() {
    QSettings settings("BarkFluff", "QtClient");
    QString deviceId = settings.value(CONFIG_KEY).toString();
    
    if (deviceId.isEmpty()) {
        deviceId = QUuid::createUuid().toString(QUuid::WithoutBraces);
        settings.setValue(CONFIG_KEY, deviceId);
        settings.sync();
    }
    
    return deviceId;
}

void DeviceIdGenerator::resetDeviceId() {
    QSettings settings("BarkFluff", "QtClient");
    settings.remove(CONFIG_KEY);
    settings.sync();
}

} // namespace BarkFluff