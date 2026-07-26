[← Documentation hub](../README.md)

# macOS

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
