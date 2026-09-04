# P7-032 implementation plan

Owner authorized autonomous continuation and commits on 2026-09-04. Dependencies P2-019 and
P4-003 are Done. Scope remains the completion/ownership paths in the three scheduler files,
their existing test fixtures, and evidence/status documentation.

1. Expose the execution owner's existing scheduled-state predicate internally within the same
   assembly. No new state, data format or public API. Both controllers use it only after a
   rejected completion to distinguish unconsumed work from a completed failing lane.
2. Preserve the scheduled phase/dependency while the owner retains its operation. Preserve
   ExecuteReady for a completed lane failure. Do not change success paths, counters or the
   Pipelined stage-advance requirement.
3. For each controller test missing, zero-length and oversized results/failure arrays, untouched
   rejected output, retry of the same job, semantic step count, next round, published metrics,
   abort and disposal. Separately schedule a machine with a closed update to prove lane-failure
   diagnostics and cleanup through the controller API.
4. Run focused scheduling tests (including existing zero-GC/partition/order tests), full EditMode,
   static verification and diff review. No new benchmark claim: only failed-completion state
   handling changes, with no extra normal per-round work.

The earlier live reproduction already proves the red case. Do not re-run the broken-controller
negative test merely to strand live native allocations; verification uses corrected API cleanup.
