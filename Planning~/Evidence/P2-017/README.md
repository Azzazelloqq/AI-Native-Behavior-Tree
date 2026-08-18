# P2-017 native async lifecycle evidence

Observed 2026-08-17 with Unity 6000.5.8f1.

- Native Commands/Async assembly: 20/20.
- `NativeAsyncActionLifecycleTests` covers start-once, Running retick, outcome
  mapping, abort-once, and terminal-completion precedence.
- The broader suite covers typed completion payloads, stale generations,
  tombstones, fault cleanup, restart, deterministic merge, Burst jobs, capacity,
  lifetime, and zero managed allocation after initialization.
- XML SHA-256:
  `a838c2b308902013511c843c008ea1e5e0a05283140116b7d5b8afadfb47e9cd`.
- Log SHA-256:
  `941bf77a7a62202304d3fc0a09655e0fd7e67c7fc56aaaeb75dbc1d5c24b3b23`.
