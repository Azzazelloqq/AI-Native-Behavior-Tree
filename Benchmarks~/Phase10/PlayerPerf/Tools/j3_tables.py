"""P10-005 (stage J3) tables from j3.py summaries.

    python j3_tables.py <summary.json> [<summary.json> ...] > tables.md

Every summary (j3.py output) may hold several worker counts. Rows of the same (scenario, N,
workers) from several summaries are merged by taking the median of their medians, and the
spread is the min..max of every run mean. Tables: small-N crossover, scale, worker count, mixed
population, cadence, and everything else as a flat list.
"""
import io
import json
import statistics
import sys

TREES = [('n-io-wait1', 'io-wait1'), ('n-shared-loop16', 'shared-loop16'),
         ('n-shared-reactive16', 'shared-reactive16'), ('n-io-move1-fact-effect', 'io-move1-fact-effect')]
POL = [('', 'Immediate'), ('-jobs', 'BatchedJobsSameFrame'), ('-agentframe', 'JobsAgentFrame')]


def load(paths):
    merged = {}
    for p in paths:
        for s in json.load(open(p, encoding='utf-8')):
            merged.setdefault((s['scenario'], s['n'], s['workers']), []).append(s)
    out = {}
    for k, ss in merged.items():
        def m(field):
            v = [s[field] for s in ss if s.get(field) is not None]
            return statistics.median(v) if v else None
        out[k] = {
            'mean': m('mean_us'), 'lo': min(s['mean_min_us'] for s in ss), 'hi': max(s['mean_max_us'] for s in ss),
            'runs': sum(s['runs'] for s in ss), 'dt': m('frame_dt_us'), 'proc': m('process_cpu_us_per_frame'),
            'main': m('main_thread_cpu_us_per_frame'), 'p99': m('p99_us'), 'max': max(s['max_us'] for s in ss),
            'faults': max(s['faults_max'] for s in ss), 'workers_share': m('agentframe_on_workers_share'),
            'job_us': m('agentframe_job_us_per_frame'), 'af_agents': m('agentframe_agents_per_frame'),
            'managed': m('agentframe_managed_agents'),
            'sched': m('scheduler_drive_us'), 'feeder': m('feeder_us'),
        }
    return out


def f(v, d=0):
    return '-' if v is None else ('{:,.%df}' % d).format(v).replace(',', ' ')


def default_workers(data):
    ws = sorted({k[2] for k in data if k[2] is not None})
    return ws[-1] if ws else None


def cell(data, key, digits=0):
    r = data.get(key)
    if r is None:
        return '-'
    return '%s (%s–%s)' % (f(r['mean'], digits), f(r['lo'], digits), f(r['hi'], digits))


def small(data, w):
    ns = sorted({k[1] for k in data if k[1] <= 500 and k[2] == w and any(k[0] == t for t, _ in TREES)})
    print('## Small N (main-thread µs per frame, median of runs, run spread in brackets; workers=%s)\n' % w)
    for t, name in TREES:
        print('### %s\n' % name)
        print('| N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | AF / Imm | AF − Imm µs | frame wall Imm / AF µs |')
        print('|---:|---:|---:|---:|---:|---:|---:|')
        cross = None
        worst = None
        for n in ns:
            imm = data.get((t, n, w)); af = data.get((t + '-agentframe', n, w))
            if not imm or not af:
                continue
            ratio = af['mean'] / imm['mean']
            if worst is None or ratio > worst[0]:
                worst = (ratio, n)
            if ratio <= 0.9 and cross is None:
                cross = n
            elif ratio > 0.9:
                cross = None
            print('| %d | %s | %s | %s | %.2f | %+.1f | %s / %s |' % (
                n, cell(data, (t, n, w), 1), cell(data, (t + '-jobs', n, w), 1), cell(data, (t + '-agentframe', n, w), 1),
                ratio, af['mean'] - imm['mean'], f(imm['dt']), f(af['dt'])))
        print('\nCrossover (JobsAgentFrame ≥10%% faster from this N on, within N ≤ 500): %s. Highest AF / Imm: %s\n' % (
            cross if cross else 'not reached', '%.2f at N = %d' % worst if worst else '-'))


def scale(data, w):
    print('## Scale (workers=%s): main-thread µs per frame / frame wall µs / process CPU µs per frame\n' % w)
    print('| Tree | N | Immediate | BatchedJobsSameFrame | JobsAgentFrame | Imm / AF |')
    print('|---|---:|---|---|---|---:|')
    for t, name in TREES:
        for n in (1000, 5000, 20000):
            cells = []
            for suf, _ in POL:
                r = data.get((t + suf, n, w))
                cells.append('-' if r is None else '%s / %s / %s' % (f(r['mean']), f(r['dt']), f(r['proc'])))
            imm = data.get((t, n, w)); af = data.get((t + '-agentframe', n, w))
            print('| %s | %d | %s | %s | %s | %s |' % (name, n, cells[0], cells[1], cells[2], '%.1f×' % (imm['mean'] / af['mean']) if imm and af else '-'))
    for m in ('m-shared-shape15', 'm-unity15-both'):
        for n in (1000, 5000, 20000):
            r = data.get((m, n, w))
            if r:
                print('| %s (managed) | %d | %s / %s / %s | | | |' % (m, n, f(r['mean']), f(r['dt']), f(r['proc'])))
    print()


