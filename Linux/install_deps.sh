#!/bin/bash
# Install dependencies for BarkFluffQt on Debian/Ubuntu

set -e

echo "Installing Qt6 development packages..."
sudo apt install -y \
    qt6-base-dev \
    qt6-base-dev-tools \
    qt6-tools-dev \
    libqt6svg6-dev \
    libqt6concurrent6

echo "Installing gRPC and Protobuf..."
sudo apt install -y \
    libgrpc-dev \
    libgrpc++-dev \
    protobuf-compiler \
    protobuf-compiler-grpc

echo "Installing OpenSSL development files..."
sudo apt install -y \
    libssl-dev

echo "Installing build tools..."
sudo apt install -y \
    cmake \
    build-essential \
    ninja-build

echo ""
echo "All dependencies installed!"
echo "Now you can build the project:"
echo "  cd BarkFluffQt"
echo "  mkdir build && cd build"
echo "  cmake .."
echo "  make -j\$(nproc)"