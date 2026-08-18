# P2-003 independent review round 2 remediation

Status: candidate remediation only; a third independent review is required.

| Review finding | Remediation evidence |
| --- | --- |
| Compiled v2 omitted observer records, watched-slot table, and debug runtime index; optional variants could leave invalid dependencies | `compiledBytes` now covers all three record classes. Full/Shared-only fixtures retain a valid Shared observer; Agent-only removes its dependent node observer and compiled observer/watch entries. Mutations of each new field change the compiled pin. |
| Typed default canonicalization was incomplete and full-width Int64 could pass through JavaScript Number | Canonical validation fixes Quaternion `x,y,z,w`, shortest Float32 round-trip formatting, exact nonzero UInt64 AgentId/EntityId, and the accepted four-field OperationId grammar. `lossless-int64-defaults.json` is tokenized into BigInt during actual JSON parsing and independently pins both schema and compiled bytes. |
| Runtime AgentId and TreeInstanceId validation was not exact UInt64 at every boundary | Registry, bind/unbind/rebind, pass, lease, release, Shared stream owner, and every Shared record validate exact nonzero UInt64 before callback or reduction; zero, negative, non-integral, non-canonical, and overflow cases are asserted. |
| Enum32 contract was lost inside `reduceOne` | Reducer validation now receives the slot type contract. Shared First/Last reject mismatched contracts and preserve the declared contract in the selected canonical value. |
| Shared registered equality callback and atomic failure were not modeled | Whole-context Reduce requires and preflights the registered equality callback after result validation and before commit. Fixtures stage an earlier Bool change, then prove missing, throwing, and non-Boolean callbacks publish no slot/version/revision mutation; a valid callback commits all staged slots once. |

No production schema, authoring, compiled, storage, scheduler, or reducer code is changed by this remediation.
