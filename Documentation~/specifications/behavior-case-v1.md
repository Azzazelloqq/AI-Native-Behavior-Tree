# Behavior case v1

Behavior cases are strict, backend-neutral observable tests. They never invoke arbitrary expressions or implementation internals.

## Inputs

A case identifies a semantic tree path, root seed, tree instance ID, typed initial Tree blackboard values, and ordered steps. Each `update` step supplies update ID, snapshot revision, time in microseconds, optional deterministic step budget, ordered external events, and completions. `resume` continues a suspended update without changing input. `restart` invokes the explicit terminal-root restart API. `abort` supplies its own strictly-new update ID, snapshot revision, time in microseconds, optional deterministic step budget, and optional completions before requesting host abort; adapters never synthesize a hidden abort context.

Completion and command payloads use registered typed-value contracts. Unknown type IDs are case diagnostics.

Event and completion sources share one source-ID sequence namespace. Within an update, event records precede completion records for high-water validation; abort steps contain only completions. Source sequences strictly increase across all accepted case inputs for one source, while gaps are allowed.

## Assertions

Each step may assert:

- executor progress and optional public root result;
- typed blackboard values and change versions;
- ordered commands or a declared subset match;
- ordered lifecycle/abort/observer trace records using stable event fields;
- diagnostics by code, severity, and location;
- executed step count.

`commands`, `trace`, `diagnostics`, and `executedSteps` are the delta produced by exactly one executor `Execute`/host API call; they are never cumulative across case steps. The blackboard result is the current observable snapshot. Every observed trace record carries complete common metadata: trace format version, tree semantic hash, tree instance ID, sequence, update ID, and snapshot revision. Case trace expectations are a finite closed set of typed predicate fields; any omitted common or event-specific field is a wildcard, while the adapter's observed record remains complete and is independently contract-validated by the runner.

There is no string expression language. Invariants are typed assertion records. Float assertions use either exact equality or an explicit absolute/relative tolerance; omission means exact equality.

Only `Success` and `Failure` are public root statuses. `Running` and `Inactive` are represented by progress and by omitting `rootStatus`.

## Determinism

Case arrays are ordered. Maps are canonicalized by canonical JSON v1. The same case can run against any executor adapter implementing the case-runner contract. Adapters may expose additional metrics, but cases cannot depend on private frame or instruction layout.

Invalid case syntax or semantics produce `AIBT9xxx` structured diagnostics and do not partially execute.
