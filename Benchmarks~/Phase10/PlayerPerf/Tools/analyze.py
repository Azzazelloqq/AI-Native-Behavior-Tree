"""Aggregates AibtPerf Player output (runs.jsonl + per-run CSVs) into summary.json and tables.md.

Per (scenario, N) and run: mean/p50/p95/p99/max of the frame bracket (Update start -> LateUpdate
end, main thread), the feeder share, the scheduler drive share, allocation per frame from the
GC-disabled window, GC collections per 1000 measured frames, creation and memory per agent.
Across runs the table shows the median of the per-run values and the min..max spread of the mean.
"""
import csv
import json
import os
import statistics
import sys
from collections import defaultdict


def pct(values, p):
    values = sorted(values)
    if not values:
        return float('nan')
    k = (len(values) - 1) * p / 100.0
    f = int(k)
    c = min(f + 1, len(values) - 1)
    return values[f] + (values[c] - values[f]) * (k - f)


def load(folder):
    runs = [json.loads(line) for line in open(os.path.join(folder, 'runs.jsonl'), encoding='utf-8') if line.strip()]
    rows = []
    for r in runs:
        name = '%s_n%d_r%d.csv' % (r['scenario'], r['n'], r['run'])
        frames = list(csv.DictReader(open(os.path.join(folder, name), encoding='utf-8')))
        bracket = [float(f['bracket_us']) for f in frames]
        feeder = [float(f['feeder_us']) for f in frames]
        sched = [float(f['scheduler_drive_us']) for f in frames]
        dt = [float(f['frame_dt_us']) for f in frames]
        gc_in_bracket = sum(int(f.get('gc_in_bracket') or 0) for f in frames)
        n = r['n']
        allocw = r.get('allocWindowBytes')
        allocf = allocw / r['allocWindowFrames'] if allocw is not None and r.get('allocWindowGc', 0) == 0 else None
        rows.append({
            'scenario': r['scenario'], 'n': n, 'run': r['run'],
            'mean': statistics.fmean(bracket), 'p50': pct(bracket, 50), 'p95': pct(bracket, 95), 'p99': pct(bracket, 99), 'max': max(bracket),
            'feeder_mean': statistics.fmean(feeder), 'sched_mean': statistics.fmean(sched), 'frame_dt_mean': statistics.fmean(dt),
            'frames': len(frames),
            'gc_per_1000_frames': 1000.0 * r['gcCollectionsGen0'] / max(1, len(frames)),
            'gc_in_bracket': gc_in_bracket,
            'alloc_per_frame': allocf,
            'alloc_window_gc': r.get('allocWindowGc'),
            'create_us_per_agent': r['createUs'] / n,
            'first_agent_us': r['firstUs'],
            'create_heap_bytes_per_agent': (r.get('createHeapBytes') or 0) / n,
            'retained_heap_bytes_per_agent': (r['heapAfter'] - r['heapBefore']) / n,
            'sys_bytes_per_agent': (r['sysAfterWarmup'] - r['sysBefore']) / n,
            'running': r['running'], 'effects_per_frame': r['effects'] / max(1, len(frames) + (r.get('allocWindowFrames') or 0)) if r['effects'] else 0,
            'spotted': r['spotted'], 'native_completed_runs': r['nativeCompletedRuns'], 'faults': r['faultsTotal'],
        })
    return rows


def aggregate(rows):
    groups = defaultdict(list)
    for row in rows:
        groups[(row['scenario'], row['n'])].append(row)
    out = []
    for (scenario, n), items in sorted(groups.items(), key=lambda kv: (kv[0][0], kv[0][1])):
        def med(key):
            vals = [i[key] for i in items if i[key] is not None]
            return statistics.median(vals) if vals else None
        means = [i['mean'] for i in items]
        out.append({
            'scenario': scenario, 'n': n, 'runs': len(items),
            'mean_us': med('mean'), 'mean_min_us': min(means), 'mean_max_us': max(means),
            'p50_us': med('p50'), 'p95_us': med('p95'), 'p99_us': med('p99'), 'max_us': med('max'),
            'us_per_agent': med('mean') / n,
            'feeder_us': med('feeder_mean'), 'scheduler_drive_us': med('sched_mean'), 'frame_dt_us': med('frame_dt_mean'),
            'alloc_bytes_per_frame': med('alloc_per_frame'),
            'alloc_bytes_per_agent_tick': (med('alloc_per_frame') / n) if med('alloc_per_frame') is not None else None,
            'gc_per_1000_frames': med('gc_per_1000_frames'),
            'create_us_per_agent': med('create_us_per_agent'), 'first_agent_us': med('first_agent_us'),
            'first_agent_us_run1': next((i['first_agent_us'] for i in items if i['run'] == 1), None),
            'create_heap_bytes_per_agent': med('create_heap_bytes_per_agent'),
            'retained_heap_bytes_per_agent': med('retained_heap_bytes_per_agent'),
            'sys_bytes_per_agent': med('sys_bytes_per_agent'),
            'effects_per_frame': med('effects_per_frame'), 'spotted_total': sum(i['spotted'] for i in items),
            'running_min': min(i['running'] for i in items), 'faults': max(i['faults'] for i in items),
        })
    return out


