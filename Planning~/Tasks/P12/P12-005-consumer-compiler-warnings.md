# P12-005 — Two compiler warnings every consumer sees

Status: `In progress` — filed 2026-11-25 from the SweetKingdom feedback on 0.2.0 (item B). The edits are made and
not committed: the Modules Editor is closed, so nothing is compiled yet. Modules moved to 7000.0.0a8 the same
day; the compile and tests happen there.

## Objective

`MCP/McpRuntimeTools.cs` and `Agents/Nodes/BtAttributes.cs` compile without CS0618 and CS0114, and the
package's runtime assemblies compile without other warnings, with no change of behaviour.

Since 2026-11-25 Modules is on 7000.0.0a8: compiling and testing happen on Unity 7 only. 6000.x can no longer
be compiled in this project. For 6000.x, "compiles without these warnings" is argued only from the installed
6000.6.9f1 assemblies (metadata read below), not from a compile.

## What was established

- **Both warnings showed on 6000.6.9f1 too**, not only on Unity 7. `Logs/Editor.log` of Modules (a 6000.6.9f1
  session, read before the move to Unity 7; that log is now overwritten) had `McpRuntimeTools.cs(219,16)`, `(219,102)`, `(220,25)` and `(220,108)` CS0618, and
  `BtAttributes.cs(19,23)` CS0114.
- **CS0618.** Read through metadata (System.Reflection.Metadata, nothing executed) from
  `UnityEngine.CoreModule.dll` of both Editors under `C:\Program Files\Unity\Hub\Editor\`:
  - `Object.FindObjectsByType<T>(FindObjectsInactive, FindObjectsSortMode)`, `<T>(FindObjectsSortMode)` and
    their `Type` forms carry `[Obsolete(..., error=false)]` in 6000.6.9f1 and in 7000.0.0a8, with the same
    message.
  - `FindObjectsByType<T>()` and `FindObjectsByType<T>(FindObjectsInactive)` exist in both and are not
    obsolete.
  - The package declares `"unity": "6000.0"`. No 6000.0 Editor is installed, so whether the overload
    without a sort mode exists there is not established. The new call is therefore behind
    `#if UNITY_6000_6_OR_NEWER`, and older Editors keep the old call. Both Editors know that define: the string
    is in `Unity.dll` of 6000.6.9f1 and of 7000.0.0a8, whose list runs to `UNITY_7000_0_OR_NEWER`.
  - Behaviour: the old call asked for `FindObjectsSortMode.None` (no order), and the new overload sorts
    nothing. `FindHosts` then removes duplicates. No caller depends on the order.
- **CS0114.** `System.Attribute.TypeId` is `public virtual object`, and `BtNodeAttribute.TypeId` (a `string`)
  hides it. The warning comes from the C# compiler for any target that has that virtual. A netstandard2.1
  build outside Unity shows it too. The fix is the `new` modifier: `override` cannot change the type.
  - **`new` changes nothing in the assembly.** Two deterministic builds of the attribute, with and without
    `new`, are byte-identical (same MD5). Reflection (`GetCustomAttribute<BtNodeAttribute>().TypeId` in
    `Agents/Runtime/ManagedNodeType.cs:94`), the link.xml processor, the generator and the analyzer see the
    same metadata.
  - `BtNodeAttribute` is not in `ExpectedPublicAbiV2.txt`. The generator (`CodeGen~`,
    `Analyzers/AIBT.CodeGen.dll`) does not mention `BtNode` at all.

## The other warnings of the first Unity 7 compile (2026-11-25)

`Logs/Editor.log` of Modules, the 7000.0.0a8 compile of 20:56 (build success, no errors), was made before the
edits above, so it shows the package as committed. Besides the consumer's two it has, for `Assets\AIBT`:

