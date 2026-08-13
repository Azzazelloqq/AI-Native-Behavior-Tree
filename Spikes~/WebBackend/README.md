# Web backend spike harness

`Build-WebBackend.ps1` creates a disposable Unity project under the ignored verification output, copies the current AIBT Runtime, Authoring, behavior-case framework, P1-018 adapter, and golden fixtures, then makes a non-development IL2CPP WebGL Player with compression disabled.

The Player runs all five accepted semantic-slice cases, compares immediate and one-step-budgeted execution, and records coarse managed-memory and throughput observations. It does not modify or impersonate a native executor.

`serve-and-run.mjs` serves the build and launches the installed Chrome and Firefox binaries headlessly. Each real Player posts its self-reported result to the local server.

The current managed reference executor cannot be placed inside `IJob.Run` or a Burst direct function. Those variants remain unsupported until the native packed executor exists; a synthetic executor would not satisfy P0-003.
