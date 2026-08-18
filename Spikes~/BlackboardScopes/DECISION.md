# P2-003 versioned format decision

Status: Accepted after independent round-4 review on 2026-08-14.

## Decision

- Agent and Shared authoring starts at self-contained `aibt.tree` format
  version 2, additive-complete over the v1 Tree/node/observer/tag/metadata
  shape. Production `tree.schema.json` and serializers are not changed by this
  contract spike.
- The complete Agent/Shared scope schema is repeated by every compatible tree.
  Exact identity, version, schema hash, and layout hash matching is required;
  subset and superset binding are not v1 capabilities.
- Agent execution sharing one context is serialized by `TreeInstanceId`.
- Shared writes are bounded contributions. Reduction uses the explicit stable
  key `(TreeInstanceId, ContributionSequence)` and is atomic for the whole
  Shared context.
- Built-in reducers are closed. Custom reducers are deferred until a versioned
  contract can reuse the accepted P2-001 unmanaged ABI.
- Compiled format v2 hashes the complete v1-derived header, nodes, children,
  blackboard records, blobs, and debug identities plus optional Agent/Shared
  descriptors, raw layouts, and scope-aware access records. Agent-only and
  Shared-only images remain valid closed variants.
- Compiled-v2 preserves registry capability bits 0-6 exactly. Header bit 7
  declares Agent-scope records and bit 8 declares Shared-scope records; these
  bits are not node-registry capabilities. Scope-descriptor and raw-layout
  counts are unsigned 32-bit little-endian values.
- Format version 2 nodes may carry an optional canonical `bindings` object,
  immediately after `parameters`, mapping generated blackboard binding IDs to
  existing blackboard key IDs. It is an exact generated-handle parameter map,
  not a reinterpretation of Phase-1 manifest reads/writes. Non-blackboard
  handles are excluded and ABI-v1 generated Shared handles remain read-only.

## Rejected alternatives

- Adding fields to tree format version 1 was rejected as an unversioned
  persisted-format change.
- External scope-schema documents were rejected because they introduce a new
  resolver and make a tree non-self-contained without a demonstrated need.
- Subset/superset Agent binding was rejected because it introduces layout
  remapping and migration semantics not required by P2-003.
- Worker append order and job completion order were rejected because they
  violate deterministic mode.
- Treating the previous Shared value as a hidden reducer operand was rejected
  because it makes `First`/`Last` ill-defined and silently turns reductions
  such as `Any` and `Min` into sticky accumulators.

## Implementation boundary

The spike is executable evidence only. Production models, serializers,
compiler records, native storage, schedulers, and reducers remain owned by
later work items.