- **CS0652, `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs(182,44)`.** In P12-005:
  `AIBT.Runtime` is compiled in every consumer project. What the comparison is: `TryScheduleExecuteRound`
  refused `activeLaneCount <= 0 || activeLaneCount > uint.MaxValue`, with `activeLaneCount` an `int`. C#
  compares an `int` with a `uint` constant as `long`, and no `int` exceeds 4 294 967 295, so the second half was
  false for every input (the compiler says so). Removing it changes nothing for any value; the method's
  refusals are exactly those of `activeLaneCount <= 0`. The line came with P7-033 (`a3e69b2`). The C# compiler
  reports CS0652 on any Editor; that 6000.6.9f1 reported it too is not checked (its log is overwritten).
- **CS0618, `Tests/Editor/Performance/LargeGraphPerformanceTests.cs(73,21)` and `(74,21)`**
  (`ITransform.position` and `.scale` obsolete on Unity 7). Not in P12-005: `AIBT.Editor.Tests` is a test
  assembly (`TestAssemblies`), which a consumer compiles only if it lists the package in `testables`. The
  replacement (`style.translate`, `style.scale`) needs a version check against 6000.0 that cannot be compiled
  here now. A follow-up for the package's own Console.
- **CS0728 × 22, `Tests/Integration/Simulation/TreeSimulationIoTests.cs`** (lines 377, 378, 487, 619, 620,
  867, 894) **and `TreeSimulationActionTests.cs`** (826, 1180, 1206, 1207): writing through the indexer of a
  `NativeArray` declared in a `using`. Harmless (the struct copy shares the buffer that `using` disposes), and
  not in P12-005: `AIBT.Integration.Tests` is a test assembly too. The fix is the pattern of
  `TreeSimulationIndexedInputTests` (`using (var owner = …) { var rows = owner; … }`). A follow-up.

No other warning of the log is in `Assets\AIBT`.

## Changes (uncommitted until the Editor is back)

- `MCP/McpRuntimeTools.cs`: `FindHosts` uses `FindObjectsByType<T>(FindObjectsInactive.Include)` under
  `UNITY_6000_6_OR_NEWER`, else the old call.
- `Agents/Nodes/BtAttributes.cs`: `public new string TypeId { get; }`, with a doc sentence.
- `Runtime/Scheduling/Native/NativePipelinedPhaseControllerV1.cs`: the always-false `activeLaneCount >
  uint.MaxValue` removed from the refusal in `TryScheduleExecuteRound`, with a comment.

## Acceptance

- After a recompile in Modules (7000.0.0a8), the Console and `Logs/Editor.log` have no CS0618 for
  `McpRuntimeTools.cs`, no CS0114 for `BtAttributes.cs` and no CS0652 for `NativePipelinedPhaseControllerV1.cs`.
- The tests that reach `TryScheduleExecuteRound` pass: `NativePipelinedPhaseControllerTests`,
  `NativeBatchedLifecycleOwnerTests`, `NativeExecutionEquivalenceTests`, `PipelinedJobsCadenceTests`.
- MCP `runtime_hosts` and the runtime trace tools that resolve a host (`FindHosts`, `McpRuntimeTools.cs:176`,
  `:181`) find the same hosts: `McpRuntimeNativeAgentTests` and `AgentConsumerToolsTests` pass. The managed
  agent tests that read `BtNodeAttribute` (`ManagedAgentLayerTests`, `BehaviorTreeAgentComponentTests`) pass.
- 6000.x: not compiled (Modules is on Unity 7). The record says the 6000.x claim rests on the metadata read of
  6000.6.9f1 and the define list, and that the package still declares `"unity": "6000.0"`, where no Editor is
  installed.

## Forbidden changes

`CodeGen~`, `ExpectedPublicAbiV2.txt`, the compiled program format, `Documentation~/specifications`,
`package.json`, `Tests/Fixtures`, existing public signatures (the property keeps its name, type and access),
execution semantics (the CS0652 change removes a condition that is false for every input).

## Evidence

`Planning~/Evidence/P12-005/README.md` (to be written after the compile).
