"""Установить benchmark APK и запустить Macrobenchmark без очистки логина."""

from __future__ import annotations

import argparse
import re
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


def parse_instrumentation_output(output: str) -> str | None:
    if "INSTRUMENTATION_FAILED" in output:
        return "INSTRUMENTATION_FAILED"

    crash = re.search(r"(?m)^INSTRUMENTATION_RESULT: shortMsg=(.+)$", output)
    if crash:
        return f"process crash: {crash.group(1).strip()}"

    if "FAILURES!!!" in output:
        counts = re.search(
            r"(?m)^Tests run:\s*(\d+)\s*,\s*Failures:\s*(\d+)\s*$",
            output,
        )
        failed_tests = re.findall(r"(?m)^\s*\d+\)\s+(.+)$", output)
        details = []
        if counts:
            details.append(f"{counts.group(1)} tests, {counts.group(2)} failures")
        if failed_tests:
            names = ", ".join(failed_tests[:3])
            if len(failed_tests) > 3:
                names += ", …"
            details.append(f"failing tests: {names}")
        return "; ".join(details) if details else "FAILURES!!!"

    runner_summaries = [
        line.strip()
        for line in output.splitlines()
        if line.strip().startswith("OK (") or line.strip() == "FAILURES!!!"
    ]
    if (
        not runner_summaries
        or not re.fullmatch(r"OK \(\d+ tests?\)", runner_summaries[-1])
    ):
        return "missing final OK (...) summary"
    return None


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
    instrumentation_output = instrumentation.stdout + instrumentation.stderr
    (output_dir / "instrumentation.txt").write_text(
        instrumentation_output,
        encoding="utf-8",
    )
    run_adb(serial, "pull", REMOTE_OUTPUT, str(output_dir), adb_command=adb_command)
    print(f"Результаты Macrobenchmark сохранены: {output_dir}")
    failure_summary = parse_instrumentation_output(instrumentation_output)
    if failure_summary is not None:
        print(f"Проблема Macrobenchmark: {failure_summary}", file=sys.stderr)
    if failure_summary is not None or instrumentation.returncode != 0:
        reason = failure_summary or f"код adb {instrumentation.returncode}"
        raise PerfDeviceError(
            f"Инструментирование завершилось с ошибкой ({reason}); "
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
