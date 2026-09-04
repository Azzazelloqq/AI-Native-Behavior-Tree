# P7-032 scheduler recovery verification

Status: **Done**. Owner authorized autonomous implementation and commits on 2026-09-04.

## Behavior

The execution owner exposes its existing outstanding-operation state internally. After a false
completion return, SameFrame and Pipelined retain their scheduled phase/dependency if the owner
still holds the operation. A completed lane failure returns the controller to ExecuteReady and
preserves its diagnostic. Successful-round accounting and Pipelined stage restrictions are unchanged.

## Verification

- Added 14 real-job regressions: absent, zero-length and oversized result/failure buffers for
  each controller, plus an actual scheduled lane error for each controller.
- Rejected buffers remain untouched. Abort/dispose are refused while the operation is outstanding;
  valid retry consumes the same job (one semantic step), the next round executes once, published
  counters include only two accepted rounds, and normal update/abort/disposal succeeds.
- A lane whose update is closed returns NativeLifetimeStateInvalid through both per-lane and
  controller diagnostics and permits normal abort/disposal. Tests never access private owners.
- Focused scheduling suite: **126/126 passed**, zero failed/skipped, job
  2d1a65e0bed7440cb184dae5db26b09f. Includes existing stage-order, partitioning, trace and zero-GC tests.
- Full EditMode with P7-031: **1726/1729 passed, 3 failed, 0 skipped**, job
  b38994b8ba4e472087dbfbde0834563b. Fresh XML and MCP agree. The same two CodeGen PackageInfo
  assertions and LocalSaveSystem autosave test fail as before; see verification-results.json.
- Static verification and git diff --check passed.

No policy/default, batching, latency, normal-round work or performance threshold changed. The
additional state read is only on the failed-completion path. Existing allocation tests passed;
no new benchmark or speedup claim is made. No Player build was needed for this error-state fix.

Scope: three native scheduling files, two existing test fixtures and associated evidence/docs.
The earlier review's red reproduction is documented in the card; it was not repeated merely to
strand allocations in an unrecoverable baseline controller.
