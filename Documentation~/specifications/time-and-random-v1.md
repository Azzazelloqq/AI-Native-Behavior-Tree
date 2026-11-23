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
- Every random-consuming runtime node index derives its initial stream deterministically from root seed, all 32 raw tree semantic-hash bytes, the stream identity, and runtime node index using the combiner below. Which 64-bit word is the stream identity is the host's policy (see [Stream identity](#stream-identity)).
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
stream identity as unsigned 64-bit little-endian
runtime node index as unsigned 32-bit little-endian
```

Zero is invalid for the stream identity; `0xffffffff` is invalid for runtime node
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
fresh streams from the unchanged root seed, semantic hash, stream identity,
and runtime node indices using the exact initial sequence above. Restart does
not change the stream identity; therefore the post-restart sequence intentionally
matches a newly created instance with the same derivation inputs. A host that
requires a distinct sequence supplies a distinct root seed or stream identity
according to its lifecycle policy ([Stream identity](#stream-identity)).

Abort, cancellation, Exit, observer evaluation, budget suspension/resume,
rejected context operations, failed/rejected frame completion, diagnostics,
and trace capture never advance the committed stream. Hot reload preserves a stream only when the hot-reload compatibility
contract accepts the same node identity/version and random-state layout;
otherwise subtree restart applies the derivation above.

### Stream identity

The stream identity is the 64-bit word mixed after the semantic hash. Until
P11-012 this word was named `TreeInstanceId`; the bytes, their position and the
derivation name `AIBT-PCG-XSH-RR32-v1` are unchanged, so every stream derived
from the same four inputs is unchanged. Each host states its policy:

- `TreeSimulation`: the agent's container-local `TreeInstanceId`, 1, 2, ... in
  the order agents are added or restarted in that container.
- `ProductionTreeHost`, default (a bootstrap without a stream identity, or with
  identity 0): its own `InstanceId`, assigned from one process-wide counter.
  A bootstrap the host refuses at once (already bootstrapped, disposed, or
  called while it drives an update) takes no id; one that fails later (the
  catalog cannot be retained or does not match, instance or trace capacity
  cannot be allocated) has taken one; the managed-dispatch overload takes its
  id first. It is unique within the process,
  so no two hosts share streams, but it is not repeatable across sessions: it
  depends on how many hosts were bootstrapped before (start order, other
  hosts, Enter Play Mode without a domain reload).
- `ProductionTreeHost` bootstrapped with a nonzero stream identity
  (`RandomStreamSeeding`): that identity, as given. `InstanceId` stays
  process-wide for its other uses (handles, traces, scheduler order).
- `NativeTreeAgent`: with `RandomStreamId` 0, the host default. With a nonzero
  `RandomStreamId`, the identity of a run is
  `CombineStreamIdentity(RandomStreamId, runIndex)`. The run index is 0 for
  the first run after the component is enabled and +1 for each run that starts
  while it stays enabled (automatic restart, `Restart`, a change of `Tree`); a
  start that fails does not consume one. One agent life lasts from enable to
  disable, so an agent disabled and enabled again with the same id repeats run 0.

The combine function maps a stream id and a run index to a nonzero stream
identity, with unsigned 64-bit wrap and `Mix64` as above:

```text
CombineStreamIdentity(streamId, runIndex):
    z = Mix64(Mix64(streamId xor 0x414942545354524d) + runIndex)
    if z == 0: return 0x414942545354524d
    return z
```

`0x414942545354524d` is ASCII `AIBTSTRM`; the function never yields 0. For one
stream id two run indexes give one value only at that constant: the run whose
mix is 0 is mapped to it, and one other run reaches it by itself (for stream
id 1, runs `0fa0ecdca6488920` and `7d6af27209acafb0`). Across stream ids values
collide, but not between neighbouring pairs: (a, r) equals
(b, r + Mix64(a xor D) - Mix64(b xor D)) for every r, with D the constant (id 1
and id 2 are `5bb7ebdb0fec06fe` runs apart), whereas `streamId + runIndex` or
`streamId xor runIndex` collide at (1, 1) and (2, 0). (Amended 2026-11-24,
P11-014: this paragraph called the function a bijection of the run index.) Test vectors (from an independent reference implementation):

| stream id | run index | stream identity |
| --- | --- | --- |
| `0000000000000001` | `0000000000000000` | `17c7da942a926088` |
| `0000000000000001` | `0000000000000001` | `380638fd2ee43a95` |
| `0000000000000002` | `0000000000000000` | `4b94756ef81e1100` |
| `00000000075bcd15` | `0000000000000003` | `8f47c4ae40c92095` |
| `ffffffffffffffff` | `ffffffffffffffff` | `bd8b4b5991a381ef` |
| `0000000000000001` | `0fa0ecdca6488920` | `414942545354524d` (the mix is 0) |

A derived stream with a combined identity: root seed `0123456789abcdef`,
semantic hash `000102...1e1f`, stream id 7, run index 2 (stream identity
`89076cda0e1cdaaf`), node index 42: initial state `89ad669f83b79db7`, increment
`31325367a6526d8f`, seeded state `061ebb045d6c22dd`, outputs `c3d7507e 7ba12ba7
b2818e51 bb6beb1c e8a5545c 327c490f`.

Equal streams are not equal behaviour. A stream identity makes the numbers a
node can draw repeatable; it does not make a session repeat. Hosts driven by a
`ProductionTreeScheduler` update in an order that follows start order (its last
tie-break is `InstanceId`), and nodes draw as frame time lets them run, so the
same streams can be consumed at other updates. A lockstep replay with a fixed
tick and inputs is `TreeSimulation`'s job.

### Published vectors

Hexadecimal fields are fixed-width and lowercase. `outputs` lists the first six
observable `NextUInt32` values after the two discarded seeding advances.

| root seed | semantic hash | stream identity | node index | initial state | increment | seeded state | outputs |
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
