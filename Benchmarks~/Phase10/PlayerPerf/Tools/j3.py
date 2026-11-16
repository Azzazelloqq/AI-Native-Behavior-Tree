"""P10-005 (stage J3) analysis: per scenario, N and job worker count, across one or more run folders.

    python j3.py <out.json> <run folder> [<run folder> ...]

For every runs.jsonl line (one scenario, N and run) it reads the frame CSV and computes the mean
main-thread bracket, p99 and max bracket, mean frame wall time (frame_dt_us), and from the P10-005
CPU window the process CPU and main-thread CPU per measured frame. Across runs (and folders) of the
same (scenario, N, jobWorkers) it keeps every run mean and reports the median, min and max.
Columns that the Player did not write (an older Player) come out as null.
"""
import csv
import json
import os
import statistics
import sys


def pct(values, p):
    values = sorted(values)
    if not values:
        return None
    k = (len(values) - 1) * p / 100.0
    f = int(k)
    c = min(f + 1, len(values) - 1)
    return values[f] + (values[c] - values[f]) * (k - f)


def load(folder):
    rows = []
    for line in open(os.path.join(folder, 'runs.jsonl'), encoding='utf-8'):
        if not line.strip():
            continue
        r = json.loads(line)
        name = '%s_n%d_r%d.csv' % (r['scenario'], r['n'], r['run'])
        frames = list(csv.DictReader(open(os.path.join(folder, name), encoding='utf-8')))
        bracket = [float(f['bracket_us']) for f in frames]
        dt = [float(f['frame_dt_us']) for f in frames]
        sched = [float(f['scheduler_drive_us']) for f in frames]
        feeder = [float(f['feeder_us']) for f in frames]
        measured = r.get('measuredFrames') or len(frames)
        proc = r.get('cpuWindowProcessUs')
        main = r.get('cpuWindowMainThreadUs')
        wall = r.get('cpuWindowWallUs')
        stats = r.get('agentFrameStats') or {}
        rows.append({
            'scenario': r['scenario'], 'n': r['n'], 'workers': r.get('jobWorkers'), 'run': r['run'], 'folder': os.path.basename(folder.rstrip('/\\')),
            'mean': statistics.fmean(bracket), 'p99': pct(bracket, 99), 'max': max(bracket),
            'dt': statistics.fmean(dt), 'frames': len(frames), 'sched': statistics.fmean(sched), 'feeder': statistics.fmean(feeder),
            'proc_cpu': proc / measured if proc is not None and proc >= 0 else None,
            'main_cpu': main / measured if main is not None and main >= 0 else None,
            'wall': wall / measured if wall else None,
            'faults': r.get('faultsTotal'),
            'af_agents': stats.get('agents'), 'af_batches': stats.get('batches'),
            'af_on_workers': stats.get('agentsOnWorkers'), 'af_managed': stats.get('managedAgents'),
            'af_job_us': stats.get('jobMicroseconds'), 'af_dispatches': stats.get('dispatches'),
            'running': r.get('running'), 'completed': r.get('nativeCompletedRuns'), 'effects': r.get('effects'),
            'alloc_window_bytes': r.get('allocWindowBytes'), 'alloc_window_gc': r.get('allocWindowGc'),
        })
    return rows


def med(values):
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


def aggregate(rows):
    groups = {}
    for r in rows:
        groups.setdefault((r['scenario'], r['n'], r['workers']), []).append(r)
    out = []
    for (scenario, n, workers), rs in sorted(groups.items(), key=lambda kv: (kv[0][0], kv[0][1], kv[0][2] or 0)):
        means = [r['mean'] for r in rs]
        frames = sum(r['frames'] for r in rs)
        afa = sum(r['af_agents'] or 0 for r in rs)
        out.append({
            'scenario': scenario, 'n': n, 'workers': workers, 'runs': len(rs),
            'mean_us': med(means), 'mean_min_us': min(means), 'mean_max_us': max(means),
            'p99_us': med([r['p99'] for r in rs]), 'max_us': max(r['max'] for r in rs),
            'frame_dt_us': med([r['dt'] for r in rs]),
            'scheduler_drive_us': med([r['sched'] for r in rs]), 'feeder_us': med([r['feeder'] for r in rs]),
            'process_cpu_us_per_frame': med([r['proc_cpu'] for r in rs]),
            'main_thread_cpu_us_per_frame': med([r['main_cpu'] for r in rs]),
            'faults_max': max((r['faults'] or 0) for r in rs),
            'agentframe_agents': afa or None,
            'agentframe_on_workers_share': (sum(r['af_on_workers'] or 0 for r in rs) / afa) if afa else None,
            'agentframe_managed_agents': sum(r['af_managed'] or 0 for r in rs) if afa else None,
            'agentframe_job_us_per_frame': (sum(r['af_job_us'] or 0 for r in rs) / frames) if afa else None,
            'agentframe_agents_per_frame': (afa / frames) if afa else None,
            'running_min': min((r['running'] if r['running'] is not None else -1) for r in rs),
            'alloc_window_bytes_max': max((r['alloc_window_bytes'] or 0) for r in rs),
            'folders': sorted(set(r['folder'] for r in rs)),
        })
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    rows = []
    for folder in sys.argv[2:]:
        rows.extend(load(folder))
    out = aggregate(rows)
    json.dump(out, open(sys.argv[1], 'w', encoding='utf-8'), indent=1)
    for s in out:
        print('%-44s %6d w=%-3s runs=%d mean %10.1f [%.1f..%.1f] dt %10.1f proc %s main %s faults %d' % (
            s['scenario'], s['n'], s['workers'], s['runs'], s['mean_us'], s['mean_min_us'], s['mean_max_us'], s['frame_dt_us'],
            '%.1f' % s['process_cpu_us_per_frame'] if s['process_cpu_us_per_frame'] is not None else '-',
            '%.1f' % s['main_thread_cpu_us_per_frame'] if s['main_thread_cpu_us_per_frame'] is not None else '-', s['faults_max']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
