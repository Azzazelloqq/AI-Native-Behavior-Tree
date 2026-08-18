# P2-001 feasibility evidence

Status: Accepted. Independent round-5 review accepted the normative contract
and final executable evidence on 2026-08-14. AIBT-020 and the ADR are Accepted;
P2-001 is Done.

The first four 2026-08-14 candidates were rejected. The 293-assertion/26-test
run below supersedes the round-4 checkpoint and is the round-5 candidate. See
`remediation-matrix.md`; round-5 independent review accepted it.

## Pinned environment

- Workspace and Unity project: `<workspace>` (repository root)
- Unity Editor: `6000.5.8f1`
- Unity editor instance observed ready through Unity MCP: `Modules@783f0d4fd8687b7b`
- Unity Roslyn host: `4.10.0.0`, editor-bundled .NET SDK `8.0.318`
- Generator target: Microsoft.CodeAnalysis.CSharp `4.3.1`; no 4.10-only API
- Burst: actual `1.8.29`, checked through requested manifest, resolved depth-zero
  package lock, final log registration, and resolved PackageCache manifest

## Normative artifacts

- `Documentation~/specifications/burst-node-abi-v1.md`
- focused random completion in `Documentation~/specifications/time-and-random-v1.md`
- diagnostic range reservation in `Documentation~/specifications/diagnostics-v1.md`
- Accepted `Documentation~/decisions/ADR-P2-001-public-burst-node-abi.md`
- `decision-matrix.md` and `remediation-matrix.md` in this directory

## Accepted proof matrix

| Proof | Candidate observation | State |
| --- | --- | --- |
| B1 exact public ABI | One compiled contract source; explicit 352-record expected manifest plus exact five-member facade surface; layouts and result representation | ACCEPTED |
| B2 facade and handshake | Runtime-owned host claim from batch creation; all host copies share one Ready-to-Scheduled claim; every job-view copy is stale after terminal; host copies read repeatedly until release; independent bytes pass | ACCEPTED |
| B3 analyzer import | RoslynAnalyzer-only import, clean invalid-to-valid Unity probe, required dependencies resolved, failure-marker gate | ACCEPTED |
| B4 random stream | Actual BurstEnterContext/BurstTickContext jobs prove Enter/re-entry, precedence/single-claim, and abort/observer/budget/rejected no-consume | ACCEPTED |
| B5 enforcement | Exact field/storage/binding/identity/schema/collision/shard-argument spans, additional locations, NotConfigurable, and unusable output; wrong-mode async is `5007` at binding and external selected-shard conflict is `5011` at local `typeof` | ACCEPTED |
| Determinism | Two clean output trees; each run compares normal enumeration with `tr-TR` plus reversed references/shards | PASS |
| Unity/Burst | Exact generated facade and public ABI compile; focused immediate/scheduled/RNG/layout/codec tests execute under Burst | PASS, 26/26 |
| Repository checks | Schema and static verification | PASS |

## Accepted round-5 verification

Exact command from the workspace root:

```text
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Assets/AIBT/Spikes~/BurstNodeAbi/Build-And-Verify.ps1
```

Result: exit code `0`.

- Generator and Runner builds: zero warnings, zero errors.
- Runner: 293 assertions in each of two clean invocations. Each invocation also
  compares two generator compilations under changed culture/reference/shard
  order.
- Generated catalog facade SHA-256:
  `798a7771313c34b2c32db57e2d9a62684802b6fdbfe029a0f6d326d8a536eb6f`.
- Generated UTF-8 size: 32,899 bytes; both clean trees have zero diagnostic
  bytes and are byte-identical.
- Independent BinaryWriter reconstruction of the complete three-shard catalog
  stream: `609b7f68f97b7d03940773a1cd5699129439e0fb4be655fda0bc88724ba0be22`.
  The independently built exact P1 pretty-JSON registry byte stream hashes to
  `7ee137f15483dc75bd251c6469f3f0f189519dfac622a1f8e7498f3f249381a6`.
- `Runner/ExpectedPublicAbiV1.txt` retains 352 explicit signature records,
  47,288 bytes. Reflection output compares line-for-line; secondary manifest
  SHA-256 begins `f7f5b62f`. The generated facade has exactly `IsUsable`,
  `Fingerprint`, `Validate`, `ExecuteImmediate`, and `Schedule`.
- One additional timing-only Runner observation reported 2,679 ms. Unity
  reported 1.168751 s script compilation during the final clean harness import.
  These are feasibility observations, not product thresholds.
- Unity EditMode XML: 26 total, 26 passed, zero failed, zero skipped.
- Unity log: one final Burst registration, version 1.8.29; PackageCache manifest
  version 1.8.29; zero `BCxxxx`, Burst failure, managed fallback, analyzer-load,
  `CS8032`, `AD0001`, or unresolved Roslyn markers.
