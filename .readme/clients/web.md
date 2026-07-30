[← Documentation hub](../README.md)

<p align="center">
  <img src="../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Web</h1>

<p align="center">
  <strong>Generate browser bundles and build the gRPC-Web messenger host.</strong>
</p>

<p align="center">
  <a href="../../README.md">Overview</a> ·
  <a href="#requirements">Requirements</a> ·
  <a href="#generate-browser-bundles">Generate</a> ·
  <a href="#build-the-host">Build</a>
</p>

---

The active web client is a vanilla-JavaScript SPA hosted by `Backend/BarkFluff.Web`. The separate `Frontend/Developers` project is the developer portal, not the messenger client.

## Requirements

- Node.js and npm for the JavaScript bundles.
- `protoc-gen-grpc-web` on `PATH` when generating protobuf code on Linux or macOS.
- PowerShell on Windows, or Bash on Linux/macOS.

## Generate browser bundles

```bash
cd Backend/BarkFluff.Web/scripts
npm install
cd ..
bash scripts/generate-proto.sh
bash scripts/vendor-livekit.sh
```

On Windows, replace the last two commands with:

```powershell
pwsh scripts/generate-proto.ps1
pwsh scripts/vendor-livekit.ps1
```

The generated bundles are committed under `wwwroot/js/proto/` and `wwwroot/js/vendor/`. Keep them in sync with the scripts and Docker build whenever a protobuf contract or the LiveKit dependency changes.

## Build the host

```bash
dotnet build Backend/BarkFluff.Web/BarkFluff.Web.csproj
```

For details on the browser architecture, auth metadata, and real-time updates, see [Web in the knowledge base](../../Obsidian/ClaudeVault/Клиенты/Web.md).
