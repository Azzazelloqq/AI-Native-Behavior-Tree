# ADR P9-005: Short Burst node declarations outside the ABI v2 namespace

- Status: Accepted by owner, 2026-11-15 (option A: "new attributes")
- Decision ID: AIBT-047

## Context

A Burst node had to declare `Enter`, `Tick`, `Abort` and `Exit` even when three of them were empty,
and every attribute took all positional arguments: ten for `[AibtBurstNode]`, a value type id and
version for every field, a separate binding attribute with access, scope, type id and version for
every blackboard key and command. Stage 4 asked for shorter forms with defaults and field type
inference, strictly additively: ABI v2 (`ExpectedPublicAbiV2.txt`, 357 lines) and the output of
existing long declarations must not change.

The ABI v2 contract test (`PublicBurstAbiContractTests`) reflects every public type, constructor,
field and property of `AIBT.Runtime` whose namespace is exactly `AIBT.Burst`. Overloading the
existing attributes adds manifest lines there, so that route needs a new ABI decision. The owner
was shown three options (A: new attributes in another namespace normalized by the generator;
B: overloads plus an ABI change; C: optional callbacks only) and chose A.

## Decision

1. **Optional callbacks.** `Enter`, `Abort` and `Exit` may be omitted; an omitted callback is a
   no-op and its dispatch call is not emitted. A declared callback must still match exactly
   (AIBT5003). `Tick` stays required.
2. **Short attributes in `AIBT.Native`.** `AIBT.Runtime` gains `AibtActionAttribute`,
   `AibtConditionAttribute`, `AibtParamAttribute`, `AibtMemoryAttribute`, `AibtKeyAttribute` and
   `AibtCommandAttribute` in namespace `AIBT.Native` (file
   `Runtime/Nodes/Contracts/NativeNodeShortForms.cs`). `AIBT.Native` is deliberately not
   `AIBT.Burst` and not a child of it: these types are not part of the ABI v2 surface. They have
   no runtime role; nothing reads them by reflection.
3. **Normalization in the generator.** `AIBT.CodeGen` (`ShortFormNormalization`) expands every
   short attribute into exactly the long `AIBT.Burst` attribute(s) it stands for before any
   validation, diagnostics or emission; the generator, analyzer and metadata emitter all read
   attributes only through this view. Defaults: version 1, activation memory, deterministic;
   Action = AbortOnly, Low, Success|Failure|Running; Condition = NotApplicable, Trivial,
   Success|Failure; key scope Tree. Configuration and memory types come from `Tick`'s first two
   parameters; value type ids and versions from the field type (the same identity table the
   generator already validates against); blackboard access from the handle type. Named arguments
   override every default. Diagnostics for a short form point at the short attribute.
4. **No other change.** The long attributes, `ExpectedPublicAbiV2.txt`, generated output for long
   declarations, manifests, fingerprints and the runtime are unchanged.

## How the new types are visible (not hidden)

- **ABI v2 contract test:** not covered by design (exact namespace `AIBT.Burst`). The test stays
  green without editing `ExpectedPublicAbiV2.txt`. If a later ABI version widens that test to child
  or sibling namespaces, these six attributes become part of the reviewed surface then.
- **Public API audit** (`Tools~/Verification/P7/Audit/Get-FullPublicApi.ps1 -BaselinePath`): it
  dumps every public type of `AIBT.Runtime`, `AIBT.Authoring`, `AIBT.Editor` and `AIBT.Mcp`, so the
  six types, their constructors and properties appear as added lines (`TYPE AIBT.Native.Aibt…`)
  and pass the additivity check (no removals).
- **Generated API reference:** `Documentation~/generated/api-reference-runtime.md` lists them;
  its completeness and drift tests (`McpDocumentationGeneratorsTests`) cover them.
- **Equivalence tests:** the Roslyn matrix (`Tools~/Verification/P2/CodeGen/Verifier`,
  `VerifyShortForms`) requires byte-identical generated shard and catalog source for a short and
  a long declaration of the same Action and Condition, and reports misuse with the usual
  diagnostics; `ShortFormDeclarationTests` (Unity) requires identical generated metadata, manifest
  registry and hashes for the consumer fixture node declared both ways.

## Consequences

- Two spellings exist for one concept; the long form remains the normative ABI and is what the
  short form means. Documentation shows both side by side.
- Snapshot, async-operation and completion bindings, node documentation and shards have no short
  form yet.
- The checked-in analyzer was rebuilt; its SHA-256 is in `Analyzers/AIBT.CodeGen.sha256`. The
  available .NET SDK (10.0.303, Unity 6000.7.0b1) does not reproduce the previous checked-in
  binary from the previous source either, so the old binary's reproducibility could not be
  re-proved; two independent builds of the new source are identical.