- Clean invalid analyzer probe emits `AIBT5001`; its log has zero analyzer-load
  or execution-failure markers. The valid project then generates and compiles.
- Generated cleanup scan: zero direct post-acquire failure returns and 53
  `TryFailDispatch` routes.
- Result canaries prove offsets `0/2` and `0/2/4/8`, zero reserved high byte,
  undefined-enum `Success=false`, and bridge `InvalidStatus` rejection with no
  mutation in managed and Burst execution.
- FixedString32 and AssetId fieldwise canonical codec canaries, cancellation
  positive/negative matrix, registered computed-property/auto/equality cases,
  exact collision locations, shared scheduled result repeatability/release,
  and combined random validation precedence all pass.
- The scheduled-view test executes two requests through the Runtime-owned shared
  cursor, permits repeatable host-result reads, and returns `InvalidHandle` from
  the terminal job view. Three focused Burst-context tests execute actual
  Enter/Tick context calls for progression/re-entry, precedence/single-claim,
  and abort/observer/budget/rejected no-consume behavior.
- Focused repository verification: six schemas pass and 50 work items pass
  static validation.

## Round-3 rejection and round-4 remediation

The normative specification already contained the required stale-view,
registered-schema, lifecycle, and diagnostic-location rules. No contract rule
was weakened. The round-2 review required:

1. full exact constructors, properties, methods, overloads, and extra-member
   rejection for every public `AIBT.Burst` type/attribute/result/context/bridge
   plus the generated facade;
2. terminal job-view `InvalidHandle` after completion, distinct from the
   repeatable host result view;
3. nonzero registered and nested registered H32 streams, independent complete
   P1 registry bytes/hash, and the exact RandomStream capability byte rather
   than hash inequality alone;
4. Burst jobs calling the actual Enter/Tick context methods, including Enter
   progression/re-entry, precedence, and abort/budget/observer/rejected
   no-consume behavior;
5. exact primary/additional spans and suppression behavior: `AIBT5004` kind
   argument, `AIBT5009` identity/version/documentation argument, `AIBT5003`
   callback identifier, `AIBT5006` outermost access invocation/expression, and
   one outermost forbidden syntax for `AIBT5008` without nested duplicates.

Round-3 rejected that closure claim. The focused next proof must additionally:

1. allocate/attach one Runtime-owned shared record when the batch is created,
   so every pre-schedule host copy observes the same atomic Ready-to-Scheduled
   claim and cannot enqueue or steal a second schedule;
2. make every job-view copy, including one copied before terminal completion,
   return `InvalidHandle` after terminal; host copies alone retain repeatable
   result access until Runtime release, after which every view is invalid;
3. assert exact B5 locations for the `AIBT5001` offending field, `AIBT5002`
   storage type, `AIBT5007` mismatched binding argument, each applicable
   `AIBT5009` field/value/type/version/schema argument, non-node `AIBT5010`
   primary plus first-declaration additional location, and `AIBT5011`
   offending shard-type argument.

The round-4 Runner and Unity paths now execute the same all-copy lifecycle and
the expanded B5 location matrix. This is a candidate closure claim only; a new
independent review must confirm it before acceptance.

## First-candidate rejection history

The prior rejected candidate used facade hash
`2f7c67dc1f53f1c54bd5eec396582440deba1020320400353c987fec43bb8117`,
20,587 bytes, and Unity 3/3. Its five blockers were:

1. compiled ABI/layout/storage mismatch;
2. missing facade handshake/fingerprint matrix;
3. analyzer plugin load failure not rejected by the script;
4. prose-only RNG with zero context stubs;
5. incomplete generator enforcement and diagnostics.

Those counts remain rejection history only. The exact current closure mapping is
in `remediation-matrix.md`; it does not become accepted until independent review
confirms every row.

## Independent checks retained

- A clean-room C# reimplementation matched all three published random vectors,
  including domain bytes, all 32 hash bytes, 63-bit stream mask, two-step PCG
  seeding, and the first six outputs.
- Independent FNV-1a 64 calculation maps
  `aibt.equality.canonical-bytes.v1` to `69e3a80e385e338e`. Registered-value
  schema SHA-256 remains separate and is not truncated into P1 64-bit IDs.

## Scope and limitations

Unity MCP established the live editor/version/ready state. The isolated batch
harness supplied reproducible clean analyzer, generator, Unity, and Burst
evidence because Unity MCP tools were not exposed to this worker. Raw artifacts,
logs, DLLs, caches, and machine paths remain ignored; retained evidence contains
only sanitized commands, versions, hashes, counts, and observations.

No production Runtime, Authoring, CodeGen, compiler, or executor implementation
is part of P2-001. The rejection/remediation history is retained above;
round-5 independent review accepted the final contract and evidence.
