# P12-005 — Two compiler warnings every consumer sees

Status: `In progress` — filed 2026-11-25 from the SweetKingdom feedback on 0.2.0 (item B). The edits are made and
not committed: the Modules Editor is closed, so nothing is compiled yet.

## Objective

`MCP/McpRuntimeTools.cs` and `Agents/Nodes/BtAttributes.cs` compile without CS0618 and CS0114 on
6000.6.9f1 and on 7000.0.0a8, with no change of behaviour.

## What was established

- **Both warnings show on 6000.6.9f1 too**, not only on Unity 7. `Logs/Editor.log` of Modules (a 6000.6.9f1
  session) has `McpRuntimeTools.cs(219,16)`, `(219,102)`, `(220,25)` and `(220,108)` CS0618, and
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

## Changes (uncommitted until the Editor is back)

- `MCP/McpRuntimeTools.cs`: `FindHosts` uses `FindObjectsByType<T>(FindObjectsInactive.Include)` under
  `UNITY_6000_6_OR_NEWER`, else the old call.
- `Agents/Nodes/BtAttributes.cs`: `public new string TypeId { get; }`, with a doc sentence.

## Acceptance

- After a recompile in Modules (6000.6.9f1), the Console and `Logs/Editor.log` have no CS0618 for
  `McpRuntimeTools.cs` and no CS0114 for `BtAttributes.cs`.
- MCP `runtime_hosts` and the runtime trace tools that resolve a host (`FindHosts`, `McpRuntimeTools.cs:176`,
  `:181`) find the same hosts: `McpRuntimeNativeAgentTests` and `AgentConsumerToolsTests` pass. The managed
  agent tests that read `BtNodeAttribute` (`ManagedAgentLayerTests`, `BehaviorTreeAgentComponentTests`) pass.
- Unity 7: not compiled here (the owner's Editor). The record says the claim rests on the metadata read and
  the define list, and the consumer is asked to confirm.

## Forbidden changes

`CodeGen~`, `ExpectedPublicAbiV2.txt`, the compiled program format, `Documentation~/specifications`,
`package.json`, `Tests/Fixtures`, existing public signatures (the property keeps its name, type and access).

## Evidence

`Planning~/Evidence/P12-005/README.md` (to be written after the compile).
