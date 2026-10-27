"""Записать энергозамер и при необходимости разобрать Perfetto-трейс."""

from __future__ import annotations

import argparse
import csv
import io
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from perf_device import ADB_COMMAND, PerfDeviceError, run_adb, verify_device


PERF_DIR = Path(__file__).resolve().parent
OUTPUT_ROOT = PERF_DIR / "out"
TRACE_CONFIG = PERF_DIR / "power_trace.pbtx"
QUERY_DIR = PERF_DIR / "queries"
DEFAULT_PACKAGE = "com.barkfluff.nightly"
REMOTE_TRACE_DIR = "/data/misc/perfetto-traces"
SCREEN_OFF_TIMEOUT_MAX = "2147483647"


class TraceProcessorError(RuntimeError):
    """Ошибка запуска или разбора результата trace_processor_shell."""


def parse_csv_rows(output: str, *, required_columns: set[str]) -> list[dict[str, str]]:
    """Разобрать CSV trace processor, проверяя заголовок и строки."""
    try:
        reader = csv.DictReader(io.StringIO(output.strip()))
        if not reader.fieldnames or not required_columns.issubset(reader.fieldnames):
            raise ValueError("CSV не содержит ожидаемые столбцы.")
        rows = list(reader)
        if any(None in row or any(value is None for value in row.values()) for row in rows):
            raise ValueError("CSV содержит строку с неправильным числом столбцов.")
        return rows
    except (csv.Error, ValueError) as error:
        raise TraceProcessorError(f"Не удалось разобрать CSV trace processor: {error}") from error


def parse_data_loss(output: str) -> list[dict[str, str]]:
    return parse_csv_rows(
        output,
        required_columns={"name", "idx", "severity", "source", "value"},
    )


def parse_trace_bounds(output: str) -> tuple[int, int]:
    rows = parse_csv_rows(output, required_columns={"start_ts", "end_ts"})
    if len(rows) != 1:
        raise TraceProcessorError("Ожидалась одна строка trace_bounds.")
    try:
        start_ns = int(rows[0]["start_ts"])
        end_ns = int(rows[0]["end_ts"])
    except (TypeError, ValueError) as error:
        raise TraceProcessorError("trace_bounds вернул некорректные timestamps.") from error
    if end_ns < start_ns:
        raise TraceProcessorError("Конец трейса раньше начала.")
    return start_ns, end_ns


def parse_rails(output: str) -> list[dict[str, str | float | None]]:
    rows = parse_csv_rows(output, required_columns={"rail", "energy_uws"})
    result: list[dict[str, str | float | None]] = []
    for row in rows:
        try:
            energy = float(row["energy_uws"])
        except (TypeError, ValueError) as error:
            raise TraceProcessorError("Некорректное значение energy_uws.") from error
        result.append({"rail": row["rail"], "energy_uws": energy})
    return result


def parse_pings(output: str) -> dict[str, int | float | None]:
    rows = parse_csv_rows(
        output,
        required_columns={"completed_count", "mean_duration_ms", "unfinished_count"},
    )
    if len(rows) != 1:
        raise TraceProcessorError("Ожидалась одна строка метрик BF:onlinePing.")
    try:
        completed = int(rows[0]["completed_count"] or 0)
        mean_duration = (
            float(rows[0]["mean_duration_ms"])
            if rows[0]["mean_duration_ms"] not in (None, "")
            else None
        )
        unfinished = int(rows[0]["unfinished_count"] or 0)
    except (TypeError, ValueError) as error:
        raise TraceProcessorError("Некорректные метрики BF:onlinePing.") from error
    return {
        "completed_count": completed,
        "mean_duration_ms": mean_duration,
        "unfinished_count": unfinished,
    }


def require_trace_processor_success(
    returncode: int,
    stdout: str,
    stderr: str,
) -> str:
    if returncode != 0:
        detail = stderr.strip() or stdout.strip() or "нет вывода"
        raise TraceProcessorError(
            f"trace_processor завершился с кодом {returncode}: {detail}"
        )
    return stdout


