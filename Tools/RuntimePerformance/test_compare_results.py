"""Acceptance-report regression tests; synthetic fixtures are never performance evidence."""
import contextlib
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("compare_results", Path(__file__).with_name("compare_results.py"))
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)


class StrictEditorAcceptance(unittest.TestCase):
    def setUp(self):
        base = Path(__file__).resolve().parents[2] / "Logs" / "RuntimePerformance"
        self.temp = tempfile.TemporaryDirectory(dir=base)
        if Path(self.temp.name).resolve().parent != base.resolve():
            raise RuntimeError("Fixture cleanup must stay in the workspace log directory")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.before, self.after = self.root / "before", self.root / "after"
        self.report = self.root / "report.md"
        self.metadata = "\n".join(f"{key}: {value}" for key, value in {
            "Unity": "2022.3", "Editor": "True", "Batchmode": "False", "Development": "True",
            "CPU": "fixture", "GPU": "fixture", "API": "Direct3D11", "Quality": "Low",
            "Automatic camera target": "1920x1080", "Output mode": "visible Game View",
            "Warmup seconds": "15", "Sample seconds": "60", "Close dinosaur": "fixture animal",
            "Game View layout": "maximized", "Spawn seed": "73021"}.items())
        for directory in (self.before, self.after):
            directory.mkdir()
            (directory / "results.md").write_text(self.metadata, encoding="utf-8")
            (directory / "complete.txt").write_text("JW_PERF_COMPLETE\n", encoding="utf-8")
            (directory / "views.txt").write_text("\n".join(
                f"normal-{view}-{repeat}: position=(0,0,0), rotation=(0,0,0)"
                for view in ("forest", "creek") for repeat in range(1, 4)), encoding="utf-8")
            for view in ("forest", "creek", "dinosaur-close", "route"):
                for repeat in range(1, 4):
                    (directory / f"normal-{view}-{repeat}.csv").write_text("frame_ms\n" + "15\n" * 4000, encoding="utf-8")

    def run_report(self):
        args = ["compare_results.py", "--before", str(self.before), "--after", str(self.after),
                "--output", str(self.report), "--strict-editor-60", "--views", "forest,creek,dinosaur-close,route"]
        with patch.object(sys, "argv", args), contextlib.redirect_stdout(io.StringIO()):
            try:
                compare.main()
            except SystemExit as error:
                if error.code != 1: raise
        return self.report.read_text(encoding="utf-8")

    def test_all_runs_within_budget(self):
        self.assertIn("Stage one: PASS", self.run_report())

    def test_one_slow_repeat_fails(self):
        (self.after / "normal-creek-3.csv").write_text("frame_ms\n" + "25\n" * 2400, encoding="utf-8")
        self.assertIn("Stage one: FAIL", self.run_report())

    def test_fast_median_with_bad_tail_fails(self):
        (self.after / "normal-route-1.csv").write_text("frame_ms\n" + "12\n" * 3600 + "50\n" * 400, encoding="utf-8")
        self.assertIn("Stage one: FAIL", self.run_report())

    def test_short_sampling_and_batchmode_rejected(self):
        for old, new in (("Sample seconds: 60", "Sample seconds: 10"), ("Batchmode: False", "Batchmode: True"),
                         ("Close dinosaur: fixture animal", "Close dinosaur: unavailable")):
            with self.subTest(new=new):
                (self.after / "results.md").write_text(self.metadata.replace(old, new), encoding="utf-8")
                with self.assertRaises(ValueError): self.run_report()

    def test_missing_repeat_rejected(self):
        (self.after / "normal-forest-3.csv").unlink()
        with self.assertRaises(FileNotFoundError): self.run_report()

    def test_partial_render_coverage_rejected(self):
        (self.after / "normal-creek-1.csv").write_text("frame_ms\n" + "15\n" * 100, encoding="utf-8")
        with self.assertRaises(ValueError): self.run_report()

    def test_incomplete_capture_rejected(self):
        (self.after / "complete.txt").write_text("interrupted", encoding="utf-8")
        with self.assertRaises(ValueError): self.run_report()


if __name__ == "__main__":
    unittest.main()
