"""P10-002 quick mode: summarize a quick run and compare it with a saved quick baseline.

    python quick.py summarize <run folder>                 # writes quick.json next to the raw data
    python quick.py compare <baseline quick.json> <run folder or quick.json> [--threshold 5]

A scenario counts as single-threaded when its id has no '-jobs' suffix (native default profile,
Immediate, and the managed controls). compare prints a before/after table of the mean main-thread
time per frame and exits with code 2 when a single-threaded scenario got slower by more than the
threshold (percent, default 5).
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analyze  # noqa: E402


def summarize(folder):
    summary = analyze.aggregate(analyze.load(folder))
    env = {}
    env_path = os.path.join(folder, 'environment.json')
    if os.path.exists(env_path):
        env = json.load(open(env_path, encoding='utf-8'))
    out = {'folder': os.path.abspath(folder), 'environment': env, 'scenarios': summary}
    json.dump(out, open(os.path.join(folder, 'quick.json'), 'w'), indent=1)
    return out


def load_quick(path):
    if os.path.isdir(path):
        q = os.path.join(path, 'quick.json')
        return json.load(open(q, encoding='utf-8')) if os.path.exists(q) else summarize(path)
    return json.load(open(path, encoding='utf-8'))


def single_threaded(scenario):
    return not scenario.endswith('-jobs') and not scenario.endswith('-pipelined') and not scenario.endswith('-agentframe')


def fmt(v, digits=0):
    if v is None:
        return '-'
    return ('{:,.%df}' % digits).format(v)


def compare(base, new, threshold):
    index = {(s['scenario'], s['n']): s for s in base['scenarios']}
    rows = []
    flagged = []
    for s in new['scenarios']:
        b = index.get((s['scenario'], s['n']))
        if b is None:
            continue
        change = 100.0 * (s['mean_us'] - b['mean_us']) / b['mean_us']
        st = single_threaded(s['scenario'])
        flag = st and change > threshold
        if flag:
            flagged.append((s['scenario'], s['n'], change))
        rows.append((s['scenario'], s['n'], b, s, change, st, flag))
    rows.sort(key=lambda r: (r[0], r[1]))
    print('| scenario | N | before µs/frame | after µs/frame | change | before spread | after spread | before B/agent-tick | after B/agent-tick | thread | flag |')
    print('|---|---:|---:|---:|---:|---|---|---:|---:|---|---|')
    for scenario, n, b, s, change, st, flag in rows:
        print('| %s | %s | %s | %s | %+.1f%% | %s..%s | %s..%s | %s | %s | %s | %s |' % (
            scenario, fmt(n), fmt(b['mean_us']), fmt(s['mean_us']), change,
            fmt(b['mean_min_us']), fmt(b['mean_max_us']), fmt(s['mean_min_us']), fmt(s['mean_max_us']),
            fmt(b['alloc_bytes_per_agent_tick'], 1), fmt(s['alloc_bytes_per_agent_tick'], 1),
            'single' if st else 'jobs', 'SLOWER >%g%%' % threshold if flag else ''))
    print()
    if flagged:
        print('FLAGGED single-threaded scenarios slower than %g%%:' % threshold)
        for scenario, n, change in flagged:
            print('  %s N=%d %+.1f%%' % (scenario, n, change))
        return 2
    print('No single-threaded scenario slower than %g%%.' % threshold)
    return 0


def main():
    if len(sys.argv) >= 3 and sys.argv[1] == 'summarize':
        out = summarize(sys.argv[2])
        for s in out['scenarios']:
            print('%-32s %6d mean %10.0f spread %8.0f..%-8.0f B/agent-tick %s' % (
                s['scenario'], s['n'], s['mean_us'], s['mean_min_us'], s['mean_max_us'], fmt(s['alloc_bytes_per_agent_tick'], 1)))
        return 0
    if len(sys.argv) >= 4 and sys.argv[1] == 'compare':
        threshold = 5.0
        if '--threshold' in sys.argv:
            threshold = float(sys.argv[sys.argv.index('--threshold') + 1])
        return compare(load_quick(sys.argv[2]), load_quick(sys.argv[3]), threshold)
    print(__doc__)
    return 1


if __name__ == '__main__':
    sys.exit(main())
