[← Documentation hub](../README.md)

# Android

The supported Android client is **V1**, implemented with Kotlin, Views/XML, and gRPC-OkHttp. Android V2 is a Compose experiment and is not part of normal development work.

## Requirements

- Android Studio or a JDK 17+ installation.
- Android SDK platform 36.

The Android projects share one Gradle root in `Android/`: `:core`, `:app-v1`, and `:app-v2`.

## Build V1

```bash
cd Android
./gradlew :app-v1:assembleDebug
```

The debug APK is written below `Android/Barkfluff.Client.Android/app/build/outputs/apk/debug/`.

## Build every Android module

Use this only when work affects shared code or the experimental V2 project:

```bash
cd Android
./gradlew :core:assembleDebug :app-v1:assembleDebug :app-v2:assembleDebug
```

## Notes

- V1 targets Android API 36 and has a minimum SDK of 31.
- V2 has a minimum SDK of 35 and remains experimental.
- See the [Android knowledge-base page](../../Obsidian/ClaudeVault/Клиенты/Android.md) for architecture details.
