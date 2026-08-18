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

Deterministic random uses PCG-XSH-RR 32 with 64-bit state and 64-bit odd stream increment. The repository implementation MUST include the published test vectors below, created independently from the implementation under test.

Rules:

- Project/host supplies a 64-bit root seed.
- Every random-consuming runtime node index derives its initial stream deterministically from root seed, all 32 raw tree semantic-hash bytes, `TreeInstanceId`, and runtime node index using the combiner below.
- Each random-consuming runtime node index owns explicit Runtime-private stream
  state separate from public `TMemory`; no global Unity random state is used.
- Bounded integers use rejection sampling and are unbiased.
- `Float32` in `[0,1)` uses the upper 24 random bits divided by `2^24`.
- Abort and budget suspension do not consume additional random values.

### Stream derivation

The combiner input is this exact byte stream:

```text
ASCII "AIBT-PCG-XSH-RR32-v1" followed by one zero byte
root seed as unsigned 64-bit little-endian
all 32 raw semantic SHA-256 bytes in displayed hexadecimal order
TreeInstanceId as unsigned 64-bit little-endian
runtime node index as unsigned 32-bit little-endian
```

Zero is invalid for `TreeInstanceId`; `0xffffffff` is invalid for runtime node
index. The combiner processes every byte in order, with unsigned 64-bit wrap:

```text
Mix64(x):
    z = x + 0x9e3779b97f4a7c15
    z = (z xor (z >> 30)) * 0xbf58476d1ce4e5b9
    z = (z xor (z >> 27)) * 0x94d049bb133111eb
    return z xor (z >> 31)

accumulator = 0x243f6a8885a308d3
for each input byte b:
    accumulator = Mix64(accumulator xor b)

initialState = Mix64(accumulator xor 0xa0761d6478bd642f)
streamWord  = Mix64(accumulator xor 0xe7037ed1a0b428db)
streamSelector = streamWord and 0x7fffffffffffffff
increment   = (streamSelector << 1) | 1
```

The PCG multiplier is `6364136223846793005`. Seeding uses the reference PCG
sequence, with unsigned wrap:

```text
state = 0
advance once and discard output
state = state + initialState
advance once and discard output
```

The next advance produces the first value observable by a node. An advance
uses the old state for XSH-RR output, then assigns
`state = oldState * 6364136223846793005 + increment`. XSH-RR is
`rotateRight32(uint(((oldState >> 18) xor oldState) >> 27), oldState >> 59)`.

The committed state and increment are private runtime storage. Public contexts
expose operations, never either word. For a random-capable node, the Burst ABI
Enter/Tick factory copies both words into the matching mutable opaque context.
A non-random node has no committed stream storage; its context contains the
canonical inert pair `state=0`, `increment=1`, while its opaque token records
that the capability is absent. A random call on that context returns
`PhaseViolation` without changing the pair. A successful random
operation advances only that private context copy; it does not mutate the
Runtime-owned stream directly.

### Stream lifecycle

Every random-consuming node receives its stream when a tree instance is
created, after the compiled program/catalog/layout handshake succeeds and
before its first `Enter`. A node without the declared random capability has no
stream storage and cannot call random operations.

`Enter` and `Tick` continue the same node-owned stream across eligible updates
and budget resumes. Only successful completion of the matching Enter/Tick frame
atomically replaces the committed state with that context's advanced state.
Completion of a non-random context validates the inert pair and persists no RNG
state. Invalid status, failed or rejected completion, frame failure, and a callback
that never starts because its budget is exhausted discard the private copy and
leave the committed stream unchanged. A copied context is bit-identical; only
the first matching completion may claim the live frame, and every later claim
through either copy is stale. A terminal `Exit`, aborted `Exit`, and ordinary
re-entry of the node do not reseed or reset it. Tree `Restart` and explicit tree reset derive
fresh streams from the unchanged root seed, semantic hash, tree instance ID,
and runtime node indices using the exact initial sequence above. Restart does
not change `TreeInstanceId`; therefore the post-restart sequence intentionally
matches a newly created instance with the same derivation inputs. A host that
requires a distinct sequence supplies a distinct root seed or tree instance
identity according to its lifecycle policy.

Abort, cancellation, Exit, observer evaluation, budget suspension/resume,
rejected context operations, failed/rejected frame completion, diagnostics,
and trace capture never advance the committed stream. Hot reload preserves a stream only when the hot-reload compatibility
contract accepts the same node identity/version and random-state layout;
otherwise subtree restart applies the derivation above.

### Published vectors

Hexadecimal fields are fixed-width and lowercase. `outputs` lists the first six
observable `NextUInt32` values after the two discarded seeding advances.

| root seed | semantic hash | instance | node index | initial state | increment | seeded state | outputs |
| --- | --- | ---: | ---: | --- | --- | --- | --- |
| `0000000000000000` | `0000000000000000000000000000000000000000000000000000000000000000` | 1 | 0 | `114fca7ce1cd0d61` | `364142da8f45ed0b` | `cd0663b1aab38607` | `650f0350 19bf2775 93792ebd f8d15448 80f1bd3c 1312f9f2` |
| `0123456789abcdef` | `000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f` | 1 | 42 | `25f040c812858258` | `8d9ea478a3f51455` | `b63505dd96f263be` | `94286b1a 4ff48da5 ce86bc0d 55e6545a 8ba0f814 83be6712` |
| `ffffffffffffffff` | `ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff` | 18364758544493064720 | 4294967294 | `8efd18f4fddea327` | `32bea5d2ab2b8077` | `aa817c070c95253d` | `56a75281 2089b2de 5e76d072 81b053c5 0dde67a2 c869d193` |

Bounded generation with `boundExclusive == 0` is rejected and consumes no
value. For a positive bound, compute `threshold = (0 - boundExclusive) mod
boundExclusive`; draw until `value >= threshold`, then return `value mod
boundExclusive`. `NextFloat32` uses the upper 24 bits of one `NextUInt32` value
divided by `16777216.0f`.

Public context validation precedence is token/frame liveness (`InvalidHandle`),
then random capability and expected odd increment (`PhaseViolation`), then the
nonzero bound (`InvalidStatus`). Combined-invalid inputs return the first result
in that order, set output to default, and preserve context and committed state.
