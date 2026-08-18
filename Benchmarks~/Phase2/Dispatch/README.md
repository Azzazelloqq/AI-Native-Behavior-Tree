# P2-012 native dispatch microbenchmark

This benchmark isolates generated closed ABI v2 dispatch at catalog
cardinalities of 1, 16, and 128 types. It is evidence, not a performance gate:
the runner records every timing and allocation observation and defines no
machine-derived pass/fail threshold.

## Measured work

The benchmark contains three separately Burst-compiled entry points. Their
source-visible switches have exactly 1, 16, and 128 cases; every case directly
calls the same micro-callback body. The matching function pointer is selected
before the timer starts, so none of the scenarios shares a wider switch or pays
an out-of-band selector inside the measured loop.

Each sample creates 16,384 deterministic Enter requests and cycles evenly
through every case in its selected generated switch. The timed function
performs, for every request:

1. request and case resolution;
2. frame acquisition;
3. one `UInt32` configuration read;
4. one `Int32` memory read, write, and commit;
5. Enter-context creation and completion.

Input construction, Runtime-owner creation/copies, disposal, result checks,
GC, and JSON serialization are outside the timed and managed-allocation window.
The three cardinalities are warmed first and measured round-robin to reduce
order drift. Defaults are five warmup samples and fifteen measured samples per
cardinality.

Every raw sample records its `generatedSwitchWidth` and `compiledEntryPoint`;
the enclosing case record repeats both fields for straightforward auditing.

Each measured entry is compiled explicitly with
`BurstCompiler.CompileFunctionPointer`; the run aborts if Burst is disabled and
does not allow a managed fallback. A `BurstDiscard` sentinel also makes every
managed-fallback invocation fail instead of producing benchmark data.

The runner prefers `GC.GetAllocatedBytesForCurrentThread` for warm
current-thread allocation deltas. Unity Mono versions where that counter is
inert use `Profiler.GetMonoUsedSizeLong` for process-wide managed used-size
deltas. The selected API and its scope are written to the result, and a positive
allocation canary must prove that the selected counter is sensitive before any
measurement is accepted.

## Run

From this directory in PowerShell:

```powershell
.\Run-DispatchBenchmark.ps1
```

If local execution policy blocks scripts, invoke it explicitly without changing
the machine policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\Run-DispatchBenchmark.ps1
```

An explicit reproducible invocation is:

```powershell
.\Run-DispatchBenchmark.ps1 `
  -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe' `
  -WarmupSamples 5 `
  -MeasuredSamples 15 `
  -DispatchesPerSample 16384 `
  -OutputPath '.\Results\my-run.json'
```

`DispatchesPerSample` must be divisible by 128. The script creates a fresh
isolated Unity project under the system temporary directory, copies only
`Runtime/` plus the benchmark assembly, and pins Burst 1.8.30, Collections
6.5.0, and Newtonsoft JSON 3.2.2. It leaves the isolated project in place for
inspection and prints its path; removal is intentionally manual.

The benchmark assembly deliberately uses the existing friend assembly name
`AIBT.NativeBurstDispatch.Tests` so it can construct the internal Runtime owner
without widening production visibility. Sources live under `Benchmarks~` and
are only imported into the isolated project by the runner; the package itself
does not compile a duplicate test assembly.

## Evidence and interpretation

The JSON contains environment/package metadata, command line, timer frequency,
allocation-canary result, raw elapsed ticks, derived timings, raw allocation
deltas, and descriptive min/median/p95/max summaries. The adjacent Unity log is
the compilation/run diagnostic.

This Editor batchmode microcase proves that the measured entry was compiled by
Burst on the recorded machine. It is not a Windows Player baseline and does not
replace separate Player AOT/Burst Inspector evidence. Thermal and power state
are not controlled, and results from this workstation must not be generalized
to other platforms.

## Recorded evidence

The canonical 2026-08-16 isolated run is preserved as
[raw JSON](Results/windows-editor-generated-switch-burst-6000.5.8f1-2026-08-16.json).
The adjacent Unity log from that run is not committed, per repository policy
against committing raw Unity logs.
It used Unity 6000.5.8f1, Burst 1.8.30, Collections 6.5.0, five warmups,
fifteen raw samples per switch width, and 16,384 dispatches per sample.

| Generated switch width | Measured dispatches | Median ns/dispatch | Managed-counter delta |
| ---: | ---: | ---: | ---: |
| 1 | 245,760 | 2,117.615 | 0 B in all samples |
| 16 | 245,760 | 2,102.417 | 0 B in all samples |
| 128 | 245,760 | 2,113.123 | 0 B in all samples |

These are descriptive observations only. The JSON is authoritative for all 45
raw samples, environment metadata, min/p95/max summaries, function names, and
switch widths; the table does not define a threshold.
