# Blackboard scope contract spike

This spike fixes the P2-003 persisted Agent/Shared model without implementing
production storage or reducers.

Artifacts:

- `tree-v2.schema.json`: proposed versioned authoring schema;
- `Fixtures/`: canonical, reordered, invalid, layout, hash, and reduction vectors;
- `model-tests.mjs`: independent executable contract model;
- `Verify.ps1`: focused verification entry point.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Spikes~/BlackboardScopes/Verify.ps1
```

The model validates semantic conditions that JSON Schema cannot express,
constructs exact canonical scope/layout and full v1-derived compiled-v2
streams, checks pins through an independent PowerShell SHA-256 verifier, models
Agent ownership/binding/reset, and permutes contribution insertion order for
every built-in reducer.
