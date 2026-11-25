# P12-004 evidence — Burst path tests independent of a warm Burst cache

Setup on 2026-11-25: Modules on Unity 7000.0.0a8, `com.unity.burst` 2.0.0 resolved as built-in
(`Packages/packages-lock.json`). Every run used `Tools/Unity/Invoke-UnityTests.ps1 -Filter BurstExecutionPathTests`,
one run at a time.

## What Burst 2.0.0 is here, and what it does with the option

- **The package is a shim.** `Library/PackageCache/com.unity.burst@626e66ab421b` holds only `package.json`
  (`"type": "shim"`, version 2.0.0) and `Runtime/TypeForwarders.cs`, which forwards `BurstCompiler`,
  `BurstCompilerOptions`, `BurstCompileAttribute` and the rest to the Editor's own modules. Read in the running
  Editor with `script eval`: `BurstCompilerOptions` lives in `UnityEngine.BurstModule`
  (`Editor/Data/Managed/UnityEngine/UnityEngine.BurstModule.dll` of 7000.0.0a8), and the editor options in
  `UnityEditor.BurstModule` (`Unity.Burst.Editor.BurstEditorOptions`). The 1.8.29 source cited in the card is
  therefore not what runs; the facts below are read from the running Editor.
- **The option does not trigger a recompilation.** The IL of
  `BurstCompilerOptions.set_EnableBurstCompileSynchronously` calls only `OnOptionsChanged`, which invokes
  `OptionsChanged`. `MaybeTriggerRecompilation` (which calls `BurstCompiler.TriggerRecompilation`) is not
  called from that setter. (Call sites listed from the method bodies by reflection; the script was
  `Temp/p12004-burst-setter-calls.cs` of Modules, not kept.)
- **The option is saved.** `OptionsChanged` is `BurstEditorOptions.GlobalOnOptionsChanged`. It calls
  `EditorPrefs.SetBool` for every option and also `SessionState.SetBool`. The EditorPrefs key is the const
  `BurstEditorOptions.EnableBurstCompileSynchronouslyText` = `BurstCompileSynchronously`, as in 1.8.29. So an
  Editor that dies between the class's set-up and its teardown keeps "Synchronous Compilation" checked.
