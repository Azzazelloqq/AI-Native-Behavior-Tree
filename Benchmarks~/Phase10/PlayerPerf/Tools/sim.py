"""P10-009: tables for the `sim` set (TreeSimulation vs NativeTreeAgent UpdateCadence 5).

usage: python sim.py <run folder> [<run folder> ...] > sim-table.md

Per run of a scenario (one CSV): over the measured frames, the median and p99 of `sim_step_us`
(the TreeSimulation.Step call alone, container scenarios), the mean, median and p99 of `bracket_us`
(main-thread time of the whole frame from the first script Update to the last LateUpdate) and the
mean of `sim_step_alloc_bytes` (managed bytes allocated by Step per tick). Per scenario and folder:
the median of the per-run values, min-max in brackets. ABBA labels come from runs.jsonl ("A1",
"B1", "C1", "B2", "A2"); the A and B rows are also given per half.
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
        if not (r['scenario'].startswith('sim-') or 'cadence5' in r['scenario']):
            continue
        name = '%s_n%d_r%d.csv' % (r['scenario'], r['n'], r['run'])
        frames = list(csv.DictReader(open(os.path.join(folder, name), encoding='utf-8')))
        bracket = [float(f['bracket_us']) for f in frames]
        step = [float(f['sim_step_us']) for f in frames]
        alloc = [float(f['sim_step_alloc_bytes']) for f in frames]
        base = r['scenario'].rsplit('-', 1)[0]
        rows.append({
            'folder': os.path.basename(folder.rstrip('/\\')), 'scenario': base, 'abba': r.get('abba', ''),
            'run': r['run'], 'workers': r['jobWorkers'], 'frames': len(frames),
            'bracket_mean': statistics.fmean(bracket), 'bracket_median': statistics.median(bracket),
            'bracket_p99': pct(bracket, 0.99),
            'step_median': statistics.median(step), 'step_p99': pct(step, 0.99),
            'alloc_per_tick': statistics.fmean(alloc),
            'faults': r.get('sim', {}).get('faults', 0) + r.get('sim', {}).get('failedSteps', 0),
            'completions': r.get('sim', {}).get('completionsMeasured', r.get('nativeCompletedRuns', 0)),
            'agentFrame': r.get('agentFrameStats'),
            'alloc_window_bytes': r.get('allocWindowBytes'),
        })
    return rows


def fmt(values, digits=1):
    m = statistics.median(values)
    return '%.*f (%.*f–%.*f)' % (digits, m, digits, min(values), digits, max(values))


def main(folders):
    rows = [row for folder in folders for row in per_run(folder)]
    json.dump(rows, open(os.path.join(folders[0], 'sim-runs.json'), 'w'), indent=1)
    keys = []
    for row in rows:
        key = (row['workers'], row['scenario'])
        if key not in keys:
            keys.append(key)
    print('| workers | scenario | runs | Step µs median | Step µs p99 | Step bytes/tick | frame µs mean | frame µs median | frame µs p99 | faults |')
    print('|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    for workers, scenario in keys:
        sel = [r for r in rows if r['workers'] == workers and r['scenario'] == scenario]
        sim = scenario.startswith('sim-')
        print('| %d | `%s` | %d | %s | %s | %s | %s | %s | %s | %d |' % (
            workers, scenario, len(sel),
            fmt([r['step_median'] for r in sel]) if sim else '–',
            fmt([r['step_p99'] for r in sel]) if sim else '–',
            fmt([r['alloc_per_tick'] for r in sel], 0) if sim else '–',
            fmt([r['bracket_mean'] for r in sel]), fmt([r['bracket_median'] for r in sel]),
            fmt([r['bracket_p99'] for r in sel]), sum(r['faults'] for r in sel)))
    print()
    print('ABBA (frame µs, median of per-run means; B = container with default settings, A = NativeTreeAgent cadence 5):')
    print()
    print('| workers | tree | A (native) | B (container) | B/A | B1/A1 | B2/A2 |')
    print('|---:|---|---:|---:|---:|---:|---:|')
    trees = []
    for row in rows:
        tree = row['scenario'].replace('sim-', '').replace('n-', '').replace('-nojobs', '').replace('-cadence5-stagger', '')
        if (row['workers'], tree) not in trees:
            trees.append((row['workers'], tree))
    for workers, tree in trees:
        def means(prefix, labels):
            return [r['bracket_mean'] for r in rows if r['workers'] == workers and r['abba'] in labels
                    and r['scenario'] == prefix]
        a_id = 'n-%s-cadence5-stagger' % tree
        b_id = 'sim-%s' % tree
        a, b = means(a_id, ('A1', 'A2')), means(b_id, ('B1', 'B2'))
        if not a or not b:
            continue
        a1, a2 = means(a_id, ('A1',)), means(a_id, ('A2',))
        b1, b2 = means(b_id, ('B1',)), means(b_id, ('B2',))
        print('| %d | `%s` | %.1f | %.1f | %.2f | %.2f | %.2f |' % (
            workers, tree, statistics.median(a), statistics.median(b), statistics.median(b) / statistics.median(a),
            statistics.median(b1) / statistics.median(a1), statistics.median(b2) / statistics.median(a2)))


if __name__ == '__main__':
    main(sys.argv[1:])
