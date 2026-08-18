# ADR-P2-024 — Web uses the unmanaged single-thread backend

Status: Accepted

## Decision

Web keeps the public `SingleThreadImmediate` and `SingleThreadBudgeted` policies.
Their implementation uses the same packed unmanaged runtime and generated dispatch
contracts as the native backend, executed on the Web main thread. No public Web jobs,
worker parallelism, `BatchedJobsSameFrame`, or hidden frame latency are introduced.

## Evidence boundary

Unity 6000.5.8f1 non-Development WebGL IL2CPP/Burst builds and executes the generated
dispatch canary in desktop Chrome and Firefox. The same callback, canonical memory,
status, and managed-path sentinel assertions pass in both browsers. This supports the
unmanaged direct implementation choice; it does not establish Safari/mobile support,
worker scheduling, or a performance default.
