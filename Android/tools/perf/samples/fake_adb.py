"""Подставной adb для тестов проверки serial и модели без устройства."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path


args = sys.argv[1:]
log_path = os.environ.get("PERF_TEST_ADB_LOG")
if log_path:
    with Path(log_path).open("a", encoding="utf-8") as log_file:
        log_file.write(json.dumps(args) + "\n")

if len(args) < 3 or args[0] != "-s":
    print("missing -s serial", file=sys.stderr)
    raise SystemExit(2)

device_command = args[2:]
if device_command == ["get-state"]:
    print("device")
elif device_command == ["shell", "getprop", "ro.product.model"]:
    print(os.environ.get("PERF_TEST_ADB_MODEL", "Pixel 10 Pro"))
else:
    print("unexpected device action", file=sys.stderr)
    raise SystemExit(3)
