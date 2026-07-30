[← Documentation hub](../README.md)

<p align="center">
  <img src="../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">macOS</h1>

<p align="center">
  <strong>Build the native SwiftUI client and its shared Swift packages.</strong>
</p>

<p align="center">
  <a href="../../README.md">Overview</a> ·
  <a href="#requirements">Requirements</a> ·
  <a href="#build">Build</a>
</p>

---

The macOS client is a SwiftUI application with local Swift packages for shared core, networking, protobuf, and calls code.

## Requirements

- macOS with a current Xcode installation.
- Network access on the first build so Swift Package Manager can resolve dependencies.

## Build

```bash
cd Mac/Barkfluff
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build
```

## Open in Xcode

```bash
open Mac/Barkfluff/Barkfluff.xcodeproj
```

The first command may take longer because Xcode resolves Swift packages. More implementation detail is available in the [macOS knowledge-base page](../../Obsidian/ClaudeVault/Клиенты/macOS.md).
