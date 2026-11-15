# P10-003 profiling aid (temporary edit, never commit its output).
# Wraps every BurstGeneratedRuntimeBridge entry point in SchedulerFrameProfileV1 bridge laps so a
# profile Player (-DefineSymbols AIBT_SCHEDULER_FRAME_PROFILE) splits the "Body" step into generated
# code and bridge categories (BodyGenerated, BridgeFrame, BridgeContext, BridgeComplete, BridgeAccess).
# Usage: python instrument_bridge_laps.py <AIBT package root>; build the profile Player; then
# restore the file with `git checkout -- Runtime/Nodes/Contracts/BurstGeneratedRuntimeBridge.cs`.
# The laps cost about 0.4-0.5 us per dispatch themselves.
import re
import sys
root = sys.argv[1].rstrip('/') + '/' if len(sys.argv) > 1 else 'Assets/AIBT/'
p=root+'Runtime/Nodes/Contracts/BurstGeneratedRuntimeBridge.cs'
s=open(p,encoding='utf-8').read()
def cat(name):
    if name in ('TryGetCatalogHandshake','TryRejectBatch','TryGetExecutionRequest','TryGetExecutionResult','TryPrepareSchedule','TryAcquireDispatchFrame'): return 'BridgeFrame'
    if name.startswith('TryCreate') and name.endswith('Context') or name in ('TryGetAbortReason','TryGetExitReason','TryCreateConfigurationReader','TryCreateMemoryAccessor'): return 'BridgeContext'
    if name in ('TryCompleteEnter','TryCompleteTick','TryCompleteAbort','TryCompleteExit','TryCompleteObserver','TryFailDispatch'): return 'BridgeComplete'
    return 'BridgeAccess'
P='AIBT.SchedulerFrameProfileV1'
n=0
def rep(m):
    global n; n+=1
    head,name,expr=m.group(1),m.group(2),m.group(3)
    return f'{head}{{ {P}.BridgeEnter(); var r = {expr}; {P}.BridgeExit({P}.Step.{cat(name)}); return r; }}'
s2=re.sub(r'(public static BurstContextResult (\w+)(?:<[^>]*>)?\([^\n]*?\)(?: where [^\n]*?)?) => ([^\n]*?);\n', lambda m: rep(m)+'\n', s)
s2=s2.replace('''        {
            return BurstDispatchBridgeCoreV2.TryGetExecutionRequest(in batch, out instanceOrdinal, out runtimeNodeIndex, out catalogCaseIndex, out phase, out hasWork);''','''        {
            AIBT.SchedulerFrameProfileV1.BridgeEnter(); var r = BurstDispatchBridgeCoreV2.TryGetExecutionRequest(in batch, out instanceOrdinal, out runtimeNodeIndex, out catalogCaseIndex, out phase, out hasWork); AIBT.SchedulerFrameProfileV1.BridgeExit(AIBT.SchedulerFrameProfileV1.Step.BridgeFrame); return r;''')
print(n)
open(p,'w',encoding='utf-8',newline='').write(s2)
