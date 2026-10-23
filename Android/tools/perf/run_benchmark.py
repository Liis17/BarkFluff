"""Установить benchmark APK и запустить Macrobenchmark без очистки логина."""

from __future__ import annotations

import argparse
import sys
from datetime import datetime, timezone
from pathlib import Path

from perf_device import ADB_COMMAND, PerfDeviceError, run_adb, verify_device


PERF_DIR = Path(__file__).resolve().parent
ANDROID_ROOT = PERF_DIR.parents[1]
OUTPUT_ROOT = PERF_DIR / "out"
APP_APK = (
    ANDROID_ROOT
    / "Barkfluff.Client.Android"
    / "app"
    / "build"
    / "outputs"
    / "apk"
    / "nightly"
    / "benchmark"
    / "app-v1-nightly-benchmark.apk"
)
BENCHMARK_APK = (
    ANDROID_ROOT
    / "benchmark"
    / "build"
    / "outputs"
    / "apk"
    / "benchmark"
    / "benchmark-benchmark.apk"
)
INSTRUMENTATION = "com.barkfluff.benchmark/androidx.test.runner.AndroidJUnitRunner"
REMOTE_OUTPUT = "/sdcard/Android/media/com.barkfluff.benchmark/"


def run_benchmark(
    serial: str,
    expect_model: str,
    *,
    test_class: str | None = None,
    install_only: bool = False,
    adb_command: tuple[str, ...] = ADB_COMMAND,
) -> int:
    missing = [path for path in (APP_APK, BENCHMARK_APK) if not path.is_file()]
    if missing:
        raise FileNotFoundError("Сначала соберите benchmark APK: " + ", ".join(map(str, missing)))

    verify_device(serial, expect_model, adb_command=adb_command)
    run_adb(serial, "install", "-r", str(APP_APK), adb_command=adb_command)
    run_adb(serial, "install", "-r", str(BENCHMARK_APK), adb_command=adb_command)
    if install_only:
        print("Оба benchmark APK установлены. Логин и выдачу POST_NOTIFICATIONS выполните вручную.")
        return 0

    command = ["shell", "am", "instrument", "-w"]
    if test_class:
        command.extend(("-e", "class", test_class))
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_dir = OUTPUT_ROOT / timestamp
    output_dir.mkdir(parents=True, exist_ok=False)
    command.append(INSTRUMENTATION)
    instrumentation = run_adb(
        serial,
        *command,
        adb_command=adb_command,
        check=False,
    )
    (output_dir / "instrumentation.txt").write_text(
        instrumentation.stdout + instrumentation.stderr,
        encoding="utf-8",
    )
    run_adb(serial, "pull", REMOTE_OUTPUT, str(output_dir), adb_command=adb_command)
    print(f"Результаты Macrobenchmark сохранены: {output_dir}")
    if instrumentation.returncode != 0:
        raise PerfDeviceError(
            f"Инструментирование завершилось с кодом {instrumentation.returncode}; "
            f"результаты сохранены в {output_dir}."
        )
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True, help="Явный adb serial устройства")
    parser.add_argument("--expect-model", required=True, help='Ожидаемая модель, например "Pixel 10 Pro"')
    parser.add_argument("--class", dest="test_class", help="Полное имя benchmark-класса")
    parser.add_argument(
        "--install-only",
        action="store_true",
        help="Установить APK, сохранив текущие данные и не запуская тесты",
    )
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        return run_benchmark(
            args.serial,
            args.expect_model,
            test_class=args.test_class,
            install_only=args.install_only,
        )
    except (PerfDeviceError, OSError) as error:
        print(f"Ошибка Macrobenchmark: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