def workers(data):
    ws = sorted({k[2] for k in data if k[2] is not None})
    print('## Worker count: JobsAgentFrame main-thread µs per frame (process CPU µs per frame)\n')
    print('| Tree | N | Immediate | ' + ' | '.join('AF, %d workers' % x for x in ws) + ' |')
    print('|---|---:|---:|' + '---:|' * len(ws))
    for t, name in TREES:
        for n in (1000, 5000):
            imm = data.get((t, n, ws[-1])) if ws else None
            row = []
            for x in ws:
                r = data.get((t + '-agentframe', n, x))
                row.append('-' if r is None else '%s (%s)' % (f(r['mean']), f(r['proc'])))
            print('| %s | %d | %s | %s |' % (name, n, f(imm['mean']) if imm else '-', ' | '.join(row)))
    print()


def split(data, w):
    print('## Where the main thread goes under JobsAgentFrame (workers=%s), µs per frame\n' % w)
    print('bracket = whole main-thread bracket; driver = scheduler drive (begin + job + finish of every batch); job = schedule + complete wall time (AgentFrameDiagnosticsV1); begin/finish = driver − job; rest = bracket − driver − feeder (due collection, world bridge, frame entries, component overhead).\n')
    print('| Tree | N | bracket | driver | job | begin/finish | rest | feeder | begin/finish per agent µs | rest per agent µs |')
    print('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
    for t, name in TREES:
        for n in sorted({k[1] for k in data if k[0] == t + '-agentframe' and k[2] == w}):
            r = data.get((t + '-agentframe', n, w))
            if not r or r['sched'] is None or r['job_us'] is None:
                continue
            bf = r['sched'] - r['job_us']
            rest = r['mean'] - r['sched'] - (r['feeder'] or 0)
            print('| %s | %d | %s | %s | %s | %s | %s | %s | %.2f | %.2f |' % (name, n, f(r['mean']), f(r['sched']), f(r['job_us']), f(bf), f(rest), f(r['feeder']), bf / n, rest / n))
    print()


def mixed(data, w):
    print('## Mixed population (workers=%s): half the JobsAgentFrame hosts fall back (StepBudget)\n' % w)
    print('| Tree | N | Immediate | JobsAgentFrame | (Imm+AF)/2 | alternating | vs (Imm+AF)/2 | grouped | vs (Imm+AF)/2 |')
    print('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
    for t, name in TREES:
        for n in (1000, 5000):
            imm = data.get((t, n, w)); af = data.get((t + '-agentframe', n, w)); mx = data.get((t + '-agentframe-mixed', n, w)); gr = data.get((t + '-agentframe-mixed-grouped', n, w))
            if not (imm and af and mx):
                continue
            half = (imm['mean'] + af['mean']) / 2

            def d(r):
                return '-' if r is None else '%+.0f%%' % (100 * (r['mean'] - half) / half)
            print('| %s | %d | %s | %s | %s | %s | %s | %s | %s |' % (name, n, f(imm['mean']), f(af['mean']), f(half),
                                                                f(mx['mean']), d(mx), f(gr['mean']) if gr else '-', d(gr)))
    print()


def cadence(data, w):
    rows = sorted(k for k in data if 'cadence' in k[0] and k[2] == w)
    if not rows:
        return
    print('## UpdateCadence 6 (workers=%s): main-thread µs per frame\n' % w)
    print('| scenario | N | mean | p99 | max | faults |')
    print('|---|---:|---:|---:|---:|---:|')
    for k in rows:
        r = data[k]
        print('| %s | %d | %s | %s | %s | %d |' % (k[0], k[1], f(r['mean']), f(r['p99']), f(r['max']), r['faults']))
    print()


def main():
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
    data = load(sys.argv[1:])
    w = default_workers(data)
    small(data, w)
    scale(data, w)
    workers(data)
    split(data, w)
    mixed(data, w)
    cadence(data, w)
    print('## All rows\n')
    print('| scenario | N | workers | runs | main µs (spread) | frame wall µs | process CPU µs/frame | main-thread CPU µs/frame | AF on workers | AF job µs/frame | faults |')
    print('|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|')
    for k in sorted(data, key=lambda k: (k[0], k[1], k[2] or 0)):
        r = data[k]
        print('| %s | %d | %s | %d | %s | %s | %s | %s | %s | %s | %d |' % (
            k[0], k[1], k[2], r['runs'], cell(data, k, 1), f(r['dt']), f(r['proc']), f(r['main']),
            '-' if r['workers_share'] is None else '%.0f%%' % (100 * r['workers_share']), f(r['job_us']), r['faults']))


if __name__ == '__main__':
    main()
