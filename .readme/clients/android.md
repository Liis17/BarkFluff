[← Documentation hub](../README.md)

<p align="center">
  <img src="../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Android</h1>

<p align="center">
  <strong>Build the supported Kotlin Android client and its shared gRPC foundation.</strong>
</p>

<p align="center">
  <a href="../../README.md">Overview</a> ·
  <a href="#requirements">Requirements</a> ·
  <a href="#build-v1">Build V1</a>
</p>

---

The supported Android client is **V1**, implemented with Kotlin, Views/XML, and gRPC-OkHttp.

## Requirements

- Android Studio or a JDK 17+ installation.
- Android SDK platform 36.

The Android projects share one Gradle root in `Android/`: `:core` and `:app-v1`.

## Build V1

```bash
cd Android
./gradlew :app-v1:assembleDebug
```

The debug APK is written below `Android/Barkfluff.Client.Android/app/build/outputs/apk/debug/`.

## Build every Android module

Use this only when work affects shared code:

```bash
cd Android
./gradlew :core:assembleDebug :app-v1:assembleDebug
```

## Notes

- V1 targets Android API 36 and has a minimum SDK of 31.
- See the [Android knowledge-base page](../../Obsidian/ClaudeVault/Клиенты/Android.md) for architecture details.
