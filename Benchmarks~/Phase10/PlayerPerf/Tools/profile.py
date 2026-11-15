"""P10-002 frame profile: per-stage scheduler time per frame from a Player built with
AIBT_SCHEDULER_FRAME_PROFILE (Invoke-Perf.ps1 -Step Build -DefineSymbols AIBT_SCHEDULER_FRAME_PROFILE).

    python profile.py <run folder>

Prints, per scenario and N, the median over runs of microseconds per frame for each stage and the
counters per frame (rounds, lanes per round, ...). The stages are laps of one timer, so they add up
to the instrumented part of ProductionTreeScheduler.Update.
"""
import json
import os
import statistics
import sys
from collections import defaultdict

STAGES = ['FrameStart', 'WorldBridge', 'DueCollect', 'Bookkeeping', 'HostBegin', 'HostAdvance', 'HostRecord',
          'HostDispatch', 'HostComplete', 'GroupSetup', 'RoundBegin', 'RoundAllocate', 'RoundSchedule',
          'RoundComplete', 'RoundDispose', 'RoundHandle', 'RoundResolve']


def main():
    folder = sys.argv[1]
    groups = defaultdict(list)
    for line in open(os.path.join(folder, 'runs.jsonl'), encoding='utf-8'):
        if not line.strip():
            continue
        r = json.loads(line)
        p = r.get('frameProfile')
        if not p or not p.get('Frames'):
            continue
        frames = p['Frames']
        row = {k: p[k + 'Us'] / frames for k in STAGES}
        row['total'] = sum(row[k] for k in STAGES)
        row['rounds'] = p['Rounds'] / frames
        row['lanesPerRound'] = p['Lanes'] / p['Rounds'] if p['Rounds'] else 0.0
        row['dispatchPerFrame'] = p['DispatchMembers'] / frames
        row['immediateHosts'] = p['ImmediateHosts'] / frames
        groups[(r['scenario'], r['n'])].append(row)
    keys = STAGES + ['total', 'rounds', 'lanesPerRound', 'dispatchPerFrame', 'immediateHosts']
    out = []
    for (scenario, n), rows in sorted(groups.items()):
        med = {k: statistics.median(r[k] for r in rows) for k in keys}
        med['scenario'] = scenario
        med['n'] = n
        med['runs'] = len(rows)
        out.append(med)
    json.dump(out, open(os.path.join(folder, 'profile.json'), 'w'), indent=1)
    shown = [k for k in STAGES if any(o[k] >= 0.5 for o in out)]
    print('| scenario | N | ' + ' | '.join(shown) + ' | total µs | rounds/frame | lanes/round | dispatches/frame |')
    print('|---|---:|' + '---:|' * (len(shown) + 4))
    for o in out:
        print('| %s | %d | %s | %.0f | %.2f | %.0f | %.0f |' % (
            o['scenario'], o['n'], ' | '.join('%.0f' % o[k] for k in shown), o['total'], o['rounds'],
            o['lanesPerRound'], o['dispatchPerFrame']))


if __name__ == '__main__':
    main()
