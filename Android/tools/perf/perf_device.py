"""Общие проверки устройства и безопасный вызов adb для замеров Android."""

from __future__ import annotations

import subprocess
from collections.abc import Sequence


ADB_COMMAND = ("adb",)


class PerfDeviceError(RuntimeError):
    """Ошибка проверки или команды устройства."""


def run_adb(
    serial: str,
    *args: str,
    adb_command: Sequence[str] = ADB_COMMAND,
    input_text: str | None = None,
    timeout: float | None = None,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    """Выполнить adb только для явно указанного serial."""
    if not serial or not serial.strip():
        raise PerfDeviceError("Укажите serial устройства через --serial.")
    if not adb_command:
        raise PerfDeviceError("Не задан исполняемый файл adb.")

    command = [*adb_command, "-s", serial, *args]
    result = subprocess.run(
        command,
        input=input_text,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        check=False,
    )
    if check and result.returncode != 0:
        details = result.stderr.strip() or result.stdout.strip()
        raise PerfDeviceError(
            f"Команда adb завершилась с кодом {result.returncode}: "
            f"{' '.join(command[1:])}\n{details}"
        )
    return result


def verify_device(
    serial: str | None,
    expect_model: str | None,
    *,
    adb_command: Sequence[str] = ADB_COMMAND,
) -> str:
    """Проверить serial и модель до выполнения команд, меняющих устройство."""
    if not serial or not serial.strip():
        raise PerfDeviceError("Укажите serial устройства через --serial.")
    if not expect_model or not expect_model.strip():
        raise PerfDeviceError("Укажите ожидаемую модель через --expect-model.")

    state = run_adb(serial, "get-state", adb_command=adb_command).stdout.strip()
    if state != "device":
        raise PerfDeviceError(f"Устройство {serial} недоступно (состояние: {state or 'пустое'}).")

    model = run_adb(
        serial,
        "shell",
        "getprop",
        "ro.product.model",
        adb_command=adb_command,
    ).stdout.strip()
    if not model:
        raise PerfDeviceError(f"Не удалось определить модель устройства {serial}.")
    if model.casefold() != expect_model.strip().casefold():
        raise PerfDeviceError(
            f"Модель устройства не совпала: ожидалась {expect_model!r}, "
            f"получена {model!r}. Команды замера не запускались."
        )
    return model
