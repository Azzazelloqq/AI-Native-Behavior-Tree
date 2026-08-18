# P2-023 — Android ARM64 native AOT build conformance

Status: `Done`

## Objective

Prove that the P2 native executor, generated dispatch, and public custom node survive Android ARM64 IL2CPP/Burst AOT and package correctly.

## Depends on

- `P2-012`.
- `P2-018`.
- `P2-019`.
- `P2-020`.

## Required reading

- `Documentation~/specifications/platform-backends-v1.md`
- `Planning~/Evidence/P0-004/README.md`
- `Planning~/USER_ACTIONS.md`

## Allowed changes

- `Tools~/Verification/P2/Android/`
- `Planning~/Evidence/P2-ANDROID/`

## Forbidden changes

- Runtime semantics, architecture-specific public API, device/performance/battery/store claims without actual evidence.

## Deliverables

- Repeatable Android ARM64 IL2CPP/Burst build harness and sanitized AOT/native artifact report.

## Acceptance criteria

- Build retains the native executor, generated dispatch, and custom-node entry points with no Burst compilation/fallback errors.
- APK contains only the intended ARM64 native libraries and expected IL2CPP/Burst artifacts.
- Environment, SDK/NDK/JDK, source, build settings, artifact hashes, size, and symbol/retention checks are recorded.
- Missing module or controlled-invalid binding fails the command.

## Required verification

```text
isolated Android ARM64 IL2CPP/Burst build
APK ABI/library and generated-entry inspection
sanitized build-summary validation
git diff --check
```

## Handoff notes

- Without an approved physical device, acceptance proves build/AOT compatibility only.
