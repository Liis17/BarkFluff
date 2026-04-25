#!/bin/bash

set -e

APP_NAME="BarkFluff"
DMG_URL="https://storage.barkfluff.com/get/barkfluffmacos/release/"
TMP_DMG="/tmp/${APP_NAME}.dmg"

echo "Downloading ${APP_NAME}..."

curl -L "$DMG_URL" -o "$TMP_DMG"

echo "Mounting DMG..."

MOUNT_OUTPUT=$(hdiutil attach "$TMP_DMG" -nobrowse)
MOUNT_POINT=$(echo "$MOUNT_OUTPUT" | grep "/Volumes/" | awk '{print $3}')

echo "Opening installer..."

open "$MOUNT_POINT"

echo ""
echo "Drag ${APP_NAME}.app to Applications to install."
