# Known limitations after Phase 1

- The executor is the managed deterministic reference backend; native packed data, Burst dispatch, jobs scheduling, and production performance belong to Phase 2.
- The visual editor, debugger/profiler UI, code generation, hot reload, and MCP integration are not implemented.
- Agent and Shared blackboard storage/reduction are outside the Phase 1 Tree-scope runtime.
- Registered values require explicit runtime bindings; arbitrary managed payloads are not accepted.
- Android evidence is build-only; no device execution or throughput claim exists.
- Web evidence covers tested Chrome/Firefox desktop versions in single-thread modes. Safari and mobile browsers are unverified.
- The remote Unity CI job requires the owner-managed pre-activated self-hosted runner documented in `USER_ACTIONS.md`.
