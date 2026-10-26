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


def load(directory, view, repeats):
    runs = []
    for repeat in range(1, repeats + 1):
        path = directory / f"normal-{view}-{repeat}.csv"
        with path.open(encoding="utf-8-sig", newline="") as source:
            rows = list(csv.DictReader(source))
        if not rows:
            raise ValueError(f"No samples: {path}")
        frames = [float(row["frame_ms"]) for row in rows]
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
    args = parser.parse_args()
    old_metadata, new_metadata = metadata(args.before), metadata(args.after)
    old_views, new_views = fixed_views(args.before), fixed_views(args.after)
    for view in ("forest", "creek", "dinosaur"):
        for repeat in range(1, args.repeats + 1):
            key = (view, repeat)
            if key not in old_views or key not in new_views or any(abs(a - b) > .05 for a, b in zip(old_views[key], new_views[key])):
                raise ValueError(f"Incompatible fixed camera {key}: {old_views.get(key)} / {new_views.get(key)}")
    for key in ("Unity", "Editor", "Development", "CPU", "GPU", "API", "Automatic camera target", "Quality"):
        if key not in old_metadata or old_metadata[key] != new_metadata.get(key):
            raise ValueError(f"Incompatible {key}: {old_metadata.get(key)} / {new_metadata.get(key)}")
    lines = ["# Runtime performance comparison", "",
             f"Baseline: `{args.before.resolve()}`  ", f"Optimized: `{args.after.resolve()}`", "",
             "Same resolution, quality, graphics threading and build type are required. "
             "Cells show the median of run medians and the worst run P95; FPS is 1000 / median frame time. "
             "Animals remain active, so dynamic content varies. See each capture's metadata, raw CSV and screenshots.", "",
             "|View|Before median ms|After median ms|Before FPS|After FPS|Frame time change|Before worst P95 ms|After worst P95 ms|Stage one|",
             "|---|---:|---:|---:|---:|---:|---:|---:|---|"]
    all_pass = True
    for view in ("forest", "creek", "dinosaur", "route"):
        before, after = (load(directory, view, args.repeats) for directory in (args.before, args.after))
        old, new = (median(run["median"] for run in runs) for runs in (before, after))
        p95_old, p95_new = (max(run["p95"] for run in runs) for runs in (before, after))
        passed = all(run["median"] <= 33.3 and run["p95"] <= 40 for run in after)
        all_pass &= passed
        lines.append(f"|{view}|{old:.2f}|{new:.2f}|{1000/old:.1f}|{1000/new:.1f}|{(new/old-1)*100:+.1f}%|{p95_old:.2f}|{p95_new:.2f}|{'PASS' if passed else 'FAIL'}|")
    lines.extend(["", "Stage one: " + ("PASS" if all_pass else "FAIL — one or more runs exceed the frame-time budget."), "",
                  "GPU/CPU/draw/GC details and unavailable counters remain in the original results.md reports; "
                  "no GPU time is inferred from CPU submission time.", ""])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
