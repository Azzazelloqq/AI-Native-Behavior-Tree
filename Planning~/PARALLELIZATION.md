# Safe parallelization waves

This is a convenience view of `work-items.json`; the machine-readable dependencies remain authoritative.

## Phase 0

```text
P0-001
   |
P0-002
   |
   +--------+--------+
   v        v        v
P0-003   P0-004   P0-005
   +--------+--------+
            v
          P0-006
```

`P0-003`, `P0-004`, and `P0-005` may use separate agents/worktrees after verification entrypoints are merged.

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

## First Phase 1 parallel frontier

After `P0-006` and `P1-001` are merged, `P1-002` and `P1-003` can proceed independently. After their merge, registry and blackboard work can proceed independently. The compiler and executor remain intentionally serialized behind shared contracts.
