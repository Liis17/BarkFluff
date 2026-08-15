[← Documentation hub](../README.md)

<p align="center">
  <img src="../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Windows</h1>

<p align="center">
  <strong>Build the primary WinUI 3 client for the BarkFluff platform.</strong>
</p>

<p align="center">
  <a href="../../README.md">Overview</a> ·
  <a href="#requirements">Requirements</a> ·
  <a href="#build">Build</a>
</p>

---

The primary Windows client is a WinUI 3 application using Windows App SDK, targeting `net10.0-windows10.0.26100.0` on x64. The former WPF client is legacy and is no longer the primary Windows client.

## Requirements

- Windows.
- .NET SDK **10.0.110** (the repository-pinned version).
- Visual Studio with the .NET desktop development workload is recommended for local debugging.

## Build

```powershell
dotnet build Windows/BarkFluff.Client.WinUI/BarkFluff.Client.WinUI.csproj -p:Platform=x64
```

To open the complete solution:

```powershell
start BarkFluff.sln
```

The solution also includes the legacy WPF client, the DB editor, and the updater CLI. The command above builds the supported primary client only.

For internal structure and runtime behaviour, see [Windows WinUI in the knowledge base](../../Obsidian/ClaudeVault/Клиенты/Windows-WinUI.md). The legacy [Windows WPF client](../../Obsidian/ClaudeVault/Клиенты/Windows-WPF.md) is retained as a predecessor reference.
