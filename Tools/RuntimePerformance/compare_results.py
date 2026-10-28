"""Compare complete, normal-camera benchmark captures; Python standard library only."""
import argparse
import csv
import math
import re
from pathlib import Path
from statistics import median


def percentile(values, fraction):
    values = sorted(values)
    return values[max(0, math.ceil(len(values) * fraction) - 1)]


def load(directory, view, repeats, minimum_seconds=0):
    runs = []
    for repeat in range(1, repeats + 1):
        path = directory / f"normal-{view}-{repeat}.csv"
        with path.open(encoding="utf-8-sig", newline="") as source:
            rows = list(csv.DictReader(source))
        if not rows:
            raise ValueError(f"No samples: {path}")
        frames = [float(row["frame_ms"]) for row in rows]
        if any(not math.isfinite(frame) or frame <= 0 for frame in frames):
            raise ValueError(f"Non-positive or non-finite frame time: {path}")
        if sum(frames) < minimum_seconds * 950:
            raise ValueError(f"Rendered frames do not cover the sample duration: {path}")
        runs.append({"median": median(frames), "p95": percentile(frames, .95), "frames": len(frames)})
    return runs


def metadata(directory):
    result = {}
    for line in (directory / "results.md").read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("|"):
            break
        if ": " in line:
            key, value = line.split(": ", 1)
            result[key] = value
    return result


def fixed_views(directory):
    views = {}
    pattern = r"normal-(forest|creek|dinosaur)-(\d+): position=\(([^)]+)\), rotation=\(([^)]+)\)"
    for match in re.finditer(pattern, (directory / "views.txt").read_text(encoding="utf-8-sig")):
        views[(match[1], int(match[2]))] = tuple(float(v) for v in (match[3] + "," + match[4]).split(","))
    return views


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--before", type=Path, required=True)
    parser.add_argument("--after", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--views", default="forest,creek,dinosaur,route")
    parser.add_argument("--strict-editor-60", action="store_true",
                        help="Require visible 1080p Editor, 15/60/3 sampling and median/P95 <=16.7ms.")
    args = parser.parse_args()
    old_metadata, new_metadata = metadata(args.before), metadata(args.after)
    old_views, new_views = fixed_views(args.before), fixed_views(args.after)
    views = args.views.split(",")
    if args.strict_editor_60:
        if set(views) != {"forest", "creek", "dinosaur-close", "route"} or args.repeats < 3:
            raise ValueError("Strict Editor 60 requires forest,creek,dinosaur-close,route and at least three repeats")
        for data in (old_metadata, new_metadata):
            for key, value in {"Editor": "True", "Batchmode": "False", "Automatic camera target": "1920x1080",
                               "Output mode": "visible Game View", "Quality": "Low", "Game View layout": "maximized", "Spawn seed": "73021"}.items():
                if data.get(key) != value:
                    raise ValueError(f"Strict Editor 60 requires {key}: {value}")
            if float(data.get("Warmup seconds", "0")) < 15 or float(data.get("Sample seconds", "0")) < 60:
                raise ValueError("Strict Editor 60 requires at least 15s warmup and 60s samples")
            if data.get("Close dinosaur", "unavailable") == "unavailable":
                raise ValueError("Strict Editor 60 requires an active close dinosaur")
        if old_metadata.get("Close dinosaur") != new_metadata.get("Close dinosaur"):
            raise ValueError("Close dinosaur identities differ")
        for directory in (args.before, args.after):
            if (directory / "complete.txt").read_text(encoding="utf-8-sig").strip() != "JW_PERF_COMPLETE":
                raise ValueError("Capture did not complete successfully")
    for view in (view for view in views if view in ("forest", "creek", "dinosaur")):
        for repeat in range(1, args.repeats + 1):
            key = (view, repeat)
            if key not in old_views or key not in new_views or any(abs(a - b) > .05 for a, b in zip(old_views[key], new_views[key])):
                raise ValueError(f"Incompatible fixed camera {key}: {old_views.get(key)} / {new_views.get(key)}")
    for key in ("Unity", "Editor", "Development", "CPU", "GPU", "API", "Automatic camera target", "Quality"):
        if key not in old_metadata or old_metadata[key] != new_metadata.get(key):
            raise ValueError(f"Incompatible {key}: {old_metadata.get(key)} / {new_metadata.get(key)}")
    if "Batchmode" in old_metadata and "Batchmode" in new_metadata and old_metadata["Batchmode"] != new_metadata["Batchmode"]:
        raise ValueError("Incompatible Batchmode")
    lines = ["# Runtime performance comparison", "",
             f"Baseline: `{args.before.resolve()}`  ", f"Optimized: `{args.after.resolve()}`", "",
             "Same resolution, quality, graphics threading and build type are required. "
             "Cells show the median of run medians and the worst run P95; FPS is 1000 / median frame time. "
             "Animals remain active, so dynamic content varies. See each capture's metadata, raw CSV and screenshots.", "",
             "|View|Before median ms|After median ms|Before FPS|After FPS|Frame time change|Before worst P95 ms|After worst P95 ms|Stage one|",
             "|---|---:|---:|---:|---:|---:|---:|---:|---|"]
    all_pass = True
    median_budget, p95_budget = (16.7, 16.7) if args.strict_editor_60 else (33.3, 40)
    for view in views:
        before, after = (load(directory, view, args.repeats, 60 if args.strict_editor_60 else 0) for directory in (args.before, args.after))
        old, new = (median(run["median"] for run in runs) for runs in (before, after))
        p95_old, p95_new = (max(run["p95"] for run in runs) for runs in (before, after))
        passed = all(run["median"] <= median_budget and run["p95"] <= p95_budget for run in after)
        all_pass &= passed
        lines.append(f"|{view}|{old:.2f}|{new:.2f}|{1000/old:.1f}|{1000/new:.1f}|{(new/old-1)*100:+.1f}%|{p95_old:.2f}|{p95_new:.2f}|{'PASS' if passed else 'FAIL'}|")
    lines.extend(["", "Stage one: " + ("PASS" if all_pass else "FAIL — one or more runs exceed the frame-time budget."), "",
                  f"Budgets: median <= {median_budget} ms, P95 <= {p95_budget} ms in every run.", "",
                  "GPU/CPU/draw/GC details and unavailable counters remain in the original results.md reports; "
                  "no GPU time is inferred from CPU submission time.", ""])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    if args.strict_editor_60 and not all_pass:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
