# P4-008 Android ARM64 platform benchmark evidence

Runs `P4-001`'s exact scenario/policy sweep inside a real, non-development, IL2CPP, Burst-enabled
Android ARM64 build, installed and run on **genuine ARM64 hardware** -- a physical phone (`adb`
over USB), not an emulator. All three fixed policies (`Immediate`, `Budgeted`,
`BatchedJobsSameFrame`) are measured, since Android is a full "Native backend" target per
`Documentation~/specifications/platform-backends-v1.md` (unlike single-thread Web).

## Why this needed a real device, not the locally available emulator

Only an `x86_64` system image and AVD (`Pixel_10_Pro`, ironically named after the same device
model measured here) were available locally -- no `arm64-v8a` image was downloaded. An x86_64
emulator does not satisfy `Planning~/USER_ACTIONS.md`'s "identify an Android ARM64 device class,"
and even a downloaded arm64-v8a system image would only be QEMU-emulated ARM64-on-x86_64, not
genuine hardware performance evidence. The user connected their own physical Android phone via USB
(`adb devices` confirmed `arm64-v8a`/`arm64-v8a` ABI, Android OS 17 / API-37) -- real hardware, not
a substitute.

## Build, install, run

`Build-AndroidPlatformBenchmark.ps1` builds an isolated project (copying `Runtime/`, `Authoring/`,
`Tests/Runtime/Benchmarking/SchedulingPolicyDriver.cs`, and
`Benchmarks~/Phase4/Scheduling/Unity/SchedulingScenarios.cs` unchanged) and produces a release,
ARM64-only, IL2CPP, Burst-enabled APK. It was then installed (`adb install -r`), launched
(`adb shell monkey -p com.aibt.p4008platformbenchmark -c android.intent.category.LAUNCHER 1`), and
its result read via `adb logcat` -- like the Web probe, an Android app has no simple, reliable
arbitrary filesystem write across API levels without extra permission ceremony this card does not
need, so results are logged (one scenario per marked log line, to stay comfortably under logcat's
per-entry size limit) rather than written to a file. The app was uninstalled from the user's phone
immediately after reading results (`adb uninstall`) -- this is the user's own personal device, not
a disposable isolated project.

## Result

Confirmed real ARM64 hardware execution: `deviceModel: "Google Pixel 10 Pro"`,
`processorType: "ARM64 FP ASIMD AES"`, `processorCount: 8`, `systemMemoryMB: 15575`,
`operatingSystem: "Android OS 17 / API-37"`, `burstEnabled: true`, `is64Bit: true`. Build evidence
separately confirms `target: Android`, `architecture: ARM64`, `scriptingBackend: IL2CPP`,
`result: Succeeded`, 0 errors/warnings, non-development.

### Windows desktop vs. Android phone (Immediate policy, 16 agents)

| Scenario | Windows ns/agent | Android (Pixel 10 Pro) ns/agent | Windows/Android ratio |
| --- | ---: | ---: | ---: |
| `scheduling-baseline-empty-job` | 218.75 | 287.50 | 0.76x (Android within 1.3x of desktop) |
| `shallow-tree-cheap-conditions` | 1,125.00 | 1,468.75 | 0.77x |
| `deep-sequence-selector-traversal` | 12,625.00 | 15,106.25 | 0.84x |
| `wide-branching-frequent-failures` | 356.25 | 450.00 | 0.79x |
| `predominantly-running-actions` | 268.75 | 293.75 | 0.91x |
| `many-programs-small-populations` | 218.75 | 262.50 | 0.83x |

A genuinely notable finding: this Windows desktop workstation is only **roughly 1.1x-1.3x** faster
than a current-generation phone for this workload -- much closer than the ~13-14x Editor-vs-Player
gap `Benchmarks~/Phase4/Platform/Windows/README.md` found on the *same* desktop. Mobile ARM64
silicon in a 2026-class flagship phone is not a dramatically weaker target for this workload than
a desktop x64 CPU.

### BatchedJobsSameFrame overhead reproduces on ARM64 too

At 16 agents, `BatchedJobsSameFrame` costs roughly **18x-23x** more than `Immediate` on this Android
device (e.g. `deep-sequence-selector-traversal`: 352,643.75 vs. 15,106.25 ns/agent, 23.35x) --
in the same range as Windows's own ~21x-29x ratio at the same agent count. The fixed-batch-size
scheduling-overhead mechanism `P4-002`/`P4-006` traced, and that `P4-008`'s Windows pass already
confirmed is not an Editor-only artifact, now reproduces on a third, architecturally different
platform (ARM64 mobile silicon) too -- it is a property of the scheduling code's interaction with
Unity's Job system, not specific to one CPU architecture or OS.

## Scope and limitations

- Reduced parameter matrix versus the Windows Player probe (3 agent counts: 16/64/256, not 1024;
  3 warmup + 7 measured samples, not 5+15) -- matching the Web probe's reduction, chosen to keep
  each logcat-logged scenario line compact and the on-device run quick.
- One run on one physical device (a single Google Pixel 10 Pro); not generalized to other Android
  devices, chipsets, or OS versions (`Planning~/USER_ACTIONS.md` requires owner approval of
  hardware classes before any threshold or support claim is adopted). This is one data point for
  one ARM64 device class, not a claim about "Android" broadly.
- No regression threshold or "supported" performance claim is drawn from any number here.
- This card measures the same 6 implemented `P4-001` scenarios already proven in the Editor; it
  does not add new scenarios, and `PipelinedJobs`/`Auto` are not exercised here either (matching
  `P4-001`'s own scope).

## Recorded evidence

The canonical 2026-08-21 run is preserved as
[raw JSON](Results/android-player-scheduling-20260821.json) (captured from `adb logcat`) and
[build evidence](Results/android-build-20260821.build.raw.json). The APK and the Unity build log
are not committed (large generated binary / raw log, per repository policy).