def crossing(points, target_us):
    """Linear interpolation of N where mean main-thread time reaches target (points: [(n, us)])."""
    points = sorted(points)
    for (n0, t0), (n1, t1) in zip(points, points[1:]):
        if t0 <= target_us <= t1 and t1 > t0:
            return n0 + (target_us - t0) * (n1 - n0) / (t1 - t0)
    if points and points[-1][1] < target_us:
        n, t = points[-1]
        return ('>', n, round(n * target_us / t))  # linear extrapolation from the last point
    if points and points[0][1] > target_us:
        return ('<', points[0][0])
    return None


def fmt(v, digits=1):
    if v is None:
        return '-'
    if isinstance(v, float):
        return ('{:,.%df}' % digits).format(v)
    return '{:,}'.format(v)


def tables(summary):
    lines = []
    by = defaultdict(list)
    for s in summary:
        by[s['scenario']].append(s)
    for scenario, items in by.items():
        lines.append('### ' + scenario)
        lines.append('')
        lines.append('| N | mean µs | spread (min..max of run means) | p50 | p95 | p99 | µs/agent | feeder µs | scheduler drive µs | alloc B/frame | alloc B/agent-tick | GC per 1000 frames | create µs/agent | create heap B/agent | retained heap B/agent | process mem B/agent |')
        lines.append('|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
        for s in items:
            lines.append('| {n} | {mean} | {lo}..{hi} | {p50} | {p95} | {p99} | {pa} | {fd} | {sd} | {af} | {aa} | {gc} | {cu} | {ch} | {rh} | {sm} |'.format(
                n=fmt(s['n']), mean=fmt(s['mean_us']), lo=fmt(s['mean_min_us']), hi=fmt(s['mean_max_us']),
                p50=fmt(s['p50_us']), p95=fmt(s['p95_us']), p99=fmt(s['p99_us']), pa=fmt(s['us_per_agent'], 3),
                fd=fmt(s['feeder_us']), sd=fmt(s['scheduler_drive_us']), af=fmt(s['alloc_bytes_per_frame'], 0),
                aa=fmt(s['alloc_bytes_per_agent_tick'], 1), gc=fmt(s['gc_per_1000_frames']), cu=fmt(s['create_us_per_agent'], 2),
                ch=fmt(s['create_heap_bytes_per_agent'], 0), rh=fmt(s['retained_heap_bytes_per_agent'], 0), sm=fmt(s['sys_bytes_per_agent'], 0)))
        lines.append('')
    lines.append('### Agent count at 1 ms and 4 ms of main-thread time (mean bracket, linear interpolation)')
    lines.append('')
    lines.append('| scenario | N at 1 ms | N at 4 ms |')
    lines.append('|---|---:|---:|')
    for scenario, items in by.items():
        pts = [(s['n'], s['mean_us']) for s in items]
        if len(pts) < 3:
            continue
        c1, c4 = crossing(pts, 1000.0), crossing(pts, 4000.0)
        def show(c):
            if c is None:
                return '-'
            if isinstance(c, tuple):
                return ('> %s (extrapolated ~%s)' % (fmt(c[1]), fmt(c[2]))) if c[0] == '>' else ('< %s' % fmt(c[1]))
            return fmt(round(c))
        lines.append('| %s | %s | %s |' % (scenario, show(c1), show(c4)))
    return '\n'.join(lines) + '\n'


def main():
    folders = sys.argv[1:]
    rows = []
    for folder in folders:
        rows.extend(load(folder))
    summary = aggregate(rows)
    out = folders[0]
    json.dump(summary, open(os.path.join(out, 'summary.json'), 'w'), indent=1)
    open(os.path.join(out, 'tables.md'), 'w', encoding='utf-8').write(tables(summary))
    for r in summary:
        print('%-30s %6d mean %10.0f us/ag %8.3f p99 %9.0f allocB/fr %s gc/1k %s create %s rss/ag %s heap/ag %s run %d fx/fr %s' % (
            r['scenario'], r['n'], r['mean_us'], r['us_per_agent'], r['p99_us'], fmt(r['alloc_bytes_per_frame'], 0),
            round(r['gc_per_1000_frames']), round(r['create_us_per_agent'], 1), round(r['sys_bytes_per_agent']),
            round(r['retained_heap_bytes_per_agent']), r['running_min'], fmt(r['effects_per_frame'])))


if __name__ == '__main__':
    main()
