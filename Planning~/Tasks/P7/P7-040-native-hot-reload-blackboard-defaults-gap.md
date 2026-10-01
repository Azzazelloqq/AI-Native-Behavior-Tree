# P7-040 — Preserve Tree values and initialize defaults in native hot reload

Status: `Done`

Owner approved the narrowed scope and implementation on 2026-09-19.
Verified with the same single baseline failure in full regression; see
[evidence and limitations](../../Evidence/P7-040/README.md).

## Objective

The existing V1 hot-reload mechanism previously zeroed Tree-blackboard values instead of preserving
live values or applying compiled defaults. Initialize fresh Tree storage and selectively migrate
compatible values, retaining the accepted active-instance and P7-029 lifecycle behavior.

## Dependencies

- `P7-012` — native hot reload implementation (`done`).
- `P2-007` — native Tree/Agent blackboard storage (`done`).

## Accepted behavior

- Fresh hot-reload instances initialize Tree slots from compiled defaults after range validation.
- Migration carries compatible built-in live values by stable key. Both slots must be Tree-scoped;
  type/version, enum contract, size and alignment must match the known built-in layout.
- Slot indices and offsets may change. A changed default does not replace an existing compatible
  live value, including one never written since creation.
- New/incompatible keys retain new defaults; removed keys disappear. Registered types whose schema
  cannot be proven compatible from V1 metadata retain declared defaults.
- Tree keys belong to the instance: subtree exclusions preserve compatible values. Full restart
  uses new defaults. Rejected migration preserves the old instance's values and execution.

## Scope and boundaries

Implementation is limited to `NativeHotReloadInstance.cs` and
`NativeHotReloadStateMigration.cs`, focused tests under
`Tests/Runtime/NativeExecution/HotReload/`, this card/evidence, the P7-011 decision addendum,
work-item status and changelog. General arena factories, public APIs, compiled formats and node
classification remain unchanged.

The original P7-039 test proposal is superseded by owner-approved narrowing: external writes target
`GeneratedTreeDispatchAdapterV2` storage, not this arena. Production-host reload integration,
V2 versions/revision, observer, parallel and RNG migration are outside this fix. No other card is
started.

## Required reading

- Both hot-reload implementation files in full.
- `Runtime/State/Native/NativeInstanceArenaOwner.cs`: V1/V2 construction and defaults.
- ADR-P7-011 and ADR-P5-001.
- `Planning~/Evidence/P7-029/README.md`: ownership, callback and adjacent-state limitations.

## Acceptance and verification

- Drive a native running leaf callback to write once, migrate, and read the retained value on its
  next tick. This is a V1 lifecycle/arena test, not a generated production-dispatch claim.
- Cover new/removed keys, changed defaults, changed offsets/order, complete multi-byte values,
  incompatible type/version/enum/layout/scope, conservative registered fallback, subtree exclusions,
  full restart and rejection leaving the old instance usable.
- Run focused native hot-reload tests, full EditMode regression, static verification and diff check.
  Identify actual baseline failures separately.
- When unrelated host compiler errors block the open Editor, use an isolated package project and
  disclose the environment. Do not claim Player or performance verification.

## Outcome

16 new tests passed; focused native hot reload: 38/38.
Isolated full regression: 1453/1454; original `3f04417` baseline: 1437/1438, with the exact same
generated-artifact hash failure. Static verification (7 schemas, 146 work items) and diff check pass.
The full-suite Persistent allocation warning also reproduces on baseline; focused tests have no
such warning. Details and reproduction commands are in the evidence.
