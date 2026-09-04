# P7-030 verification

Implemented after owner acceptance of [the proposal](implementation-proposal.md), against
`fa14161`, in Unity 6000.5.8f1 at `C:/UnityProjects/Modules`, 2026-09-04. No commit or push.
The card's integration acceptance passed; the full host-project suite still has three unrelated
baseline failures. Exact results and live observations are in [verification-results.json](verification-results.json).

## Delivered behavior

- Completed Success/Failure roots stop and retain the result. The old Tick-only overload remains.
- Full lifecycle dispatch receives actual Enter/Tick/Abort/Exit calls, frozen logical update
  identity/time, and actual leaf Exit/Abort reasons. Adapter-owned state is initialized before Tick.
- Default scaled Unity time, or controlled nonnegative/nondecreasing microseconds, reaches the
  native Timeout/Cooldown implementation. Time is read once per logical update.
- Immediate and per-frame step budgeting use one loop and the existing native budget driver.
  Zero budget makes no progress; resume preserves machine state, clock and logical update ID.
- Disable pauses; enable resumes. Destruction drains cancellation and Exit without the normal
  budget limit, then releases native ownership. Reentrant destruction waits for dispatch completion.
  Faulted callbacks stop once and are not invoked again during teardown.
- Detailed traces expose the existing BudgetYielded/ExecutionResumed events and release writer
  leases between segments. Root-leaf completion no longer emits a duplicate Exit.

The native result now exposes reasons already held by the machine. A narrow internal teardown
option promotes pending reactive/timeout cancellation to TreeStopped; ordinary abort preconditions
remain intact. This was needed when destruction begins an update that queues reactive cancellation.
See the implementation note in ADR-P7-010. Runtime has no new assembly dependencies, executor,
compiled format or node ABI. Automatic generated-node workspace construction remains caller work.

## Automated verification

1. Before the host fix, the two new terminal-root cases failed (expected one logical update,
   observed three); four existing host tests passed. Red job: `936e339479c54c8a934b53520d077453`.
2. Focused host/native lifecycle/decorator/trace run: **55/55 passed**, including **20 host tests**
   (16 added). Covers both terminal statuses, callback initialization and Exit reasons, Timeout
   deadline 110 after 10/109, live-tree Cooldown blocked at 20 and allowed at 110, budget identity
   and readable snapshots, disable/resume/destruction, terminal-pending Exit, destruction inside
   dispatch, callback exception, invalid clock and duplicate bootstrap.
3. Full EditMode, no assembly filter: **1666/1669 passed, 3 failed, 0 skipped**. AIBT alone:
   **1299/1301 passed**. The exact failing names are recorded in JSON. Two CodeGen tests assume
   a package-owned test assembly and receive null PackageInfo in this Assets layout; the third
   is LocalSaveSystem autosave (expected 9, actual 0). The same failure families were independently
   recorded in P7-018 and P7-028 before these changes; those implementations were not modified.
4. Script compilation succeeded. Generated API documentation was regenerated through the existing
   MCP documentation command; its drift tests are included in the full run. Static verification:
   **7 schemas, 137 work items passed**. `git diff --check` passed.

Unity MCP occasionally lost test callbacks and reported a running job with zero progress even
after Unity completed. Focused counts were read from the freshly timestamped
`C:/Users/User/AppData/LocalLow/DefaultCompany/Modules/TestResults.xml` before clearing that orphaned
MCP job. The full run had all 1669 progress callbacks and matching XML. Tool status alone was not
treated as test evidence. EditMode fixture teardown explicitly invokes OnDestroy because a
component that never received Awake does not reliably receive Unity destruction callbacks there.
The Play-mode verification below uses actual Unity lifecycle callbacks.

## Actual Play-mode verification

Created a temporary host during Play mode, using the existing two-node test program and a
caller-owned lifecycle adapter. Unity drove Update through its player loop; no reflection invoked
Update. An Editor update handler requested player-loop updates, and Run In Background was enabled
during the probe. Reflection was used only to obtain the compiled fixture program.

- Budget 1: Enter on frame 5, Tick on frames 6/8/10, Exit(Success) on frame 11. Logical updates
  1/2/3 used timestamps 39999/98574/104503 microseconds. Enter and Tick in update 1 shared 39999;
  terminal Tick and Exit in update 3 shared 104503. By frame 219 the root still held Success,
  TotalUpdates remained 3 and LastFailure was None.
- Attached the real NativeExecutionDebuggerSession and a temporary TraceTimelineWindow: 31 events,
  readable timeline, zero dropped events and no trace fault in the terminal run. Traces contain
  the yield/resume pairs and three logical update boundaries.
- A second Running host was exercised in a temporary additive scene with a camera and directional
  light. A debugger snapshot was readable while its budget was zero. This deliberately longer
  run wrapped the bounded trace ring (14243 dropped records); the snapshot reported those drops
  and remained non-faulted. It is not presented as a complete event history.
- Disabling held callback count and update count at 2495 across actual frames. Re-enabling
  resumed execution. Unloading the additive scene on frame 25466 produced exactly two final
  callbacks: Abort(TreeStopped), Exit(Aborted). The host was destroyed, owner state was Disposed,
  and LastFailure remained None.
- Removed the temporary windows, hosts and Editor callbacks and stopped Play mode. Final state:
  one original scene, zero ProductionTreeHost objects, no compiling scripts, no console errors.

## Allocation measurement and limits

[allocation-probe.cs.txt](allocation-probe.cs.txt) is the C# method body used with Unity MCP
execute_code. It binds Update once outside measurement, warms 128 calls, then measures 1000 calls
using GC.GetAllocatedBytesForCurrentThread. A Running fixture uses a constant clock and a trace
capacity large enough to avoid drops/faults: **0 bytes in Immediate; 0 bytes with budget 1**.
This measures the host path in EditMode, independently from the real-frame Play verification.
It excludes bootstrap, snapshots, caller gameplay allocations and Editor/player-wide GC.

No Standalone, Android or Web build was run. No population scheduler, automatic restart,
hot-reload wiring or general generated-node dispatch adapter is claimed. P7-029, P7-031 and
P7-032 remain separate scopes.
