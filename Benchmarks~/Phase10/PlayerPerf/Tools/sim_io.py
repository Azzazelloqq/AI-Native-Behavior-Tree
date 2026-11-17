"""P10-010: tables for the `sim-io` set (TreeSimulation with game inputs and commands, stage G2).

usage: python sim_io.py <run folder> [<run folder> ...] > sim-io-table.md

Per run of a scenario (one CSV): over the measured frames, the median and p99 of `sim_step_us`
(the TreeSimulation.Step call alone; the game's input writes and command reads are outside it), the
mean of `sim_step_alloc_bytes` (managed bytes allocated by Step per tick) and the mean of
`bracket_us` (main-thread time of the whole frame, input writes and command reads included). Per
scenario: the median of the per-run values, min-max in brackets. Commands per tick come from
runs.jsonl (`sim.commandsMeasured / sim.ticksMeasured`). ABBA inside each run: B = the G1 tree
(no inputs, no commands), D = its io-* twin; D/B is the ratio of the medians of the per-run Step
medians, and per half (D1/B1, D2/B2).
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
        if not r['scenario'].startswith('sim-') or r.get('abba', '') not in ('B1', 'B2', 'D1', 'D2'):
            continue
        name = '%s_n%d_r%d.csv' % (r['scenario'], r['n'], r['run'])
        frames = list(csv.DictReader(open(os.path.join(folder, name), encoding='utf-8')))
        step = [float(f['sim_step_us']) for f in frames]
        sim = r.get('sim', {})
        ticks = sim.get('ticksMeasured', 0)
        rows.append({
            'folder': os.path.basename(folder.rstrip('/\\')), 'scenario': r['scenario'].rsplit('-', 1)[0],
            'tree': r['tree'], 'abba': r['abba'], 'run': r['run'], 'workers': r['jobWorkers'], 'frames': len(frames),
            'step_median': statistics.median(step), 'step_p99': pct(step, 0.99),
            'alloc_per_tick': statistics.fmean(float(f['sim_step_alloc_bytes']) for f in frames),
            'bracket_mean': statistics.fmean(float(f['bracket_us']) for f in frames),
            'commands_per_tick': (sim.get('commandsMeasured', 0) / ticks) if ticks else 0.0,
            'faults': sim.get('faults', 0) + sim.get('failedSteps', 0),
            'alloc_window_bytes': r.get('allocWindowBytes'),
        })
    return rows


def fmt(values, digits=1):
    m = statistics.median(values)
    return '%.*f (%.*f–%.*f)' % (digits, m, digits, min(values), digits, max(values))


def main(folders):
    rows = [row for folder in folders for row in per_run(folder)]
    json.dump(rows, open(os.path.join(folders[0], 'sim-io-runs.json'), 'w'), indent=1)
    keys = []
    for row in rows:
        key = (row['workers'], row['scenario'])
        if key not in keys:
            keys.append(key)
    print('| workers | scenario | runs | Step µs median | Step µs p99 | Step bytes/tick | frame µs mean | commands/tick | GC window bytes | faults |')
    print('|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    for workers, scenario in keys:
        sel = [r for r in rows if r['workers'] == workers and r['scenario'] == scenario]
        print('| %d | `%s` | %d | %s | %s | %s | %s | %s | %s | %d |' % (
            workers, scenario, len(sel), fmt([r['step_median'] for r in sel]), fmt([r['step_p99'] for r in sel]),
            fmt([r['alloc_per_tick'] for r in sel], 0), fmt([r['bracket_mean'] for r in sel]),
            fmt([r['commands_per_tick'] for r in sel], 1),
            max(r['alloc_window_bytes'] or 0 for r in sel), sum(r['faults'] for r in sel)))
    print()
    print('ABBA (Step µs, median of per-run medians; B = G1 tree, D = io-* twin with inputs and commands):')
    print()
    print('| workers | pair | B | D | D/B | D1/B1 | D2/B2 |')
    print('|---:|---|---:|---:|---:|---:|---:|')
    pairs = []
    for row in rows:
        if row['abba'].startswith('D'):
            key = (row['workers'], row['scenario'])
            if key not in pairs:
                pairs.append(key)
    for workers, d_id in pairs:
        b_id = d_id.replace('sim-io-', 'sim-shared-')

        def med(scenario, labels):
            values = [r['step_median'] for r in rows if r['workers'] == workers and r['scenario'] == scenario and r['abba'] in labels]
            return statistics.median(values) if values else float('nan')

        b, d = med(b_id, ('B1', 'B2')), med(d_id, ('D1', 'D2'))
        print('| %d | `%s` vs `%s` | %.1f | %.1f | %.2f | %.2f | %.2f |' % (
            workers, d_id, b_id, b, d, d / b, med(d_id, ('D1',)) / med(b_id, ('B1',)), med(d_id, ('D2',)) / med(b_id, ('B2',))))


if __name__ == '__main__':
    main(sys.argv[1:])
