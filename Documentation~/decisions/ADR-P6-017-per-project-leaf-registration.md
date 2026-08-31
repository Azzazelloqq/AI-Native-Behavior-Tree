# ADR P6-017: Per-project leaf-node registration mechanism

- Status: Accepted 2026-08-31
- Date: 2026-08-31
- Decision ID: AIBT-031

## Context

`P3-009`, `P6-007`, and `P6-008` each independently disclosed the same limitation: every reference-
executor-driven tool (preview, `simulate`, `run-tests`) is limited to the fixed Phase 1 fixture/
built-in leaf set (`ReferencePreviewFixtureEnvironment`), because AIBT ships no production per-project
leaf-behavior registration mechanism. A real project tree using anything beyond built-in composites/
decorators plus the three `aibt.test.*` constant leaves cannot be previewed, simulated, or test-run
through any tool that exists today, even though it compiles and validates fine for real authoring.
This card decides the registration/discovery shape on paper, backed by a disposable spike.

## Spike evidence (`Spikes~/PerProjectLeafRegistration/`, 2026-08-31, this workstation)

A disposable NUnit spike (`SpikePerProjectLeafRegistration`, run live via Unity MCP `run_tests`)
found the underlying registry mechanism already fully general, and the real blocker three layers
deep and deliberately enforced, not merely unbuilt.

1. **`ReferenceLeafRegistry`/`ReferenceLeafBinding` need no engine change at all.** The registry's
   constructor already takes any `IEnumerable<ReferenceLeafBinding>`; `CreatePhase1Fixtures()` is one
   convenience factory among possible others, mirroring `P6-014`'s own finding about
   `ReferenceCompilationPolicy.Phase1` -- not a hardcoded gate. A genuinely new leaf type (not a
   renamed `aibt.test.*` fixture) was registered alongside the built-ins and ticked through a real,
   unmodified `ReferenceExecutionMachine` for a full three-tick lifecycle (`Enter` once, `Tick` three
   times, `Exit` once, terminal `Success`), proving the combined-registry approach works exactly as
   expected. **Passed.**
2. **The real blocker: a deliberately enforced three-layer wall, confirmed by direct failure, not
   inferred from reading code.** `NodeRegistryBuilder.AddUserExtension` -- the only *public*
   registration method a project can call -- never attaches a `NodeHandlerBindingContract` (the
   type carrying "this manifest has real reference-executor behavior"); that type is itself
   `internal`; and `NodeRegistryBuilder`'s own `ValidateBinding` explicitly rejects a
   `UserExtension`-sourced registration if it ever *does* carry one. Registering a genuinely new,
   non-reserved-namespace leaf (`project.counter.doubling`) via `AddUserExtension` and attempting to
   compile a tree using it reproduced the exact same `AIBT3012`
   ("no Phase 1 reference-handler binding") failure `ReferenceCompiler.cs` raises for any manifest
   lacking a binding -- confirmed by direct compile failure, not assumed. **Confirmed.**
3. **`IReferenceLeafHandler`/`ReferenceNodeContext` are also both `internal`.** Even if a manifest
   could somehow carry a binding, an external project assembly (not one of the small, fixed set
   `Runtime/AssemblyInfo.cs` names in its `InternalsVisibleTo` grants) could not implement the
   behavior contract at all, and could not safely consume `ReferenceNodeContext` (an
   `internal ref struct` exposing raw `ReadOnlySpan<byte>`/`Span<byte>` views) even if it could.

Full raw output is in `Planning~/Evidence/P6-017/README.md`.

## Decision

1. **The registration/discovery shape is not yet buildable as a small facade widening -- it requires
   a new, deliberately-designed public authoring surface across three layers, each independently
   confirmed closed today:**
   - A public leaf-behavior contract (something a project assembly can implement), replacing
     `IReferenceLeafHandler`'s role for external callers -- necessarily paired with a public-safe
     node-context type (since `ReferenceNodeContext`'s raw span-based shape cannot cross the
     internal/public boundary as-is).
   - A public equivalent of `NodeHandlerBindingContract`, and a public `NodeRegistryBuilder` method
     (alongside `AddUserExtension`, or replacing its restriction for this specific case) that can
     attach one to a genuinely project-authored manifest.
   - `ValidateBinding`'s own `UserExtension`-rejects-bindings rule would need to become "accepts a
     binding of the new public kind, still rejects the internal kind" -- a real, disclosed semantic
     change to an existing, deliberate validation rule, not a pure addition.
2. **P6-010's IoC discovery pattern (attribute + `UnityEditor.TypeCache`, Editor-only) is the right
   template for *finding* a project's registered leaf types once the public contract above exists --
   but it does not solve today's actual blocker.** The blocker is not discoverability (nothing needs
   scanning to find a type a project already knows about and wants to register); it is that the
   contract a project would implement does not exist yet in public form. Applying P6-010's pattern
   before the public contract exists would have nothing valid to discover.
3. **Scope stays the reference executor only, as the card's own Forbidden-changes clause requires.**
   The native (Burst) backend's own constraints for an analogous per-project mechanism were not
   investigated and are not assumed identical.
4. **Not implemented, deferred as a dedicated future engineering card -- but not "rejected."** Unlike
   `P6-014`'s outcome (a hard engine wall with no clear path forward disclosed), this is a real,
   buildable capability with a concrete three-item design (above); it is deferred only because
   designing and implementing a new public authoring surface plus a validation-rule change is a
   materially larger undertaking than one decision-and-spike card should attempt in a single pass,
   per `DECISION_BOUNDARIES.md`'s own escalation discipline.

## Consequences

- A future implementation card designs the public leaf-behavior contract and public-safe node
  context type (its own escalated design decision, likely warranting its own ADR given the API
  surface involved), adds the corresponding public `NodeHandlerBindingContract` equivalent and
  `NodeRegistryBuilder` support, updates `ValidateBinding`'s rule, and only then applies `P6-010`'s
  discovery pattern on top.
- `P3-009`, `P6-007`, and `P6-008` all remain scoped to the fixed fixture set until that future card
  ships; expect follow-up implementation cards against all three once it does.
- No production file (`Authoring/Execution/`, `Runtime/Execution/Reference/`, `P6-007`/`P6-008`'s own
  files) was touched, per this card's own Forbidden-changes clause.

## Explicitly unverified (stated, not generalized)

- The exact shape of a public-safe node-context type (how to expose configuration/memory/blackboard
  access safely to project code without leaking raw spans or internal types) was not designed here --
  that is precisely the future implementation card's own first task, not resolved by this decision.
- Whether the native (Burst) backend has an analogous or already-solved version of this problem
  (its own generated-node pipeline already supports real per-assembly custom nodes, per `AIBT-020`)
  was not investigated; a future card should check whether that existing pattern offers a template
  before designing the reference-executor's own public contract from scratch.
