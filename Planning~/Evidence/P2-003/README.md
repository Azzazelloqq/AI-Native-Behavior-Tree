# P2-003 evidence

Status: Accepted after independent round-4 review on 2026-08-14.

## Outcome

The persisted and runtime-neutral Agent/Shared contract is specified without
production storage or reducers. The candidate fixes:

- self-contained `aibt.tree` format version 2 and compiled format version 2;
- exact Agent/Shared schema, layout, access, and compiled-content hash inputs;
- exact Agent identity, ownership, compatibility, binding, initialization,
  reset, version, and multi-tree lease rules;
- bounded per-instance Shared contribution streams and stable semantic keys;
- deterministic `Min`, `Max`, `Sum`, `Any`, `All`, `First`, and `Last` rules;
- atomic whole-context Reduce, overflow, non-finite float, and `+0` behavior;
- stable semantic compilation failures `AIBT2042` through `AIBT2046`;
- explicit deferral of custom reducers until a future version can reuse the
  accepted P2-001 unmanaged ABI.

## Executable evidence

`Spikes~/BlackboardScopes/Verify.ps1` runs the Node model and 346 assertions,
then runs an independent PowerShell SHA-256 verifier over ten byte streams and
an independent .NET Float32 oracle over eleven pinned bit patterns.
The assertions cover schema/model positives and negatives, exact byte/hash
pins, hash sensitivity, contract composition, all reducer permutations and
batch partitions, numeric negatives, whole-context atomicity, bounded capacity,
and Agent initialization/reset/lease behavior.

Pinned SHA-256 values:

| Stream | Hash |
| --- | --- |
| Agent scope schema | `5a1e99053b79dd8c5d56bfa12c8f1d20af20010c03a656fe99e999c95a35b9f8` |
| Shared scope schema | `aebae0b083761e9cae3fbd17e4dce452eb982166bccf0f8c04043ab6b32ff53d` |
| Agent physical layout | `92ddfc8bdf3a3b2014cdcb39298d6b2718f52a6b08293100d4dfb28040f15bf3` |
| Shared physical layout | `87c027eefd4a4a0ace701e767b46b7edd3d3d0310a57d9790fabf9f3c6d67821` |
| Full compiled v2 | `48eb58bf6aea2b60fb974c604de4799640589056b993f5ebdfdadd80f48cd70c` |
| Agent-only compiled v2 | `0e828802b48b2be5a683122d0607fd66d7350fcfb46e21a313bdbfa4102a2b84` |
| Shared-only compiled v2 | `b358653eb7e1cad9e793b70234fca4f9ff02c56e8a772a77b40afee138c37af3` |
| Lossless Int64 Agent schema | `1e75c80ab1e01ed3148ae3e49b1b3a1428eb55f3ad81c301591e0f9d4f6086a9` |
| Lossless Int64 compiled v2 | `9af7d9e333d279ceec0b28c87303eb3f092ddf168266104f9ee70e7b848a6c81` |
| Full fieldwise typed-default compiled v2 | `7d328fae6054c7c98d367eff89f79639d6755b1ae702025eb68529076f6cb002` |

`canonical-byte-streams.json` stores the exact scope/layout bytes and
`expected-hashes.json` stores independent pins for those streams, all three
full compiled variants, and the lossless Int64 schema/compiled path.
`Verify-Pins.ps1` hashes the emitted bytes through the
.NET cryptography implementation rather than the Node model's hash helper.
`Verify-Float32Oracle.ps1` independently searches .NET Single round-trip forms
and checks exponent/plain/sign/zero/equal-length-tie vectors against the model.

## Scope boundary

No production schema, authoring model, compiled model, native container,
scheduler, or reducer was changed. The version-2 schema and compiled model are
candidate spike artifacts only, as required by the card.
