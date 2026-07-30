[← Documentation hub](../README.md)

<p align="center">
  <img src="../../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Linux</h1>

<p align="center">
  <strong>Build the Qt 6 desktop client with CMake, gRPC, and Protobuf.</strong>
</p>

<p align="center">
  <a href="../../README.md">Overview</a> ·
  <a href="#requirements">Requirements</a> ·
  <a href="#build">Build</a>
</p>

---

The Linux client is a Qt 6 / C++20 desktop application using gRPC and Protobuf.

## Requirements

- CMake 3.20+ and a C++20 compiler.
- Qt 6: Core, Widgets, Network, Svg, Concurrent, Multimedia, MultimediaWidgets, and DBus.
- Protobuf, gRPC, and OpenSSL development packages.

On Debian/Ubuntu, the project includes a dependency helper:

```bash
cd Linux
./install_deps.sh
```

## Build

```bash
cd Linux
cmake -S . -B build
cmake --build build --parallel
./build/BarkFluffQt
```

## Proto path note

`Linux/CMakeLists.txt` currently expects the protobuf contracts at `../BarkFluffBackend/Shared/BarkFluff.Proto`. In this repository they are at `../Shared/BarkFluff.Proto`; align `PROTO_DIR` locally before configuring if your checkout does not provide the expected sibling directory.

For design and architecture notes, see [Linux Qt in the knowledge base](../../Obsidian/ClaudeVault/Клиенты/Linux-Qt.md).
