"""Validate and summarize complete, visible non-Development Player captures."""
import argparse
import csv
import math
import re
from pathlib import Path
from statistics import median

VIEWS = ('forest', 'creek', 'dinosaur-close', 'route')


def metadata(path):
    result = {}
    for line in (path / 'results.md').read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('|'):
            break
        if ': ' in line:
            key, value = line.split(': ', 1)
            result[key] = value
    return result


def percentile(values, fraction):
    return sorted(values)[max(0, math.ceil(len(values) * fraction) - 1)]


def load(path, tier, scale):
    if (path / 'INVALID.md').exists():
        raise ValueError(f'Invalid capture: {path}')
    if (path / 'complete.txt').read_text(encoding='utf-8-sig').strip() != 'JW_PERF_COMPLETE':
        raise ValueError(f'Incomplete capture: {path}')
    meta = metadata(path)
    for key, expected in {'Editor': 'False', 'Batchmode': 'False', 'Development': 'False',
                          'Automatic camera target': '1920x1080', 'Quality': tier, 'Spawn seed': '73021'}.items():
        if meta.get(key) != expected:
            raise ValueError(f'{path}: expected {key}={expected}, got {meta.get(key)}')
    if meta.get('Output mode') not in ('visible Game View', 'visible Player window'):
        raise ValueError(f'Offscreen capture: {path}')
    if float(meta.get('Warmup seconds', 0)) < 15 or float(meta.get('Sample seconds', 0)) < 60 or int(meta.get('Repeats', 0)) != 3:
        raise ValueError(f'Expected 15/60/3 sampling: {path}')
    if abs(float(meta.get('World render scale', 1)) - scale) > .001:
        raise ValueError(f'Unexpected world resolution scale: {path}')
    diagnostics = (path / 'diagnostics.txt').read_text(encoding='utf-8-sig')
    for repeat in range(1, 4):
        match = re.search(rf'normal-dinosaur-close-{repeat}:.*dinosaur submitted frames=(\d+)/(\d+)', diagnostics)
        if not match or int(match[2]) <= 0 or int(match[1]) < int(match[2]) * .95:
            raise ValueError(f'Missing close dinosaur: {path}, repeat {repeat}')
    runs = {}
    for view in VIEWS:
        values = []
        for repeat in range(1, 4):
            with (path / f'normal-{view}-{repeat}.csv').open(encoding='utf-8-sig', newline='') as stream:
                frames = [float(row['frame_ms']) for row in csv.DictReader(stream)]
            if not frames or any(not math.isfinite(value) or value <= 0 for value in frames) or sum(frames) < 57000:
                raise ValueError(f'Partial/non-rendered sample: {path}, {view}, {repeat}')
            values.append(frames)
        runs[view] = dict(median=median(median(frames) for frames in values),
                          p95=max(percentile(frames, .95) for frames in values),
                          p99=max(percentile(frames, .99) for frames in values),
                          mean_fps=sum(map(len, values)) * 1000 / sum(map(sum, values)))
    return meta, runs


def fixed_views(path):
    text = (path / 'views.txt').read_text(encoding='utf-8-sig')
    pattern = r'normal-(forest|creek)-(\d+): position=\(([^)]+)\), rotation=\(([^)]+)\)'
    return {(view, repeat): tuple(float(value) for value in (position + ',' + rotation).split(','))
            for view, repeat, position, rotation in re.findall(pattern, text)}


def compare(root, prefix):
    lines = ['# All-tier Player performance', '',
             'Visible 1920x1080 non-Development Players; 15s warmup, 60s samples, three repeats. '
             'Median = median of run medians; P95/P99 = worst repeat; average FPS includes long frames. '
             'Dinosaur close-up follows an active animal, so dynamic visibility varies.', '',
             'The shipped final defaults use native 1080p in every tier. Native optimization and final results are therefore identical. '
             'Scaled candidates (Low 75%, Medium 85%, High 100%) were measured separately and are not selected defaults.', '',
             '|Tier|View|Before median ms|Final native ms|Scaled candidate ms|Before / final average FPS|Candidate average FPS|Before / final P95 ms|Final P99 ms|',
             '|---|---|---:|---:|---:|---:|---:|---:|---:|']
    output = []
    sources = []
    for tier in ('Low', 'Medium', 'High'):
        scale = .75 if tier == 'Low' else .85 if tier == 'Medium' else 1
        paths = {phase: root / f'{prefix}-{phase}-{tier}' for phase in ('before', 'native', 'scaled')}
        if tier == 'High':
            paths['scaled'] = paths['native']
        before_meta, before = load(paths['before'], tier, 1)
        native_meta, native = load(paths['native'], tier, 1)
        candidate_meta, candidate = load(paths['scaled'], tier, scale)
        final = native
        for meta in (native_meta, candidate_meta):
            for key in ('CPU', 'GPU', 'API', 'Unity', 'Close dinosaur'):
                if meta.get(key) != before_meta.get(key):
                    raise ValueError(f'Incompatible {key} in {tier}')
            pattern = r'hard trees=(\d+), dinosaurs=(\d+)'
            if re.search(pattern, meta.get('Population', '')).groups() != re.search(pattern, before_meta.get('Population', '')).groups():
                raise ValueError(f'Population changed in {tier}')
        poses = fixed_views(paths['before'])
        for phase in ('native', 'scaled'):
            other = fixed_views(paths[phase])
            if len(poses) != 6 or poses.keys() != other.keys() or any(abs(a-b) > .05 for key in poses for a,b in zip(poses[key], other[key])):
                raise ValueError(f'Fixed views differ in {tier}/{phase}')
        for view in VIEWS:
            a, n, c, b = before[view], native[view], candidate[view], final[view]
            lines.append(f'|{tier}|{view}|{a["median"]:.2f}|{b["median"]:.2f}|{c["median"]:.2f}|'
                         f'{a["mean_fps"]:.1f} / {b["mean_fps"]:.1f}|{c["mean_fps"]:.1f}|'
                         f'{a["p95"]:.2f} / {b["p95"]:.2f}|{b["p99"]:.2f}|')
            output.append(dict(tier=tier, view=view, before=a, native=n, candidate=c, final=b, final_scale=1))
        sources += ['', f'{tier} sources: ' + ', '.join(f'[{phase}]({path.as_posix()}/results.md)' for phase,path in paths.items()), '']
    lines += sources
    lines += ['No fixed FPS target was imposed. Frame-time regressions and unsupported counters must be reviewed in the raw reports.', '']
    return '\n'.join(lines), output


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('Logs/RuntimePerformance'))
    parser.add_argument('--prefix', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report, data = compare(args.root.resolve(), args.prefix)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(report, encoding='utf-8')
    import json
    args.output.with_suffix('.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
    print(report)
