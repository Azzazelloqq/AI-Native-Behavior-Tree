# P7-003 real Profiler capture

Builds a real Windows x64 Standalone Player and keeps `P4-001`'s own
`deep-sequence-selector-traversal` scenario (64 agents, `Immediate` policy) running continuously
so the Unity Editor's Profiler window can connect to it live and capture the new
`AIBT.Native.*`/`AIBT.Reference.*` `ProfilerMarker`s this card added.

## Why this differs from `P4-008`'s own Windows Player precedent

`Benchmarks~/Phase4/Platform/Windows/` deliberately builds a **non-development** Player -- the
right choice for wall-clock/allocation measurement, since a development build's own overhead would
contaminate the numbers. A Unity Profiler window genuinely cannot connect to that kind of build:
Unity does not compile any profiler transport into a Release build at all. This card's own
acceptance criterion asks for a real Profiler *capture*, which needs `BuildOptions.Development |
BuildOptions.ConnectWithProfiler` instead -- an explicit, disclosed deviation from the
"non-Development Player" phrase in the card's own text, confirmed with the project owner before
building (see `Planning~/Evidence/P7-003/README.md`).

## Build and run

```powershell
.\Run-ProfilerCapture.ps1
```

Builds a fresh isolated project (same `Runtime/`/`Authoring/` copy technique as `P4-008`'s own
script) and launches the Player, which stays running for 150 seconds so there is time to connect a
Profiler session from the open Editor (`UnityEditorInternal.ProfilerDriver.connectedProfiler` /
`.enabled` / `.SaveProfile`) before it exits on its own. The isolated project and Player are left in
place for inspection; cleanup is manual.

## Recorded evidence

See `Planning~/Evidence/P7-003/README.md` for the captured `.data` file, the build evidence JSON,
and the live marker-hierarchy transcript pulled directly from a connected `HierarchyFrameDataView`.
