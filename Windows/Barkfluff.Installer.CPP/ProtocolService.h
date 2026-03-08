#pragma once

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"

// Protocol registration service (bf://)
class ProtocolService
{
public:
    // Register protocol handler
    bool RegisterProtocol(const std::wstring& exePath);

    // Unregister protocol handler
    bool UnregisterProtocol();

    // Check if protocol is registered
    bool IsProtocolRegistered();

    // Get registered protocol path
    std::wstring GetRegisteredPath();

    // Get last error
    const std::wstring& GetLastError() const { return m_lastError; }

private:
    std::wstring m_lastError;

    std::wstring NormalizePath(const std::wstring& path);
    std::wstring ExtractPathFromCommand(const std::wstring& command);
};
