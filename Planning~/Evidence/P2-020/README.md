# P2 native equivalence evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- The five mechanically enumerated P1 golden cases all passed the native adapter:
  `patrol-react`, `parallel-decorator`, `async-completion`,
  `async-budgeted-abort`, and `initial-blackboard`.
- Every case ran through all three fixed policies: Immediate, one-step Budgeted,
  and BatchedJobsSameFrame (15 policy/case executions).
- The adapter compiles the original tree fixture, drives `NativeLifecycleMachineV1`,
  and observes native lifecycle, blackboard/version, observer replacement,
  async operation/command, diagnostics, trace, active-node, and step outputs.
- Exact focused NUnit result: 7/7 passed (five golden cases plus the two retained
  synthetic full-trace/parallel partition canaries).
- NUnit XML SHA-256:
  `6ab39ba438070c0d56345943fbab7652c808ada7ee4de9d4e52445277f48a30b`.
- Unity log SHA-256:
  `817d0bd9cc3b0f84962a8df149f22be6df92c839d49d1fed9aafe59574bc5fa3`.

The public generated-node path is independently exercised by the clean CodeGen/
Dispatch consumer gate and platform Player harnesses. This evidence is semantic;
allocation and platform claims belong to P2-021 through P2-024.

The Package Manager `Public Burst Nodes` sample is also copied into a clean Unity
project by `Build-And-Verify.ps1`. Unity must compile its separate public shard
and catalog assemblies. The sample covers typed read/write, observer Condition,
Action `Running`, command emission, async start/completion, and Abort cancellation
without internal Runtime access.
