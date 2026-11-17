"""P10-012: tables for the `sim-trace` set (TreeSimulation with traced agents, stage G4).

usage: python sim_trace.py <run folder> [<run folder> ...] > sim-trace-table.md

Per run of a scenario (one CSV): over the measured frames, the median and p99 of `sim_step_us`
(the TreeSimulation.Step call alone) and the mean of `sim_step_alloc_bytes` (managed bytes
allocated by Step per tick). Per scenario: the median of the per-run values, min-max in brackets.
Trace records per tick come from runs.jsonl (`sim.traceRecorded / sim.ticks`: everything the traced
agents wrote since they were traced, warm-up included). ABCCBA inside each run: A = no agent traced
(T0), B = one (T1), C = eight (T8); T1/T0 and T8/T0 are ratios of the medians of the per-run Step
medians and p99s, and per half (first A B C against the mirrored C B A).
"""
import csv
import json
import os
import statistics
import sys


def pct(values, q):
    ordered = sorted(values)
    if not ordered:
        return float('nan')
    k = (len(ordered) - 1) * q
    lo = int(k)
    hi = min(lo + 1, len(ordered) - 1)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (k - lo)


def per_run(folder):
    rows = []
    for line in open(os.path.join(folder, 'runs.jsonl'), encoding='utf-8'):
        r = json.loads(line)
        sim = r.get('sim', {})
        if not r['scenario'].startswith('sim-') or 'traced' not in sim:
            continue
        name = '%s_n%d_r%d.csv' % (r['scenario'], r['n'], r['run'])
        frames = list(csv.DictReader(open(os.path.join(folder, name), encoding='utf-8')))
        step = [float(f['sim_step_us']) for f in frames]
        ticks = sim.get('ticks', 0)
        rows.append({
            'folder': os.path.basename(folder.rstrip('/\\')), 'tree': r['tree'], 'traced': sim['traced'],
            'abba': r['abba'], 'half': 1 if r['abba'].endswith('1') else 2, 'run': r['run'], 'workers': r['jobWorkers'],
            'frames': len(frames), 'step_median': statistics.median(step), 'step_p99': pct(step, 0.99),
            'alloc_per_tick': statistics.fmean(float(f['sim_step_alloc_bytes']) for f in frames),
            'records_per_tick': (sim.get('traceRecorded', 0) / ticks) if ticks else 0.0,
            'dropped': sim.get('traceDropped', 0),
            'faults': sim.get('faults', 0) + sim.get('failedSteps', 0) + sim.get('completeFailures', 0),
            'alloc_window_bytes': r.get('allocWindowBytes'),
        })
    return rows


def fmt(values, digits=1):
    m = statistics.median(values)
    return '%.*f (%.*f–%.*f)' % (digits, m, digits, min(values), digits, max(values))


def main(folders):
    rows = [row for folder in folders for row in per_run(folder)]
    json.dump(rows, open(os.path.join(folders[0], 'sim-trace-runs.json'), 'w'), indent=1)
    keys = []
    for row in rows:
        key = (row['workers'], row['tree'], row['traced'])
        if key not in keys:
            keys.append(key)
    print('| workers | tree | traced | runs | Step µs median | Step µs p99 | Step bytes/tick | trace records/tick | dropped | GC window bytes | faults |')
    print('|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for workers, tree, traced in keys:
        sel = [r for r in rows if r['workers'] == workers and r['tree'] == tree and r['traced'] == traced]
        print('| %d | `%s` | %d | %d | %s | %s | %s | %s | %d | %s | %d |' % (
            workers, tree, traced, len(sel), fmt([r['step_median'] for r in sel]), fmt([r['step_p99'] for r in sel]),
            fmt([r['alloc_per_tick'] for r in sel], 0), fmt([r['records_per_tick'] for r in sel], 1),
            max(r['dropped'] for r in sel), max(r['alloc_window_bytes'] or 0 for r in sel), sum(r['faults'] for r in sel)))
    print()
    print('Ratios to no tracing (Step µs, median of per-run values; per half: first A B C, mirrored C B A):')
    print()
    print('| workers | tree | T1/T0 median | T8/T0 median | T1/T0 p99 | T8/T0 p99 | T8/T0 half 1 | T8/T0 half 2 |')
    print('|---:|---|---:|---:|---:|---:|---:|---:|')
    seen = []
    for row in rows:
        key = (row['workers'], row['tree'])
        if key not in seen:
            seen.append(key)
    for workers, tree in seen:
        def med(traced, field='step_median', half=None):
            values = [r[field] for r in rows if r['workers'] == workers and r['tree'] == tree and r['traced'] == traced
                      and (half is None or r['half'] == half)]
            return statistics.median(values) if values else float('nan')

        print('| %d | `%s` | %.3f | %.3f | %.3f | %.3f | %.3f | %.3f |' % (
            workers, tree, med(1) / med(0), med(8) / med(0), med(1, 'step_p99') / med(0, 'step_p99'),
            med(8, 'step_p99') / med(0, 'step_p99'), med(8, half=1) / med(0, half=1), med(8, half=2) / med(0, half=2)))


if __name__ == '__main__':
    main(sys.argv[1:])
