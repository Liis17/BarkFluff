#pragma once

#include "framework.h"
#include "Barkfluff.Installer.CPP.h"

// Shortcut creation service
class ShortcutService
{
public:
    // Create Start Menu shortcut
    bool CreateStartMenuShortcut(const std::wstring& targetPath);

    // Remove Start Menu shortcut
    bool RemoveStartMenuShortcut();

    // Get shortcut target path
    std::wstring GetShortcutTargetPath(const std::wstring& shortcutPath);

    // Get last error
    const std::wstring& GetLastError() const { return m_lastError; }

private:
    std::wstring m_lastError;

    std::wstring NormalizePath(const std::wstring& path);
    bool CreateShortcut(const std::wstring& shortcutPath, const std::wstring& targetPath, const std::wstring& appUserModelId);
};
