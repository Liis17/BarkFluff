#include "GrpcClient.h"
#include <QFile>
#include <QStandardPaths>
#include <QByteArray>
#include <cstdlib>

namespace BarkFluff {

GrpcClient::GrpcClient(const QString& host, int port, bool tls, bool skipTlsVerify) {
    deviceMetadata_ = SystemInfo::getDeviceMetadata();
    
    QString address = QString("%1:%2").arg(host).arg(port);
    
    grpc::ChannelArguments args;
    args.SetMaxReceiveMessageSize(64 * 1024 * 1024);  // 64 MB
    args.SetMaxSendMessageSize(64 * 1024 * 1024);
    
    if (tls) {
        // TLS enabled (HTTPS)
        grpc::SslCredentialsOptions ssl_options;
        
        // Let's Encrypt R12 intermediate certificate
        // Needed for servers that don't send the intermediate chain
        const char* letsEncryptR12 = R"(
-----BEGIN CERTIFICATE-----
MIIFBjCCAu6gAwIBAgIRAMISMktwqbSRcdxA9+KFJjwwDQYJKoZIhvcNAQELBQAw
TzELMAkGA1UEBhMCVVMxKTAnBgNVBAoTIEludGVybmV0IFNlY3VyaXR5IFJlc2Vh
cmNoIEdyb3VwMRUwEwYDVQQDEwxJU1JHIFJvb3QgWDEwHhcNMjQwMzEzMDAwMDAw
WhcNMjcwMzEyMjM1OTU5WjAzMQswCQYDVQQGEwJVUzEWMBQGA1UEChMNTGV0J3Mg
RW5jcnlwdDEMMAoGA1UEAxMDUjEyMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIB
CgKCAQEA2pgodK2+lP474B7i5Ut1qywSf+2nAzJ+Npfs6DGPpRONC5kuHs0BUT1M
5ShuCVUxqqUiXXL0LQfCTUA83wEjuXg39RplMjTmhnGdBO+ECFu9AhqZ66YBAJpz
kG2Pogeg0JfT2kVhgTU9FPnEwF9q3AuWGrCf4yrqvSrWmMebcas7dA8827JgvlpL
Thjp2ypzXIlhZZ7+7Tymy05v5J75AEaz/xlNKmOzjmbGGIVwx1Blbzt05UiDDwhY
XS0jnV6j/ujbAKHS9OMZTfLuevYnnuXNnC2i8n+cF63vEzc50bTILEHWhsDp7CH4
WRt/uTp8n1wBnWIEwii9Cq08yhDsGwIDAQABo4H4MIH1MA4GA1UdDwEB/wQEAwIB
hjAdBgNVHSUEFjAUBggrBgEFBQcDAgYIKwYBBQUHAwEwEgYDVR0TAQH/BAgwBgEB
/wIBADAdBgNVHQ4EFgQUALUp8i2ObzHom0yteD763OkM0dIwHwYDVR0jBBgwFoAU
ebRZ5nu25eQBc4AIiMgaWPbpm24wMgYIKwYBBQUHAQEEJjAkMCIGCCsGAQUFBzAC
hhZodHRwOi8veDEuaS5sZW5jci5vcmcvMBMGA1UdIAQMMAowCAYGZ4EMAQIBMCcG
A1UdHwQgMB4wHKAaoBiGFmh0dHA6Ly94MS5jLmxlbmNyLm9yZy8wDQYJKoZIhvcN
AQELBQADggIBAI910AnPanZIZTKS3rVEyIV29BWEjAK/duuz8eL5boSoVpHhkkv3
4eoAeEiPdZLj5EZ7G2ArIK+gzhTlRQ1q4FKGpPPaFBSpqV/xbUb5UlAXQOnkHn3m
FVj+qYv87/WeY+Bm4sN3Ox8BhyaU7UAQ3LeZ7N1X01xxQe4wIAAE3JVLUCiHmZL+
qoCUtgYIFPgcg350QMUIWgxPXNGEncT921ne7nluI02V8pLUmClqXOsCwULw+PVO
ZCB7qOMxxMBoCUeL2Ll4oMpOSr5pJCpLN3tRA2s6P1KLs9TSrVhOk+7LX28NMUlI
usQ/nxLJID0RhAeFtPjyOCOscQBA53+NRjSCak7P4A5jX7ppmkcJECL+S0i3kXVU
y5Me5BbrU8973jZNv/ax6+ZK6TM8jWmimL6of6OrX7ZU6E2WqazzsFrLG3o2kySb
zlhSgJ81Cl4tv3SbYiYXnJExKQvzf83DYotox3f0fwv7xln1A2ZLplCb0O+l/AK0
YE0DS2FPxSAHi0iwMfW2nNHJrXcY3LLHD77gRgje4Eveubi2xxa+Nmk/hmhLdIET
iVDFanoCrMVIpQ59XWHkzdFmoHXHBV7oibVjGSO7ULSQ7MJ1Nz51phuDJSgAIU7A
0zrLnOrAj/dfrlEWRhCvAgbuwLZX1A2sjNjXoPOHbsPiy+lO1KF8/XY7
-----END CERTIFICATE-----
)";
        
        // Start with Let's Encrypt R12 intermediate certificate
        std::string caCerts = letsEncryptR12;
        
        // Load system CA certificates and append them
        QString caPaths[] = {
            "/etc/ssl/certs/ca-certificates.crt",  // Debian/Ubuntu
            "/etc/pki/tls/certs/ca-bundle.crt",    // RHEL/CentOS
            "/etc/ssl/ca-bundle.pem",               // OpenSUSE
            "/etc/pki/ca-trust/extracted/pem/tls-ca-bundle.pem"  // Fedora
        };
        
        for (const auto& caPath : caPaths) {
            QFile caFile(caPath);
            if (caFile.open(QIODevice::ReadOnly)) {
                QByteArray caData = caFile.readAll();
                caCerts += "\n" + caData.toStdString();
                caFile.close();
                break;
            }
        }
        
        ssl_options.pem_root_certs = caCerts;
        
        // Note: skipTlsVerify flag is kept for API compatibility
        // With Let's Encrypt R12 intermediate cert added, wildcard certs should work
        
        auto creds = grpc::SslCredentials(ssl_options);
        channel_ = grpc::CreateCustomChannel(address.toStdString(), creds, args);
    } else {
        // Insecure (no TLS, for development only)
        channel_ = grpc::CreateCustomChannel(address.toStdString(), 
                                              grpc::InsecureChannelCredentials(), args);
    }
}