def run_trace_query(
    executable: Path,
    trace_path: Path,
    query_path: Path,
    *,
    start_ns: int | None = None,
    end_ns: int | None = None,
) -> str:
    query = query_path.read_text(encoding="utf-8")
    if start_ns is not None:
        query = query.replace("{{START_NS}}", str(start_ns))
    if end_ns is not None:
        query = query.replace("{{END_NS}}", str(end_ns))
    if "{{START_NS}}" in query or "{{END_NS}}" in query:
        raise TraceProcessorError(f"Не подставлены границы окна в {query_path.name}.")

    generated_query = trace_path.parent / f".{query_path.stem}.sql"
    generated_query.write_text(query, encoding="utf-8")
    try:
        result = subprocess.run(
            [str(executable), "query", "-f", str(generated_query), str(trace_path)],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except OSError as error:
        raise TraceProcessorError(f"Не удалось запустить trace_processor: {error}") from error
    finally:
        generated_query.unlink(missing_ok=True)
    return require_trace_processor_success(result.returncode, result.stdout, result.stderr)


def analyze_trace(
    executable: Path,
    trace_path: Path,
    *,
    warmup_sec: int,
    minutes: int,
) -> dict[str, Any]:
    data_loss = parse_data_loss(
        run_trace_query(executable, trace_path, QUERY_DIR / "data_loss.sql")
    )
    trace_start_ns, trace_end_ns = parse_trace_bounds(
        run_trace_query(executable, trace_path, QUERY_DIR / "trace_bounds.sql")
    )
    window_start_ns = trace_start_ns + warmup_sec * 1_000_000_000
    window_end_ns = window_start_ns + minutes * 60 * 1_000_000_000
    if window_end_ns > trace_end_ns:
        raise TraceProcessorError(
            "Трейс короче окна анализа: "
            f"нужно {minutes * 60} секунд после прогрева."
        )

    rails = parse_rails(
        run_trace_query(
            executable,
            trace_path,
            QUERY_DIR / "rails.sql",
            start_ns=window_start_ns,
            end_ns=window_end_ns,
        )
    )
    pings = parse_pings(
        run_trace_query(
            executable,
            trace_path,
            QUERY_DIR / "pings.sql",
            start_ns=window_start_ns,
            end_ns=window_end_ns,
        )
    )
    return {
        "status": "FAIL" if data_loss else "PASS",
        "window": {
            "basis": "trace_bounds.start_ts",
            "start_ns": window_start_ns,
            "end_ns": window_end_ns,
            "duration_seconds": minutes * 60,
            "warmup_seconds": warmup_sec,
        },
        "data_loss": data_loss,
        "power_rails": rails,
        "online_ping": pings,
    }


def save_summary(output_dir: Path, summary: dict[str, Any]) -> None:
    (output_dir / "summary.json").write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def restore_device_settings(
    serial: str,
    previous_timeout: str,
    *,
    adb_command: tuple[str, ...],
    restore_timeout: bool,
    restore_stayon: bool,
) -> None:
    errors: list[str] = []
    if restore_timeout:
        command = (
            ("shell", "settings", "delete", "system", "screen_off_timeout")
            if previous_timeout == "null"
            else ("shell", "settings", "put", "system", "screen_off_timeout", previous_timeout)
        )
        try:
            run_adb(serial, *command, adb_command=adb_command)
        except (PerfDeviceError, OSError, subprocess.TimeoutExpired) as error:
            errors.append(str(error))
    if restore_stayon:
        try:
            run_adb(serial, "shell", "svc", "power", "stayon", "false", adb_command=adb_command)
        except (PerfDeviceError, OSError, subprocess.TimeoutExpired) as error:
            errors.append(str(error))
    if errors:
        raise PerfDeviceError("Не удалось восстановить настройки устройства: " + "; ".join(errors))


def capture_power(
    serial: str,
    expect_model: str,
    *,
    warmup_sec: int = 60,
    minutes: int = 10,
    package: str = DEFAULT_PACKAGE,
    trace_processor: Path | None = None,
    adb_command: tuple[str, ...] = ADB_COMMAND,
) -> int:
    model = verify_device(serial, expect_model, adb_command=adb_command)
    if trace_processor is not None and not trace_processor.is_file():
        raise TraceProcessorError(f"Файл trace_processor не найден: {trace_processor}")

    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output_dir = OUTPUT_ROOT / timestamp
    output_dir.mkdir(parents=True, exist_ok=False)
    trace_path = output_dir / "power.perfetto-trace"
    remote_trace = f"{REMOTE_TRACE_DIR}/barkfluff-power-{timestamp}.perfetto-trace"
    duration_ms = (warmup_sec + minutes * 60 + 30) * 1000
    config = TRACE_CONFIG.read_text(encoding="utf-8").replace(
        "{{DURATION_MS}}", str(duration_ms)
    )

    previous_timeout = run_adb(
        serial,
        "shell",
        "settings",
        "get",
        "system",
        "screen_off_timeout",
        adb_command=adb_command,
    ).stdout.strip()
    timeout_change_attempted = False
    stayon_change_attempted = False
    trace_pulled = False
    summary: dict[str, Any] = {
        "device": {"serial": serial, "model": model},
        "package": package,
        "trace": str(trace_path),
        "warmup_seconds": warmup_sec,
        "analysis_seconds": minutes * 60,
        "trace_duration_seconds": duration_ms // 1000,
        "analysis": {"status": "NOT_RUN", "reason": "trace_processor не задан"},
    }

    try:
        timeout_change_attempted = True
        run_adb(
            serial,
            "shell",
            "settings",
            "put",
            "system",
            "screen_off_timeout",
            SCREEN_OFF_TIMEOUT_MAX,
            adb_command=adb_command,
        )
        stayon_change_attempted = True
        run_adb(serial, "shell", "svc", "power", "stayon", "usb", adb_command=adb_command)
        run_adb(serial, "shell", "monkey", "-p", package, "1", adb_command=adb_command)
        run_adb(
            serial,
            "shell",
            "perfetto",
            "--txt",
            "-c",
            "-",
            "-o",
            remote_trace,
            adb_command=adb_command,
            input_text=config,
            timeout=duration_ms // 1000 + 120,
        )
        run_adb(serial, "pull", remote_trace, str(trace_path), adb_command=adb_command)
        trace_pulled = True
        batterystats = run_adb(
            serial,
            "shell",
            "dumpsys",
            "batterystats",
            package,
            adb_command=adb_command,
        )
        (output_dir / "batterystats.txt").write_text(
            batterystats.stdout,
            encoding="utf-8",
        )
    finally:
        restore_device_settings(
            serial,
            previous_timeout,
            adb_command=adb_command,
            restore_timeout=timeout_change_attempted,
            restore_stayon=stayon_change_attempted,
        )

    if trace_processor is not None and trace_pulled:
        try:
            summary["analysis"] = analyze_trace(
                trace_processor,
                trace_path,
                warmup_sec=warmup_sec,
                minutes=minutes,
            )
        except TraceProcessorError as error:
            summary["analysis"] = {"status": "FAIL", "error": str(error)}
            save_summary(output_dir, summary)
            print(f"Ошибка анализа трейса: {error}", file=sys.stderr)
            return 1

    save_summary(output_dir, summary)
    print(f"Результаты сохранены: {output_dir}")
    if summary["analysis"].get("status") == "FAIL":
        print("В трейсе найдены потери данных или ошибки.", file=sys.stderr)
        return 1
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True, help="Явный adb serial устройства")
    parser.add_argument("--expect-model", required=True, help='Ожидаемая модель, например "Pixel 10 Pro"')
    parser.add_argument("--warmup-sec", type=int, default=60)
    parser.add_argument("--minutes", type=int, default=10)
    parser.add_argument("--package", default=DEFAULT_PACKAGE)
    parser.add_argument("--trace-processor", type=Path, help="Путь к trace_processor_shell(.exe)")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.warmup_sec < 0 or args.minutes <= 0:
        print("warmup-sec должен быть >= 0, а minutes — > 0.", file=sys.stderr)
        return 2
    try:
        return capture_power(
            args.serial,
            args.expect_model,
            warmup_sec=args.warmup_sec,
            minutes=args.minutes,
            package=args.package,
            trace_processor=args.trace_processor,
        )
    except (PerfDeviceError, TraceProcessorError, OSError, subprocess.TimeoutExpired) as error:
        print(f"Ошибка энергозамера: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
