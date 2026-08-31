# P6-017 Per-project leaf-node registration mechanism decision evidence

## Result

Done, accepted: a real, buildable capability, deferred to a dedicated future engineering card (not a
rejection). `ADR-P6-017` (`AIBT-031`) finds the registry mechanism itself already fully general, and
the real blocker a deliberately-enforced three-layer wall rather than an unbuilt discovery mechanism.

## Real finding: the wall is deliberate, confirmed by direct failure

`ReferenceLeafRegistry`'s constructor already accepts any `IEnumerable<ReferenceLeafBinding>` --
`CreatePhase1Fixtures()` is one convenience factory among possible others, mirroring `P6-014`'s own
finding about `ReferenceCompilationPolicy.Phase1`. A genuinely new leaf type (not a renamed
`aibt.test.*` fixture) registered alongside the built-ins ticked correctly through a real,
unmodified `ReferenceExecutionMachine` for a full three-tick lifecycle.

The actual blocker sits one layer up, and is enforced on purpose: `NodeRegistryBuilder.AddUserExtension`
(the only public registration method) never attaches a `NodeHandlerBindingContract`; that type is
itself `internal`; and `NodeRegistryBuilder`'s own `ValidateBinding` **explicitly rejects** a
`UserExtension`-sourced registration that carries one. Registering a genuinely new,
non-reserved-namespace leaf (`project.counter.doubling`) via `AddUserExtension` and compiling a tree
using it reproduces the exact same `AIBT3012` ("no Phase 1 reference-handler binding") failure any
unbound manifest gets -- confirmed by direct compile failure, not inferred from reading the
validation code. `IReferenceLeafHandler`/`ReferenceNodeContext` are also both `internal`, so even a
hypothetically-bound manifest's behavior still couldn't be implemented by an external project
assembly today.

## Verification

```text
Disposable spike (SpikePerProjectLeafRegistration, Tests/Editor/PerProjectLeafRegistrationSpike/
  during this session, archived afterward): 3/3 tests passing, live via Unity MCP run_tests --
  GenuinelyNewLeafType_RegisteredAlongsideBuiltIns_TicksCorrectlyThroughTheRealMachine,
  GenuineUserExtensionPath_CanNeverCarryAReferenceHandlerBinding_ConfirmedByDirectFailure,
  RegistryConstructorIsAlreadyFullyGeneral_NoEngineChangeNeededToCombineBindings
Regression (required by this card's own acceptance criteria, unmodified, live via Unity MCP):
  AIBT.Tests.Editor.Preview.ReferencePreviewParityTests -- 2/2 passing
Verify-Static.ps1 -- passed
git diff --check -- clean
```

No production file (`Authoring/Execution/`, `Runtime/Execution/Reference/`, `Authoring/Registry/`)
was touched, per this card's own Forbidden-changes clause. The first spike test needed the internal,
test-only `AddBuiltInForTest` path (with an `aibt.core.`-namespaced type ID, an artifact of that
path's own `NodeManifestSource.BuiltIn` validation rule, not a claim about where real project leaves
would live) to reach a bound registration at all, since no public path exists -- the second test
proves the genuine, unprefixed per-project path is directly, deliberately blocked. The spike lived
temporarily in `Tests/Editor/PerProjectLeafRegistrationSpike/`, then archived to
`Spikes~/PerProjectLeafRegistration/` and deleted from `Tests/`, mirroring this session's own
established precedent.

## Handoff

A future engineering card designs a new public leaf-behavior contract with a public-safe node-context
type (its own separately-escalated decision given the API surface involved), a public
handler-binding equivalent, the corresponding `NodeRegistryBuilder`/`ValidateBinding` change, and only
then applies `P6-010`'s attribute/`TypeCache` discovery pattern on top. `P3-009`, `P6-007`, and
`P6-008` all remain scoped to the fixed fixture set until that ships.
