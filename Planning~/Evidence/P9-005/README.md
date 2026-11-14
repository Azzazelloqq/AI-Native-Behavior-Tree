# P9-005 — Shorter native node declarations

Status: Review, verified in the `Modules` host project on Unity 6000.7.0b1, 2026-11-15. Local
commit only, no push. Decision: [ADR P9-005](../../../Documentation~/decisions/ADR-P9-005-short-burst-node-declarations.md) (AIBT-047).

## ABI stop and owner decision

Overloads on the existing `AIBT.Burst` attributes would add lines to the ABI v2 manifest that
`PublicBurstAbiContractTests` checks against `ExpectedPublicAbiV2.txt`. Work stopped and three
options were presented (A: new attributes in another namespace normalized by the generator; B:
overloads plus an ABI change; C: optional callbacks only). The owner chose A ("A: новые атрибуты").

## Result

1. **Optional `Enter`/`Abort`/`Exit`.** Validated only when declared; their dispatch calls are
   emitted only when declared. `Tick` stays required; a declared wrong signature is still AIBT5003.
2. **Short attributes** in `Runtime/Nodes/Contracts/NativeNodeShortForms.cs`, namespace
   `AIBT.Native` (not `AIBT.Burst`, not a child of it): `AibtAction`, `AibtCondition` (defaults,
   named overrides, configuration/memory types from `Tick`), `AibtParam`, `AibtMemory` (value type
   from the field), `AibtKey` (config field + blackboard binding, access from the handle, Tree scope
   by default) and `AibtCommand` (config field + command binding).
3. **Normalization.** `CodeGen~/AIBT.CodeGen/ShortFormNormalization.cs` expands short attributes
   into the long ones; `BurstNodeGenerator`, `BurstNodeUsageAnalyzer` and
   `GeneratedMetadataEmitter` read attributes only through it (`AibtAttributes()`, `Attr`/`Arg`
   instead of `AttributeData`/`TypedConstant`). Diagnostics for short forms point at the short
   attribute.
4. **Sample.** `Samples~/FirstAgent/Nodes/MoveNode.cs` uses the short form and only `Tick`:
   configuration declarations 8 → 4 lines, node attribute 4 → 1 line, empty callbacks 3 → 0;
   file 43 → 38 lines (39 → 34 non-blank, including 4 new comment lines). The prepared demo asset
   stayed `Current` after the change: the catalog fingerprint is identical.

## Verification

- **Long form unchanged, byte for byte.** The `HEAD` verifier program was run twice, once against the
  `HEAD` generator and once against the new generator, dumping every generated source: 122 of 122
  outputs (valid shards, catalogs, negative cases, the 1000-declaration import-cost case) are
  byte-identical. A copy of the `HEAD` outputs is in host `Temp/stage4/codegen-golden-head/`.
- **Roslyn matrix** (`Tools~/Verification/P2/CodeGen/Verifier`, run with `DOTNET_ROLL_FORWARD=Major`,
  only .NET 10 is installed): `P2-012 Roslyn matrix passed. Assertions: 1452` (1415 at `HEAD`).
  New `VerifyOptionalLifecycleCallbacks` (Tick-only and full nodes give identical shard source; the
  Tick-only catalog calls no omitted callback; wrong declared `Enter`/`Exit` → AIBT5003) and
  `VerifyShortForms` (an Action with params, read/write keys, command and memory field, and a
  Condition with a version, cost and Agent-scope key: short and long declarations give identical
  shard and catalog source; short-form misuse reports AIBT5002, AIBT5007, AIBT5009; a short node
  without `Tick` is rejected without generator or analyzer crash).
- **Unity:** `ShortFormDeclarationTests` (new fixture assembly `AIBT.Consumer.ShortNodes` with the
  consumer node in short form): shard usable, ABI 2, and `ShardId`, `ShardVersion`,
  `CanonicalDescriptorJson`, `DescriptorHash`, `ManifestRegistryJson`, `NodeRegistryHash` equal to
  the long-form fixture. `PublicBurstAbiContractTests` 4/4 with `ExpectedPublicAbiV2.txt` untouched.
- Full run (host `Temp/stage4/tests-final2.txt`): `AIBT.Runtime.Tests` 707/707, `AIBT.Editor.Tests`
  616/618 (only the two pre-existing host-only MCP failures), `AIBT.Integration.Tests` 69/69,
  `AIBT.BehaviorCases.Tests` 65/65. First Agent ran in Play Mode with the rebuilt analyzer (see
  P9-004 evidence).
- **Public API audit** (`Get-FullPublicApi.ps1 -BaselinePath`, `HEAD` dump as baseline; run from
  scratch copies because the committed script does not copy `Agents/` and `Analyzers/`): no
  removals, 51 added lines, including `TYPE AIBT.Native.AibtActionAttribute`, `…AibtCommandAttribute`,
  `…AibtConditionAttribute`, `…AibtKeyAttribute`, `…AibtMemoryAttribute`, `…AibtParamAttribute`.
  The generated API reference lists them as well.

## Analyzer

`Analyzers/AIBT.CodeGen.dll` rebuilt with the `Build-And-Verify.ps1` recipe (`-t:Rebuild`, fixed
`.p2-codegen-gate` paths) using the Unity 6000.7.0b1 .NET SDK 10.0.303; two independent builds are
byte-identical, SHA-256 `f115b11cdbd9035d703f40b6820d5cf18d5607ec9a17caaa40b0d6c4924f8898`
(`Analyzers/AIBT.CodeGen.sha256`). The ABI fixture hash pin is unchanged.

Disclosure: the same recipe and SDK applied to the unchanged `HEAD` source produce `4a12bd64…`, not
the previously checked-in `82292904…`; the gate's default editor 6000.5.8f1 is not installed, so the
old binary's reproducibility could not be re-proved, and `Build-And-Verify.ps1` was not run end to
end (its verifier step also needs .NET 8, and its batchmode clean-project steps were not run).

## Not done / limits

- No short form for node documentation, shards, snapshot, async-operation and completion bindings.
- Configuration and memory types are inferred only from `Tick`; there is no explicit override.
