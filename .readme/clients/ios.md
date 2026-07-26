[← Documentation hub](../README.md)

# iOS

The iOS client is a SwiftUI application that shares local Swift packages with the macOS client.

## Requirements

- macOS with a current Xcode installation.
- An installed iOS simulator or a connected development device.

## Build for a simulator

```bash
cd iOS/Barkfluff
xcodebuild \
  -project Barkfluff.xcodeproj \
  -scheme Barkfluff \
  -destination 'platform=iOS Simulator,name=iPhone 17' \
  build
```

If `iPhone 17` is not available locally, choose an installed simulator in Xcode or replace the destination with one returned by `xcrun simctl list devices available`.

## Open in Xcode

```bash
open iOS/Barkfluff/Barkfluff.xcodeproj
```

See [iOS in the knowledge base](../../Obsidian/ClaudeVault/Клиенты/iOS.md) for features, architecture, and platform-specific behaviour.
