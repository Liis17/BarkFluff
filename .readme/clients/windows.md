[← Documentation hub](../README.md)

<p align="center">
  <img src="../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Windows</h1>

<p align="center">
  <strong>Build the primary WPF client for the BarkFluff platform.</strong>
</p>

<p align="center">
  <a href="../../README.md">Overview</a> ·
  <a href="#requirements">Requirements</a> ·
  <a href="#build">Build</a>
</p>

---

The primary Windows client is a WPF application targeting `net10.0-windows10.0.26100.0` on x64.

## Requirements

- Windows.
- .NET SDK **10.0.110** (the repository-pinned version).
- Visual Studio with the .NET desktop development workload is recommended for local debugging.

## Build

```powershell
dotnet build Windows/BarkFluff.Client.WPF/BarkFluff.Client.WPF.csproj
```

To open the complete solution:

```powershell
start BarkFluff.sln
```

The solution also includes the DB editor, updater CLI, and a separate V2 WPF project. The command above builds the supported primary client only.

For internal structure and runtime behaviour, see [Windows WPF in the knowledge base](../../Obsidian/ClaudeVault/Клиенты/Windows-WPF.md).
