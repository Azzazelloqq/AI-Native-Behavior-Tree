# P2-003 independent review round 3 remediation

Status: candidate remediation only; a fourth independent review is required.

| Review finding | Remediation evidence |
| --- | --- |
| Float32 selected first round-tripping precision rather than the shortest JSON text | The canonicalizer enumerates canonical plain/exponent forms through nine significant digits, selects minimum UTF-8 length, and breaks equal-length ties by ordinal text. `Verify-Float32Oracle.ps1` independently searches .NET Single candidates for eleven pinned raw-bit vectors including `1e-6`, signs, both zeros, plain/exponent choices, and equal-length ties. |
| Compiled defaults lacked fieldwise codecs and pins for composite/opaque built-ins | `encodeCompiledDefault` implements exact Float2, Float3, Quaternion `x,y,z,w`, Enum32 contract/value/padding, AgentId, EntityId, OperationId four-field, and AssetId fieldwise bytes. Hard-coded byte assertions and a tenth independent full compiled-stream hash pin cover every case. |
| Enum32 First/Last matrix was incomplete | First and Last positive vectors preserve exact contract/value. Both reducers reject a mismatched input, and whole-context fixtures stage an earlier Bool then prove no slot/version/revision publication. |
| Invalid Shared TreeInstanceId streams were constructed but not reduced | Zero, negative, and greater-than-UInt64 stream owners are each passed to `reduceContext`; all return pre-reduce `invalid-stream` and preserve the entire original context. |

No production schema, authoring, compiled, storage, scheduler, or reducer code is changed by this remediation.
