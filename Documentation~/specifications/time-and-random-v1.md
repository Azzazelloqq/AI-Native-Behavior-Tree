# Time and random v1

## Time

AIBT time uses signed 64-bit microseconds.

- `Duration` is a non-negative microsecond count unless a specific API explicitly permits a sentinel.
- `TimePoint` is a microsecond value from a host-defined monotonic epoch.
- Runtime nodes never read `UnityEngine.Time`, wall-clock APIs, or browser time directly.
- Each execution pass receives one immutable time point from its input snapshot.
- Addition and deadline comparison are checked; overflow is a structured error in validation/development paths and a safe rejected operation in players.

Canonical JSON duration fields use integer microseconds or an explicitly schema-defined duration object. Floating seconds are not canonical v1 duration storage.

## Random

Deterministic random uses PCG-XSH-RR 32 with 64-bit state and 64-bit odd stream increment. The repository implementation MUST include published test vectors created from the algorithm definition, not from the implementation under test.

Rules:

- Project/host supplies a 64-bit root seed.
- A tree instance derives its initial stream deterministically from root seed, tree semantic hash, and `TreeInstanceId` using the repository-defined hash combiner introduced with the implementation task.
- Random-consuming nodes own explicit stream state in node or tree memory; no global Unity random state is used.
- Bounded integers use rejection sampling and are unbiased.
- `Float32` in `[0,1)` uses the upper 24 random bits divided by `2^24`.
- Abort and budget suspension do not consume additional random values.

The exact hash-combiner test vectors MUST be accepted before the first random node is implemented. Until then, no agent may invent a seed derivation function.
