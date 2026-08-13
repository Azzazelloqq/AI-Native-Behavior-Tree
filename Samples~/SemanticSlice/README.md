# Semantic slice

The P1-018 sample assets are executable specifications, not runtime examples to copy into production registries.

1. Parse a canonical tree from `Tests/Fixtures/Golden/Trees`.
2. Validate it against the explicit semantic-slice node registry.
3. Compile it with the Phase 1 reference compiler.
4. Execute the matching case from `Tests/Fixtures/Golden/Cases` through the reference behavior-case adapter.
5. Compare only public root status, blackboard snapshot/version, commands, trace, diagnostics, and step counts.

The fixture node types use the reserved `aibt.test.*` namespace and are intentionally unavailable to production packages.

Phase 1 has no random service or random-consuming node. Semantic-slice cases therefore require `rootSeed: 0`; the adapter retains the field as case metadata and rejects a nonzero seed before constructing the machine.
