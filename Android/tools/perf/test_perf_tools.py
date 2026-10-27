"""Проверки разбора trace processor и отказа перед командами замера."""

from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from capture_power import (
    TraceProcessorError,
    analyze_trace,
    parse_data_loss,
    parse_pings,
    parse_rails,
    parse_trace_bounds,
    require_trace_processor_success,
    run_trace_query,
)
from perf_device import PerfDeviceError, verify_device
from run_benchmark import parse_instrumentation_output


SAMPLES = Path(__file__).resolve().parent / "samples"
FAKE_ADB = SAMPLES / "fake_adb.py"


def read_sample(name: str) -> str:
    return (SAMPLES / name).read_text(encoding="utf-8")


class PerfToolTests(unittest.TestCase):
    def test_instrumentation_ok_output_is_accepted(self) -> None:
        output = """INSTRUMENTATION_STATUS: stream=
Time: 12.34

OK (6 tests)
INSTRUMENTATION_CODE: -1
"""
        self.assertIsNone(parse_instrumentation_output(output))

    def test_instrumentation_failures_are_summarized(self) -> None:
        output = """There were 2 failures:
1) openFirstChat(com.barkfluff.benchmark.ChatOpenBenchmark)
java.lang.AssertionError
2) scrollFirstChat(com.barkfluff.benchmark.ChatScrollBenchmark)
java.lang.AssertionError
FAILURES!!!
Tests run: 6,  Failures: 2
INSTRUMENTATION_CODE: -1
"""
        summary = parse_instrumentation_output(output)
        self.assertIsNotNone(summary)
        self.assertIn("6 tests, 2 failures", summary)
        self.assertIn("openFirstChat", summary)
        self.assertIn("scrollFirstChat", summary)

    def test_instrumentation_process_crash_is_rejected(self) -> None:
        output = """INSTRUMENTATION_RESULT: shortMsg=Process crashed.
INSTRUMENTATION_CODE: 0
"""
        self.assertEqual(
            parse_instrumentation_output(output),
            "process crash: Process crashed.",
        )

    def test_instrumentation_failed_marker_is_rejected(self) -> None:
        output = "INSTRUMENTATION_FAILED: com.example.TestRunner\n"
        self.assertEqual(
            parse_instrumentation_output(output),
            "INSTRUMENTATION_FAILED",
        )

    def test_instrumentation_without_final_ok_is_rejected(self) -> None:
        output = "INSTRUMENTATION_STATUS: stream=started\nINSTRUMENTATION_CODE: 0\n"
        self.assertEqual(
            parse_instrumentation_output(output),
            "missing final OK (...) summary",
        )

    def test_clean_trace_samples_parse(self) -> None:
        self.assertEqual(parse_data_loss(read_sample("data_loss_clean.csv")), [])
        self.assertEqual(parse_trace_bounds(read_sample("trace_bounds.csv")), (1_000_000_000, 700_000_000_000))
        self.assertEqual(
            parse_rails(read_sample("rails.csv")),
            [
                {"rail": "modem", "energy_uws": 1_200_000.0},
                {"rail": "cpu_big", "energy_uws": 3_400_000.0},
            ],
        )
        self.assertEqual(
            parse_pings(read_sample("pings.csv")),
            {"completed_count": 199, "mean_duration_ms": 42.5, "unfinished_count": 0},
        )

    def test_data_loss_sample_is_reported(self) -> None:
        rows = parse_data_loss(read_sample("data_loss.csv"))
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["severity"], "data_loss")
        self.assertEqual(rows[0]["value"], "4096")

    def test_trace_processor_error_sample_is_fatal(self) -> None:
        error_output = read_sample("trace_processor_error.txt")
        with self.assertRaisesRegex(TraceProcessorError, "no such column"):
            require_trace_processor_success(1, "", error_output)

    def test_malformed_trace_processor_csv_is_fatal(self) -> None:
        with self.assertRaises(TraceProcessorError):
            parse_trace_bounds("start_ts,end_ts\nnot-a-number,20\n")
        with self.assertRaises(TraceProcessorError):
            parse_pings("completed_count,mean_duration_ms,unfinished_count\n1\n")

    def test_trace_processor_uses_v58_query_argv_and_parses_stdout_only(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_dir:
            directory = Path(temporary_dir)
            trace_path = directory / "trace.perfetto-trace"
            trace_path.touch()
            query_path = directory / "pings.sql"
            query_path.write_text("SELECT 1;\n", encoding="utf-8")
            executable = Path("trace_processor_shell.exe")
            stdout = read_sample("pings.csv")

            with patch("capture_power.subprocess.run") as trace_processor_run:
                trace_processor_run.return_value.returncode = 0
                trace_processor_run.return_value.stdout = stdout
                trace_processor_run.return_value.stderr = "Loading trace: ...\nTrace loaded.\n"

                output = run_trace_query(executable, trace_path, query_path)

        trace_processor_run.assert_called_once_with(
            [
                str(executable),
                "query",
                "-f",
                str(directory / ".pings.sql"),
                str(trace_path),
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
        self.assertEqual(output, stdout)
        self.assertEqual(
            parse_pings(output),
            {"completed_count": 199, "mean_duration_ms": 42.5, "unfinished_count": 0},
        )

    def test_analysis_window_is_exactly_minutes_times_sixty(self) -> None:
        outputs = {
            "data_loss.sql": read_sample("data_loss_clean.csv"),
            "trace_bounds.sql": read_sample("trace_bounds.csv"),
            "rails.sql": read_sample("rails.csv"),
            "pings.sql": read_sample("pings.csv"),
        }
        with patch(
            "capture_power.run_trace_query",
            side_effect=lambda *args, **_kwargs: outputs[args[2].name],
        ):
            analysis = analyze_trace(
                Path("trace_processor_shell.exe"),
                Path("power.perfetto-trace"),
                warmup_sec=60,
                minutes=10,
            )

        self.assertEqual(analysis["window"]["duration_seconds"], 600)
        self.assertEqual(
            analysis["window"]["end_ns"] - analysis["window"]["start_ns"],
            600_000_000_000,
        )
        self.assertEqual(analysis["status"], "PASS")

    def test_missing_serial_fails_without_running_adb(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_dir:
            log_path = Path(temporary_dir) / "adb.jsonl"
            with patch.dict(os.environ, {"PERF_TEST_ADB_LOG": str(log_path)}):
                with self.assertRaisesRegex(PerfDeviceError, "--serial"):
                    verify_device("", "Pixel 10 Pro", adb_command=(sys.executable, str(FAKE_ADB)))
            self.assertFalse(log_path.exists())

    def test_wrong_model_refuses_before_device_actions(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_dir:
            log_path = Path(temporary_dir) / "adb.jsonl"
            environment = {
                "PERF_TEST_ADB_LOG": str(log_path),
                "PERF_TEST_ADB_MODEL": "Pixel 8 Pro",
            }
            with patch.dict(os.environ, environment):
                with self.assertRaisesRegex(PerfDeviceError, "Команды замера не запускались"):
                    verify_device(
                        "test-serial",
                        "Pixel 10 Pro",
                        adb_command=(sys.executable, str(FAKE_ADB)),
                    )

            calls = [json.loads(line) for line in log_path.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(
                calls,
                [
                    ["-s", "test-serial", "get-state"],
                    ["-s", "test-serial", "shell", "getprop", "ro.product.model"],
                ],
            )
            self.assertTrue(all(call[0:2] == ["-s", "test-serial"] for call in calls))


if __name__ == "__main__":
    unittest.main()
