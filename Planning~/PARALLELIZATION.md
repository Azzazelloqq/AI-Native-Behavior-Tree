# Safe parallelization waves

This is a convenience view of `work-items.json`; the machine-readable dependencies remain authoritative.

## Phase 0

```text
P0-001
   |
P0-002
   |
   +--------+
   v        v
P0-004   P0-005

P1-018 -> P0-003
             |
P0-004 ------+------ P0-005
             v
           P0-006 -> P1-019
```

`P0-004` and `P0-005` may use separate agents/worktrees after verification entrypoints are merged. `P0-003` waits for the representative P1-018 executor slice; this avoids benchmarking a throwaway substitute. P1 implementation starts after P0-002 and does not wait for the later platform evidence gate.

## Phase 1

```text
P1-001
   |
   +------------+
   v            v
P1-002        P1-003
   |            |
   +----+-------+
        |
   +----+----+
   v         v
P1-004     P1-005
   |         |
   +----+----+----------------+
        |                     |
   +----+----+                |
   v         v                v
P1-006     P1-007           P1-008
   +---------+----------------+
             v
           P1-009
             v
           P1-010
         +---+---+
         v       v
      P1-011   P1-015
       +--+--+
       v     v
    P1-012 P1-013
       |
    P1-014
       +-----+-----+
             v
           P1-016
             v
           P1-017
             v
           P1-018
             v
           P1-019
```

The simplified diagram omits some cross-dependencies; agents MUST check `work-items.json` before claiming a task.

## Recommended session allocation

- One implementation agent per ready card.
- One independent reviewer may review a completed card while non-overlapping implementation continues.
- One coordinator owns status/index/shared-file updates.
- One integration agent handles each phase gate.

Do not keep an agent idle waiting for a dependency inside the same session. Finish and review the predecessor, merge it, then start a new task session with a fresh card.

## Phase 2

```text
P2-001 public ABI + feasibility
   |
   +-----------+-----------+
   v           v           v
P2-002       P2-003       P2-004
native       scopes       codegen package
contract     contract        |
   |           |           P2-005 generation
   v           |              |
P2-006         +-----> P2-007 blackboard
program/state            |          |
   +--> P2-009 snapshots |        P2-008 shared
   +--> P2-011 trace     |          |
   +--> P2-010 commands <+          |
          \       |      /          |
           +--> P2-012 dispatch <---+
                    |
                 P2-013 lifecycle/memory
                    |
             +------+------+
             v             v
          P2-014         P2-015
          reactive       parallel/decorators
             |              |
          P2-016            |
          observers         |
             +------++------+
                    ||
P2-010 + P2-012 + P2-013 -> P2-017 async
                    ||
                    vv
                 P2-018 budgeting
                    |
P2-008/009/010/011 --+--> P2-019 batched jobs
                              |
                           P2-020 equivalence/golden
                              |
                           P2-021 allocation gate
                         +----+----+
                         v    v    v
                      P2-022 023  P2-024
                      Windows Android Web
                         +----+----+
                              v
                           P2-025 gate
```

After P2-001 is accepted, P2-002, P2-003, and P2-004 may use separate worktrees. P2-006 may proceed beside P2-004/P2-005 because it consumes only accepted logical and ownership contracts. Semantic executor cards join only after generated dispatch and native storage exist. Phase 4 owns pipelined scheduling, Auto/autotuning, calibrated thresholds, and policy defaults.

## First Phase 1 parallel frontier

After `P1-001` is merged, P1-002 proceeds, followed by independent P1-004 and P1-005. P1-003 follows P1-005. The compiler and executor remain intentionally serialized behind shared contracts.