- **The JIT cache is `Library/BurstCache/JIT`**, as in 1.8.29: `BurstCompilerOptions.DefaultCacheFolder` =
  `C:\UnityProjects\Modules\Library\BurstCache\JIT`. After the runs it held 68 files at the top level and 699
  counting subfolders. A new cache starts with 46 files (orchestrator's count, top level).

## Runs

### Cold A: the old test (orchestrator)

Raw: `C:\UnityProjects\_aibt_acceptance\P12-004\cold-A-old-test.txt` (UTF-16). The Editor was started with
`Library/BurstCache/JIT` moved aside, on `f8ede04` with a clean tree, and this class was the first run of the
session.

- 22:46:16 – 22:50:00: `BurstExecutionPathTests 1/3 (failed=2 skipped=0)`.
- `EveryPath_ReportsFromInsideTheNodeBody_WhetherItRunsBurstCompiled`: `agent-frame-job x1: managed (6000 frames,
  2,3 s, first Burst frame none, managed frames 5999, Burst frames 0), expected Burst`.
- `RandomFloat3InRadius_3D_IsBitIdentical_BurstCompiledAndManaged`: `the JobsAgentFrame job ran the node body
  Burst-compiled within the bound (1810 frames, 90,1 s)` was false.
- `RandomFloat3InRadius_Flat_…` passed. It runs last, so by then the job had compiled.

The consumer's failure is reproduced on this setup, in two of their three tests. Two things stand out:

- The 6000-frame cap ended the first wait after 2.3 s.
- The asynchronous compilation of the agent-frame job then took longer than the 90 s of the 3D test as well.

### Cold B: the changed test (orchestrator)

Raw: `C:\UnityProjects\_aibt_acceptance\P12-004\cold-B-fixed-test.txt` (UTF-16). Same procedure, on the
working tree with this item's edit (`dirty=11`).

- 22:54:59 – 22:58:54: `BurstExecutionPathTests 3/3 (failed=0 skipped=0)`.
- Logged table (Console seq 9; it is the first run of that session, in the Editor I then used):
  ```
  P11-014 item 1, execution paths (Burst enabled: True, synchronous Burst compilation on for this class):
  agent-frame-job x1: Burst (11 frames, 173,0 s, first Burst frame 1, managed frames 0, Burst frames 10)
  agent-frame-in-place x1: managed (162 frames, 8,0 s, first Burst frame none, managed frames 161, Burst frames 0)
  immediate x1: managed (162 frames, 8,1 s, first Burst frame none, managed frames 161, Burst frames 0)
  batched-jobs-same-frame x1: managed (156 frames, 8,1 s, first Burst frame none, managed frames 155, Burst frames 0)
  batched-jobs-same-frame x4: managed (153 frames, 8,1 s, first Burst frame none, managed frames 152, Burst frames 0)
  pipelined-jobs x1: managed (153 frames, 8,0 s, first Burst frame none, managed frames 8, Burst frames 0)
  pipelined-jobs x4: managed (155 frames, 8,1 s, first Burst frame none, managed frames 8, Burst frames 0)
  P11-014 item 1 parity (3D): 240 frames, 240 distinct points, 0 differ bit for bit; warm-up reached Burst at frame 1
  P11-014 item 1 parity (flat): 240 frames, 240 distinct points, 0 differ bit for bit; warm-up reached Burst at frame 1
  ```
- **Synchronous compilation blocks the first frame.** The agent-frame path was Burst from its first frame,
  but its 11 frames took 173 s: the first schedule waited for the cold compilation on the main thread. That
  time is inside the test's 900 s timeout. It does not depend on `CompileWaitSeconds` = 300, because the
  probe only checks the bound after a frame. A machine slower than this one by more than about five times
  would hit the timeout. The six managed paths stayed managed with the option on, which shows the probe
  still sees managed bodies.

### Warm: the changed test, second run in the same domain (this session)

The Editor of cold B was still running (`codeLoadCount` 1, started 22:54:41, no domain reload since). The
cache was the one cold B filled. Before this run, in the same session:
`TreeSimulationSquadRowsTests`, the P12-005 classes, and a no-op recompile (`up_to_date`).

- 23:03:12 – 23:04:03: `testName BurstExecutionPathTests 3/3 (failed=0 skipped=0)`.
  - `EveryPath…` took 49.0 s.
  - Each parity test took 0.5 s.
- Logged table (Console seq 44):
  ```
  P11-014 item 1, execution paths (Burst enabled: True, synchronous Burst compilation on for this class):
  agent-frame-job x1: Burst (11 frames, 0,5 s, first Burst frame 1, managed frames 0, Burst frames 10)
  agent-frame-in-place x1: managed (150 frames, 8,1 s, first Burst frame none, managed frames 149, Burst frames 0)
  immediate x1: managed (150 frames, 8,1 s, first Burst frame none, managed frames 149, Burst frames 0)
  batched-jobs-same-frame x1: managed (150 frames, 8,1 s, first Burst frame none, managed frames 149, Burst frames 0)
  batched-jobs-same-frame x4: managed (151 frames, 8,1 s, first Burst frame none, managed frames 150, Burst frames 0)
  pipelined-jobs x1: managed (151 frames, 8,1 s, first Burst frame none, managed frames 8, Burst frames 0)
  pipelined-jobs x4: managed (155 frames, 8,1 s, first Burst frame none, managed frames 8, Burst frames 0)
  P11-014 item 1 parity (3D): 240 frames, 240 distinct points, 0 differ bit for bit; warm-up reached Burst at frame 1
  P11-014 item 1 parity (flat): 240 frames, 240 distinct points, 0 differ bit for bit; warm-up reached Burst at frame 1
  ```

## The option before and after the class

Read with `script eval` (the script `Temp/p12004-burst-state.cs` of Modules; not kept) right before and
right after the warm run:

| | `BurstCompiler.Options.EnableBurstCompileSynchronously` | EditorPrefs `BurstCompileSynchronously` | SessionState `BurstCompileSynchronously` | Burst enabled |
| --- | --- | --- | --- | --- |
| 23:03:00, before | False | False | not set | True |
| 23:04:15, after | False | False | not set | True |

The log line says "on for this class", so the class turned the option on and the teardown turned it back
off; EditorPrefs went back to False. In the same Editor, cold B's run had already turned it on and off once:
the "before" row is what B's teardown left.

## The P12-002 stale Burst view on Burst 2.0.0 of 7000.0.0a8

- Not re-provoked: the finding needs a change to an internal struct in a running Editor, and runtime code is
  not changed in this item.
- What was checked in this Editor session:
  - `Logs/Editor.log` has no `Burst internal compiler error`, no `Failed to resolve field`, no
    `Error while hashing` and no `BCxxxx` line.
  - The Console's only errors are the two that `BehaviorTreeAgentComponentTests` raises on purpose (a node
    that throws).
  - The probe's "ran managed" signal shows the current code on Burst:
    - this class's agent-frame path, first frame Burst;
    - P12-002's `AgentFrameJob_RunsTheIndexedRoutineBurstCompiled`, which passed with
      `10 steps, 0,5 s, first Burst step 1, managed steps 0, Burst steps 10`, every step's orders being values
      only the indexed routine gives.
- Whether Burst 2.0.0 of 7000.0.0a8 keeps a stale view after an internal struct change is therefore not
  established either way. The rule of P12-002 stands: after such a change, check the "ran managed" signal
  before trusting a jobs result.

## After a recompile of the test assembly (same session)

At 23:05 a doc comment of this class was edited, and `AIBT.Integration.Tests` was recompiled; this class was
not rerun. P12-002's S1 test runs without synchronous compilation, and it is in the same assembly.

- The run that started at 23:07:47 gave `1790 steps, 90,0 s, first Burst step none, managed steps 1790`:
  Inconclusive, as that test is designed to report.
- The rerun at 23:11:13 gave `10 steps, 0,5 s, first Burst step 1, managed steps 0`.

So the recompiled assembly's agent-frame job took between 90 s and about 206 s to compile asynchronously after
its first schedule. This is the same order as cold B's 173 s. No Burst error was logged. For a full run right
after a recompile, the jobs tests that do not compile synchronously may therefore run managed for their first
minutes.

## Acceptance against the card

- 3/3 cold (B) and 3/3 warm. The warm run was in the same domain as the cold one, not in a freshly started
  Editor with a warm cache; that case was not run.
- Option and EditorPrefs value the same before and after: yes (table above).
- The class still fails when the agent-frame path runs managed: the verdict rule is unchanged (diff). The
  six managed paths are reported managed with the option on. The failing branch was not exercised with the
  new test.
- Burst off (`Assume`): not run.

## Observations, not explained

- After cold run A, the Editor needed more than three minutes to exit after `EditorApplication.Exit(0)`
  (`cold-B-fixed-test.txt`, 22:50:58 – 22:54:02). The orchestrator killed nothing; the cause is unknown.
- The consumer's "Burst 1.8.29" on 7000.0.0a8: in Modules the same manifest request resolves to built-in
  2.0.0. Their resolved version is still to be asked.
