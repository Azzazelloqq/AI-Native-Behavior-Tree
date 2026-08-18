# ADR P2-002: Native ownership and bounded capacity

- Status: Accepted; independently accepted by the P2-002 round-2 review on 2026-08-14
- Date: 2026-08-14
- Decision ID: AIBT-021

## Context

Phase 2 introduces native storage shared with Unity Jobs and Burst. The runtime needs deterministic lifetimes and bounded hot-path output without allowing Unity container safety to become the only release-mode protection. Snapshot immutability, instance atomicity, and trace overflow semantics must remain compatible with the Phase 1 contracts.

## Decision

Adopt `native-runtime-v1.md`:

- one explicit owner for every native allocation and non-owning job views;
- `Allocator.Persistent` for v1 owners, with all allocation outside execution passes;
- immutable program and input frames, one exclusive execution lease per instance, and one pass owner for staging/output storage and the final dependency;
- exact owner/generation/lease tokens and an instance binding to one program-image owner and generation, validated before callbacks or scheduling;
- an immutable complete capacity plan with separate record/payload, alignment, work, and scratch limits using checked unsigned arithmetic before scheduling;
- pass-owned staged instance state and staged output, committed only after successful completion;
- atomic owner initialization with reverse-order rollback of partial native allocations, plus reverse lease rollback when scheduling fails;
- distinct completed, aborted, and faulted terminal cleanup paths;
- no resize, relocation, native/managed allocation, partial publication, or managed fallback in a pass;
- deterministic out-of-band rejection diagnostics for capacity and lifetime failures;
- a pre-reserved trace overflow-summary slot so trace loss does not change semantics;
- explicit release-mode state/generation/bounds validation in addition to Unity safety checks.

## Consequences

- Pass creation has predictable memory cost and may reject work before scheduling.
- Staging instance state costs an additional bounded copy per active pass, but makes capacity/fault rejection atomic without rolling back committed state.
- Long-lived `Persistent` allocations require explicit deterministic disposal; the v1 contract does not rely on `TempJob` age assumptions.
- Emitters must declare or derive finite worst-case bounds before they can use the Burst backend.
- Production containers and executor APIs remain deferred to later Phase 2 cards.

## Rejected alternatives

- Grow `NativeList` or switch to managed storage on exhaustion: violates the bounded zero-allocation path and changes backend behavior.
- Let jobs mutate committed instance storage directly: makes a late defensive capacity failure partially observable.
- Dispose owners through borrowed views: creates ambiguous lifetime responsibility and unsafe aliases.
- Use only Unity collection safety checks: those checks do not define release-mode behavior.
- Fail execution when ordinary trace capacity is exhausted: conflicts with the normative trace contract.

## Verification required for acceptance

The `Spikes~/NativeOwnership` harness must demonstrate create/schedule/complete, distinct abort/fault cleanup, exhaustive controlled capacity failures, stale/foreign/generation rejection, partial-initialization and schedule rollback, safety-check rejection of early disposal, and a launched non-development Burst Player completing the exact ownership path without warnings, fallback, or leaks.
