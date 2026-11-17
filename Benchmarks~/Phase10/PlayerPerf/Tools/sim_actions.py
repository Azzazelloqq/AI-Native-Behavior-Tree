"""P10-011: tables for the `sim-actions` set (TreeSimulation with external actions, stage G3).

usage: python sim_actions.py <run folder> [<run folder> ...] > sim-actions-table.md

Per run of a scenario (one CSV): over the measured frames, the median and p99 of `sim_step_us`
(the TreeSimulation.Step call alone; the game's input writes, completions and event and command
reads are outside it), the mean of `sim_step_alloc_bytes` (managed bytes allocated by Step per
tick), the means of `sim_complete_us` (the game's TryComplete calls before Step) and
`sim_events_us` (reading the action events after Step), and the mean of `bracket_us` (main-thread
time of the whole frame). Per scenario: the median of the per-run values, min-max in brackets.
Events per tick come from runs.jsonl (`(sim.startedMeasured + sim.cancelledMeasured) /
sim.ticksMeasured`), commands per tick likewise. ABBA inside each run: B = the G2 io-* tree, E =
its act-* twin; E/B is the ratio of the medians of the per-run Step medians, and per half
(E1/B1, E2/B2).
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
        if not r['scenario'].startswith('sim-') or r.get('abba', '') not in ('B1', 'B2', 'E1', 'E2'):
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
            'complete_mean': statistics.fmean(float(f.get('sim_complete_us') or 0) for f in frames),
            'events_mean': statistics.fmean(float(f.get('sim_events_us') or 0) for f in frames),
            'bracket_mean': statistics.fmean(float(f['bracket_us']) for f in frames),
            'events_per_tick': ((sim.get('startedMeasured', 0) + sim.get('cancelledMeasured', 0)) / ticks) if ticks else 0.0,
            'commands_per_tick': (sim.get('commandsMeasured', 0) / ticks) if ticks else 0.0,
            'faults': sim.get('faults', 0) + sim.get('failedSteps', 0) + sim.get('completeFailures', 0),
            'alloc_window_bytes': r.get('allocWindowBytes'),
        })
    return rows


def fmt(values, digits=1):
    m = statistics.median(values)
    return '%.*f (%.*f–%.*f)' % (digits, m, digits, min(values), digits, max(values))


def main(folders):
    rows = [row for folder in folders for row in per_run(folder)]
    json.dump(rows, open(os.path.join(folders[0], 'sim-actions-runs.json'), 'w'), indent=1)
    keys = []
    for row in rows:
        key = (row['workers'], row['scenario'])
        if key not in keys:
            keys.append(key)
    print('| workers | scenario | runs | Step µs median | Step µs p99 | Step bytes/tick | complete µs | events µs | frame µs mean | events/tick | commands/tick | GC window bytes | faults |')
    print('|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for workers, scenario in keys:
        sel = [r for r in rows if r['workers'] == workers and r['scenario'] == scenario]
        print('| %d | `%s` | %d | %s | %s | %s | %s | %s | %s | %s | %s | %s | %d |' % (
            workers, scenario, len(sel), fmt([r['step_median'] for r in sel]), fmt([r['step_p99'] for r in sel]),
            fmt([r['alloc_per_tick'] for r in sel], 0), fmt([r['complete_mean'] for r in sel]), fmt([r['events_mean'] for r in sel]),
            fmt([r['bracket_mean'] for r in sel]), fmt([r['events_per_tick'] for r in sel], 1), fmt([r['commands_per_tick'] for r in sel], 1),
            max(r['alloc_window_bytes'] or 0 for r in sel), sum(r['faults'] for r in sel)))
    print()
    print('ABBA (Step µs, median of per-run medians; B = G2 io-* tree, E = act-* twin with an external action):')
    print()
    print('| workers | pair | B | E | E/B | E1/B1 | E2/B2 |')
    print('|---:|---|---:|---:|---:|---:|---:|')
    pairs = []
    for row in rows:
        if row['abba'].startswith('E'):
            key = (row['workers'], row['scenario'])
            if key not in pairs:
                pairs.append(key)
    for workers, e_id in pairs:
        b_id = e_id.replace('sim-act-', 'sim-io-')

        def med(scenario, labels):
            values = [r['step_median'] for r in rows if r['workers'] == workers and r['scenario'] == scenario and r['abba'] in labels]
            return statistics.median(values) if values else float('nan')

        b, e = med(b_id, ('B1', 'B2')), med(e_id, ('E1', 'E2'))
        print('| %d | `%s` vs `%s` | %.1f | %.1f | %.2f | %.2f | %.2f |' % (
            workers, e_id, b_id, b, e, e / b, med(e_id, ('E1',)) / med(b_id, ('B1',)), med(e_id, ('E2',)) / med(b_id, ('B2',))))


if __name__ == '__main__':
    main(sys.argv[1:])
