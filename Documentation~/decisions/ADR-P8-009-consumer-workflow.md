# ADR P8-009: Consumer workflow and Float32 parameters

- Status: Accepted by owner implementation instruction, 2026-11-02
- Decision ID: AIBT-042

## Decision

The approved consumer workflow prepares immutable compiled data in the Editor. A versioned,
derived runtime asset carries the existing compiled semantic program and native blackboard
binding, including source debug identity. Runtime does not parse semantic JSON or reference
Authoring/Editor. A shared explicitly owned program binds this asset to the consumer's generated
catalog factory. Existing bootstrap APIs remain available; no hidden registry is introduced.

Preparation selects an explicit generated catalog and compiles from that catalog's declared
shards. The runtime asset pins its generated fingerprint; a different/rebuilt catalog fails closed
and requires re-preparation. The archive preserves the production host's existing legacy semantic
projection plus generated scope tables. It does not reinterpret that projection as the distinct
unified-slot `NativeProgramImageOwnerV2` representation. Envelope version 1, checksum, program hash
and existing generated-bootstrap validation guard restoration. No runtime reflection is added.

`ProductionTreeHost.Stop()` exposes the existing destruction/cancellation cleanup explicitly and
idempotently, so callers can release hosts before their shared program. It is permanent for that
host. In-flight/reentrant cleanup uses the existing deferred destruction path; no callback/status
or automatic-restart behavior changes.

Float32 is an additive node-parameter type. Existing parameter enum values, generated ABI v2,
node-registry format 1, authored tree formats and compiled formats remain unchanged. Existing
contracts serialize identically. New contracts use the `float32` parameter spelling and existing
Float32LE field encoding. Finite JSON numbers (including integer tokens) are rounded to IEEE-754
binary32; values overflowing binary32, NaN and infinities are rejected. Negative zero is normalized
to positive zero, following existing canonical floating-point conventions. A changed existing
node's parameter type requires a new node version and explicit migration; no existing node is
silently reinterpreted. Older tools reject the unknown type rather than dropping it.

Preview and native trace attach to the authoring window's shared graph under AIBT-032. Native
highlighting requires matching canonical source semantics and the exact live program hash/debug
map. Editing a graph invalidates the attachment instead of guessing indices. Port connections
append children in explicit semantic order; positions remain presentation data.

Performance checks measure actual work and delayed-result latency. They do not alter Auto,
scheduling presets, regression thresholds or support claims.
