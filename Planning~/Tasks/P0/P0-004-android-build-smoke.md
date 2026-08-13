# P0-004 — Android ARM64 build smoke

Status: `Done`

## Objective

Prove the empty package and test harness can produce an IL2CPP Android ARM64 Player with Burst enabled.

## Depends on

- `P0-001`
- `P0-002`

## Allowed changes

- `Tools~/Verification/Android/`
- `Planning~/Evidence/P0-004/`
- Minimal test-host project configuration owned by the task.

## Forbidden changes

- Runtime semantics, benchmark thresholds, signing credentials, or publishing configuration.

## Deliverables

- Repeatable unsigned/development build command.
- Sanitized build log and exact SDK/NDK/JDK/version record.
- Documented device-run follow-up when a device is available.

## Acceptance criteria

- ARM64 IL2CPP build succeeds with Burst enabled.
- No secret or machine-local absolute path is committed.
- Failure without installed modules is actionable.

## Required verification

- Clean Android Player build from the command entrypoint.
- Inspect output architecture and build log for Burst compilation.
