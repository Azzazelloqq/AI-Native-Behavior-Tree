# P12-004 — Burst path tests independent of a warm Burst cache

Status: `In progress` — filed 2026-11-25 from the SweetKingdom feedback on 0.2.0 (item A). The test edit is made
and not committed: the Modules Editor is closed, so nothing is compiled or run yet.

## Objective

`BurstExecutionPathTests` must give the same verdict with a cold and with a warm Burst cache, must still fail
when the agent-frame job really runs managed, and must leave the Burst options of the Editor session as it
found them.

## What the consumer saw

Unity 7000.0.0a8, Burst 1.8.29, a fresh copy of `Library`. On the first run three tests failed and on a rerun
they passed:

- `EveryPath_ReportsFromInsideTheNodeBody_WhetherItRunsBurstCompiled` (`agent-frame-job x1: managed, 6000
  frames, Burst frames 0, expected Burst`);
- `RandomFloat3InRadius_3D_IsBitIdentical_BurstCompiledAndManaged`;
- `RandomFloat3InRadius_Flat_IsBitIdentical_BurstCompiledAndManaged`.

## Why a cold cache fails them (from the code)

- **The job compiles asynchronously.** The generated `AibtAgentFrameJob` is `[BurstCompile]` without
  `CompileSynchronously` (`CodeGen~/AIBT.CodeGen/BurstNodeGenerator.cs:1474`). Burst 1.8.29 adds the
  synchronous flag only when the attribute asks for it or `RequiresSynchronousCompilation` is set
  (`BurstCompilerOptions.cs:585`, `:428`). Without either, the Editor runs the job managed until the
  compilation ends.
- **The other paths are managed by design.** The generated catalog facade carries no `[BurstCompile]`, so
  Burst does not patch its direct calls (`Planning~/Evidence/P11-014/README.md`, U3 limits). This is why
  the test pins them as managed. Synchronous compilation therefore cannot flip them.
- **The wait is cut by a frame count.** `Probe` stopped at `MaximumFrames` = 6000 whatever the seconds
  (`Tests/Integration/NativeRuntime/BurstExecutionPathTests.cs:205` before the change). The 90 s intended for
  the compiling path (`CompileWaitSeconds`) applied only if 6000 frames took longer than 90 s. In this Editor a
  frame of that test took about 50 ms: the first Burst frame came at frame 618, after 31.5 s (P11-014). The
  consumer's report shows 6000 frames passed before the compilation ended.
- **Why a cold cache is slower.** Each parity test waits for Burst in the same way, and all three failed.
  So the compilation on a fresh `Library` took longer than every one of those waits. Burst 1.8.29 keeps its
  JIT cache in `Library/BurstCache/JIT` (`BurstCompilerOptions.cs:50`). A rerun found it filled.

## Chosen fix (test only, no runtime change)

1. **Synchronous compilation for this class only.** `[OneTimeSetUp]` sets
   `BurstCompiler.Options.EnableBurstCompileSynchronously = true` when Burst is enabled and the option is off.
   `[OneTimeTearDown]` sets it back. A job whose compilation is requested while the option is on is compiled
   before it first runs, so the first agent-frame frame is Burst with a cold cache as with a warm one. The
   option triggers no recompilation of jobs already compiled (Burst 1.8.29: its setter calls only
   `OnOptionsChanged`, not `MaybeTriggerRecompilation`, `BurstCompilerOptions.cs:316-325`).
2. **A wait counted in seconds, not frames.** Sometimes the job's compilation was requested asynchronously
   before the class ran: by an earlier run in the same domain, or at a point this card did not establish. The
   probe then waits at least `MinimumFrames` and `CompileWaitSeconds` = 300 s; the 6000-frame cap is gone.
3. **Still fails when the path runs managed.** A path that never reports Burst within the bound is a
   mismatch, as before. No `Inconclusive` was added. The failure message now says whether synchronous
   compilation was on.
4. **Timeouts.** The three tests get `[Timeout(900000)]`. One compile wait of 300 s plus six paths of 8 s
   would exceed the runner's default of 180 s.

Why not the other routes:

- `CompileSynchronously = true` on the generated job is in `CodeGen~` (protected). It would also make every
  consumer's Editor block on the first schedule of each catalog.
- A longer frame bound alone keeps the verdict dependent on the machine's frame rate and the cache.
- `BurstCompiler.IsCurrentCompilationDone` is internal and reports the last compilation id, not this job.

Side effect to know: Burst writes its options to EditorPrefs on every change
(`BurstEditorOptions.GlobalOnOptionsChanged`, Burst 1.8.29). If the Editor dies between the set-up and the
teardown, "Synchronous Compilation" stays checked in the Jobs > Burst menu until someone clears it. Burst 2.0
(built into 6000.6) exposes the same public property (read from `UnityEngine.BurstModule.dll`). Its source
is not readable here, so whether it persists the option the same way is not established.

## Acceptance

- `BurstExecutionPathTests`: 3/3 in Modules with a warm cache, and 3/3 on the first run after the JIT cache
  is moved aside (cold). The log of each run shows the first Burst frame of the agent-frame path and whether
  synchronous compilation was on.
- After the class: `BurstCompiler.Options.EnableBurstCompileSynchronously` and its EditorPrefs value are what
  they were before (read before and after with `unity command eval_file`).
- The class still fails when the agent-frame path runs managed. The verdict rule is unchanged
  (`outcome.Burst != path.Burst` is a mismatch, and no `Inconclusive` is added). The six managed paths are
  still reported managed with synchronous compilation on, which shows the probe still sees managed bodies.
  The P11-014 table of expected paths is unchanged. With Burst compilation off the tests are reported as not
  run (`Assume`), as before.
- Nothing outside `Tests/Integration/NativeRuntime/BurstExecutionPathTests.cs`, `CHANGELOG.md` and the
  evidence changes.

## How to reproduce the cold case in Modules (when the Editor may be used)

1. With the Modules Editor closed, rename `C:\UnityProjects\Modules\Library\BurstCache\JIT` to
   `JIT.warm-<date>`. Move it aside; do not delete it, it is 2.1 GB with the AOT folders.
2. Start the Editor. The first test run of the session is
   `Invoke-UnityTests.ps1 -Filter BurstExecutionPathTests`. First run it on the commit before this change,
   to see whether the old test fails here as it did for the consumer. Record the first Burst frame and the
   seconds.
3. Close the Editor and move the new `JIT` folder aside again (or delete only that new, cold-built
   folder). Start the Editor and run the changed class as its first run.
4. Close the Editor and put `JIT.warm-<date>` back.

A cold cache in Modules is not the consumer's: Burst 2.0 is built into 6000.6.9f1 here, while the consumer has
Burst 1.8.29 on 7000.0.0a8. Step 2 may pass in Modules because a frame takes about 50 ms, so 6000 frames is
about 300 s. If it does, the consumer's failure is not reproduced, and the record says so.

## Forbidden changes

`CodeGen~`, `ExpectedPublicAbiV2.txt`, the compiled program format, `Documentation~/specifications`,
`package.json`, `Tests/Fixtures`, runtime code, existing public signatures.

## Evidence

`Planning~/Evidence/P12-004/README.md` (to be written after the runs).