std::unique_ptr<grpc::ClientContext> GrpcClient::createContext(const QString& accessToken) {
    auto context = std::make_unique<grpc::ClientContext>();
    
    // Helper lambda to encode string to Base64
    auto toBase64 = [](const QString& str) -> std::string {
        return str.toUtf8().toBase64().toStdString();
    };
    
    // Add device metadata headers (all values must be Base64 encoded)
    context->AddMetadata("x-device-id", toBase64(deviceMetadata_.deviceId));
    context->AddMetadata("x-device-name", toBase64(deviceMetadata_.deviceName));
    context->AddMetadata("x-os-name", toBase64(deviceMetadata_.os));
    context->AddMetadata("x-ip-address", toBase64(deviceMetadata_.ip));
    context->AddMetadata("x-app-name", toBase64(deviceMetadata_.appName));
    context->AddMetadata("x-app-version", toBase64(deviceMetadata_.appVersion));
    
    // Add auth token if provided
    if (!accessToken.isEmpty()) {
        context->AddMetadata("x-auth-token", accessToken.toStdString());
    }
    
    // Set deadline (30 seconds)
    auto deadline = std::chrono::system_clock::now() + std::chrono::seconds(30);
    context->set_deadline(deadline);
    
    return context;
}

std::unique_ptr<grpc::ClientContext> GrpcClient::createContextNoDeadline(const QString& accessToken) {
    auto context = std::make_unique<grpc::ClientContext>();
    
    // Helper lambda to encode string to Base64
    auto toBase64 = [](const QString& str) -> std::string {
        return str.toUtf8().toBase64().toStdString();
    };
    
    // Add device metadata headers (all values must be Base64 encoded)
    context->AddMetadata("x-device-id", toBase64(deviceMetadata_.deviceId));
    context->AddMetadata("x-device-name", toBase64(deviceMetadata_.deviceName));
    context->AddMetadata("x-os-name", toBase64(deviceMetadata_.os));
    context->AddMetadata("x-ip-address", toBase64(deviceMetadata_.ip));
    context->AddMetadata("x-app-name", toBase64(deviceMetadata_.appName));
    context->AddMetadata("x-app-version", toBase64(deviceMetadata_.appVersion));
    
    // Add auth token if provided
    if (!accessToken.isEmpty()) {
        context->AddMetadata("x-auth-token", accessToken.toStdString());
    }
    
    // NO deadline - for server streaming that should run indefinitely
    
    return context;
}

bool GrpcClient::isConnected() const {
    return channel_ && channel_->GetState(false) != GRPC_CHANNEL_SHUTDOWN;
}

} // namespace BarkFluff