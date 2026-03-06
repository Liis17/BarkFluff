#pragma once

#include "ServerInfo.h"
#include <QString>

namespace BarkFluff {

enum class ServiceStatus {
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3,
    Offline = 4
};

struct ServiceInfo {
    QString name;
    ServiceEndpoint endpoint;
    bool tlsEnabled = true;
    ServiceStatus status = ServiceStatus::Unknown;
    
    QString statusString() const {
        switch (status) {
            case ServiceStatus::Healthy: return "Healthy";
            case ServiceStatus::Degraded: return "Degraded";
            case ServiceStatus::Unhealthy: return "Unhealthy";
            case ServiceStatus::Offline: return "Offline";
            default: return "Unknown";
        }
    }
};

struct ServerConfiguration {
    QString name;
    QString description;
    ServerColor color;
    
    // Beacon endpoint (for reconnection)
    ServiceEndpoint beaconEndpoint;
    
    // Microservices
    ServiceInfo identity;
    ServiceInfo users;
    ServiceInfo files;
    ServiceInfo messages;
    ServiceInfo updates;
    ServiceInfo onliner;
    
    bool isValid() const {
        return !identity.endpoint.host.isEmpty() && identity.endpoint.port > 0;
    }
};

} // namespace BarkFluff