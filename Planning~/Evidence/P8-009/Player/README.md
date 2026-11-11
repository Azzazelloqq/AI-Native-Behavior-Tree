# P8-009 consumer Player diagnostic

Unity 6000.7.0b1, Windows x64 IL2CPP, Burst enabled, **Development** Player, Null graphics device.
CPU: Intel(R) Core(TM) Ultra 9 275HX (24 logical processors). One workstation run.

The exact First Agent sample was imported into an isolated project. Its runtime asset was prepared in the Editor and serialized into the scene; the gameplay assembly has no Authoring reference. The cube finished at x=4.75002384185791. All six measurement cells and explicit shutdown passed.

Each cell uses the same single generated Action, one external Float32 read and one Float32 effect, a shared prepared program, and the normal production world/scheduler. Measured agents keep distance=100, then receive distance=0 for the input-to-terminal-result latency probe. Each cell warms 60 frames and records 180 frames. Jobs use an explicit fixed batch size of 32 and the existing capability model; no defaults were changed.

| Agents | Policy | Scheduler median / p95 (ms) | Input to Success (frames) | Logical updates / effects including warmup |
|---:|---|---:|---:|---:|
| 16 | Immediate | 0.0692 / 0.0832 | 1 | 3840 / 3840 |
| 16 | BatchedJobsSameFrame | 0.1329 / 0.4158 | 1 | 3840 / 3840 |
| 16 | PipelinedJobs | 0.0272 / 0.1100 | 5 | 1280 / 1264 |
| 128 | Immediate | 0.5454 / 0.6708 | 1 | 30720 / 30720 |
| 128 | BatchedJobsSameFrame | 1.1615 / 1.6230 | 1 | 30720 / 30720 |
| 128 | PipelinedJobs | 0.2880 / 0.8737 | 5 | 10240 / 10112 |

## Attribution and limits

For 128 Immediate agents, median profiler observations were approximately 425 us in generated dispatch, 74 us in trace record methods, 16 us in the lifecycle VM, 7 us in input flushing and 3 us in effect delivery. Dispatch is the largest measured immediate-path span. This identifies where a separate optimization investigation should begin; it does not establish which internal validation/copy operation dominates.

Profiler times are inclusive, sampled at frame boundaries and aggregated across contributing threads. They are not exclusive additive parts of scheduler wall time. `AIBT.Production.GeneratedDispatch` brackets the standalone immediate adapter; grouped Jobs use a separate path, so its zero value there does not mean dispatch is free. All seven recorder handles were valid.

PipelinedJobs performed about one third as many logical updates and delayed the changed-input outcome to five observed frames. Its lower median per-frame time is not equal-work speedup. Same-frame Jobs are slower on this small Action. The findings do not justify changing Auto or forcing Jobs.

This diagnostic Development Player includes trace and profiler overhead, a shallow tree and a bounded frame-count sample. It is not directly comparable to the previous release Swarm Arena workload, does not explain that workload’s full cost, and is not a new support/performance threshold or a zero-GC measurement.

Raw samples: [player.raw.json](player.raw.json); recomputed aggregates: [summary.json](summary.json); build proof: [build.raw.json](build.raw.json).

Reproduce from the package root:

```powershell
pwsh -File Tools~/Verification/P8/Run-ConsumerWorkflow.ps1 -UnityPath "<installed Unity executable>" -OutputPath "<empty result directory>"
```

The helper creates its own temporary project. Build binaries and raw Unity logs remain there, outside the repository.
