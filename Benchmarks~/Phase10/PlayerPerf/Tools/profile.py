"""P10-002/P10-003 frame profile: per-stage scheduler time per frame from a Player built with
AIBT_SCHEDULER_FRAME_PROFILE (Invoke-Perf.ps1 -Step Build -DefineSymbols AIBT_SCHEDULER_FRAME_PROFILE).

    python profile.py <run folder>

Prints, per scenario and N, the median over runs of microseconds per frame for each stage and the
counters per frame (rounds, lanes per round, ...). The stages are laps of one timer, so they add up
to the instrumented part of ProductionTreeScheduler.Update.

P10-003 adds a second table: the steps inside one generated dispatch (Step* fields), in microseconds
per dispatch. Dispatches per frame are the single-host dispatches (SingleDispatches) plus the group
members (DispatchMembers). The steps are laps of a second timer nested inside HostDispatch/HostComplete
(Immediate) or RoundResolve (Jobs).
"""
import json
import os
import statistics
import sys
from collections import defaultdict

STAGES = ['FrameStart', 'WorldBridge', 'DueCollect', 'Bookkeeping', 'HostBegin', 'HostAdvance', 'HostRecord',
          'HostDispatch', 'HostComplete', 'GroupSetup', 'RoundBegin', 'RoundAllocate', 'RoundSchedule',
          'RoundComplete', 'RoundDispose', 'RoundHandle', 'RoundResolve']
STEPS = ['Reserve', 'Enter', 'Activation', 'Snapshot', 'RequestBuild', 'BeginValidate', 'BeginClear', 'AcquireBatch',
         'Body', 'Consume', 'Commit', 'Publish', 'Acknowledge', 'Reset', 'Return', 'EffectCapture', 'MachineComplete',
         'TraceComplete', 'GroupScan', 'GroupParticipant', 'GroupClear', 'GroupPack', 'GroupValidate', 'GroupReserve',
         'GroupValidateCommits', 'GroupPublish', 'GroupResume', 'BodyGenerated', 'BridgeFrame', 'BridgeContext', 'BridgeComplete', 'BridgeAccess']


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
        dispatches = p.get('SingleDispatches', 0) + p['DispatchMembers']
        row['dispatches'] = dispatches / frames
        for k in STEPS:
            row['step' + k] = p.get('Step' + k + 'Us', 0.0) / dispatches if dispatches else 0.0
        row['stepTotal'] = sum(row['step' + k] for k in STEPS)
        groups[(r['scenario'], r['n'])].append(row)
    keys = STAGES + ['total', 'rounds', 'lanesPerRound', 'dispatchPerFrame', 'immediateHosts', 'dispatches',
                     'stepTotal'] + ['step' + k for k in STEPS]
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
    shown = [k for k in STEPS if any(o['step' + k] >= 0.005 for o in out)]
    if not shown:
        return
    print()
    print('µs per dispatch (P10-003 steps); Body share = the catalog ExecuteImmediate call (Body + BodyGenerated + Bridge* when bridge laps are on)')
    print('| scenario | N | ' + ' | '.join(shown) + ' | sum µs | Body share | dispatches/frame |')
    print('|---|---:|' + '---:|' * (len(shown) + 3))
    for o in out:
        body = sum(o['step' + k] for k in ('Body', 'BodyGenerated', 'BridgeFrame', 'BridgeContext', 'BridgeComplete', 'BridgeAccess'))
        share = body / o['stepTotal'] if o['stepTotal'] else 0.0
        print('| %s | %d | %s | %.2f | %.0f%% | %.0f |' % (
            o['scenario'], o['n'], ' | '.join('%.2f' % o['step' + k] for k in shown), o['stepTotal'], 100 * share,
            o['dispatches']))


if __name__ == '__main__':
    main()
