# AIBT.Runtime -- public API reference (generated)

Source: live reflection over `AIBT.Runtime`'s own compiled public surface (`P7-014`). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

A type's own summary line is shown where an XML-doc `<summary>` exists in source; member-level doc-comment text is not yet correlated here (see this document's own generator comment for why) -- every member still gets its own full signature line regardless of whether prose exists for it.
255 public type(s).

---

### `AIBT.AgentId`

- `METHOD AIBT.AgentId Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.AgentId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.AgentId&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.UInt64)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt64 Value`

---

### `AIBT.AssetId`

- `METHOD AIBT.AssetId Parse(System.String,System.Nullable`1<System.Int64>)`
- `METHOD System.Boolean Equals(AIBT.AssetId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,System.Nullable`1<System.Int64>,AIBT.AssetId&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToGuidString()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.UInt64,System.UInt64,System.Int64,System.Boolean)`
- `PROPERTY System.Boolean HasLocalFileId`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.Int64 LocalFileId`
- `PROPERTY System.UInt64 GuidHigh`
- `PROPERTY System.UInt64 GuidLow`

---

### `AIBT.BlackboardFixedStringCapacity`

- `FIELD System.Int32 FixedString128`
- `FIELD System.Int32 FixedString32`
- `FIELD System.Int32 FixedString512`
- `FIELD System.Int32 FixedString64`

---

### `AIBT.BlackboardScope`

- `FIELD AIBT.BlackboardScope Agent`
- `FIELD AIBT.BlackboardScope NodeLocal`
- `FIELD AIBT.BlackboardScope Shared`
- `FIELD AIBT.BlackboardScope Tree`
- `FIELD System.Byte value__`

---

### `AIBT.BlackboardTypeDescriptor`

- `METHOD AIBT.BlackboardTypeDescriptor FromRegistered(AIBT.RegisteredUnmanagedTypeDescriptor)`
- `METHOD System.Boolean Equals(AIBT.BlackboardTypeDescriptor)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY AIBT.BlackboardValueType ValueType`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.Int32 Alignment`
- `PROPERTY System.Int32 Size`
- `PROPERTY System.UInt32 Version`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.BlackboardValue`

- `METHOD AIBT.BlackboardValue FromAgentId(AIBT.AgentId)`
- `METHOD AIBT.BlackboardValue FromAssetId(AIBT.AssetId)`
- `METHOD AIBT.BlackboardValue FromBool(System.Boolean)`
- `METHOD AIBT.BlackboardValue FromEntityId(AIBT.EntityId)`
- `METHOD AIBT.BlackboardValue FromEnum32(AIBT.Enum32Value)`
- `METHOD AIBT.BlackboardValue FromFloat2(AIBT.Float2Value)`
- `METHOD AIBT.BlackboardValue FromFloat3(AIBT.Float3Value)`
- `METHOD AIBT.BlackboardValue FromFloat32(System.Single)`
- `METHOD AIBT.BlackboardValue FromFloat64(System.Double)`
- `METHOD AIBT.BlackboardValue FromInt32(System.Int32)`
- `METHOD AIBT.BlackboardValue FromInt64(System.Int64)`
- `METHOD AIBT.BlackboardValue FromOperationId(AIBT.OperationId)`
- `METHOD AIBT.BlackboardValue FromQuaternion(AIBT.QuaternionValue)`
- `METHOD AIBT.BlackboardValue FromString128(System.String)`
- `METHOD AIBT.BlackboardValue FromString32(System.String)`
- `METHOD AIBT.BlackboardValue FromString512(System.String)`
- `METHOD AIBT.BlackboardValue FromString64(System.String)`
- `METHOD System.Boolean Equals(AIBT.BlackboardValue)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryGetAgentId(AIBT.AgentId&)`
- `METHOD System.Boolean TryGetAssetId(AIBT.AssetId&)`
- `METHOD System.Boolean TryGetBool(System.Boolean&)`
- `METHOD System.Boolean TryGetEntityId(AIBT.EntityId&)`
- `METHOD System.Boolean TryGetEnum32(AIBT.Enum32Value&)`
- `METHOD System.Boolean TryGetFixedString(System.String&)`
- `METHOD System.Boolean TryGetFloat2(AIBT.Float2Value&)`
- `METHOD System.Boolean TryGetFloat3(AIBT.Float3Value&)`
- `METHOD System.Boolean TryGetFloat32(System.Single&)`
- `METHOD System.Boolean TryGetFloat64(System.Double&)`
- `METHOD System.Boolean TryGetInt32(System.Int32&)`
- `METHOD System.Boolean TryGetInt64(System.Int64&)`
- `METHOD System.Boolean TryGetOperationId(AIBT.OperationId&)`
- `METHOD System.Boolean TryGetQuaternion(AIBT.QuaternionValue&)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY AIBT.BlackboardValueType Type`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.BlackboardValueType`

- `FIELD AIBT.BlackboardValueType AgentId`
- `FIELD AIBT.BlackboardValueType AssetId`
- `FIELD AIBT.BlackboardValueType Bool`
- `FIELD AIBT.BlackboardValueType EntityId`
- `FIELD AIBT.BlackboardValueType Enum32`
- `FIELD AIBT.BlackboardValueType FixedString128`
- `FIELD AIBT.BlackboardValueType FixedString32`
- `FIELD AIBT.BlackboardValueType FixedString512`
- `FIELD AIBT.BlackboardValueType FixedString64`
- `FIELD AIBT.BlackboardValueType Float2`
- `FIELD AIBT.BlackboardValueType Float32`
- `FIELD AIBT.BlackboardValueType Float3`
- `FIELD AIBT.BlackboardValueType Float64`
- `FIELD AIBT.BlackboardValueType Int32`
- `FIELD AIBT.BlackboardValueType Int64`
- `FIELD AIBT.BlackboardValueType Invalid`
- `FIELD AIBT.BlackboardValueType OperationId`
- `FIELD AIBT.BlackboardValueType Quaternion`
- `FIELD AIBT.BlackboardValueType Registered`
- `FIELD System.Byte value__`

---

### `AIBT.BuiltInBlackboardTypes`

- `FIELD AIBT.BlackboardTypeDescriptor AgentId`
- `FIELD AIBT.BlackboardTypeDescriptor AssetId`
- `FIELD AIBT.BlackboardTypeDescriptor Bool`
- `FIELD AIBT.BlackboardTypeDescriptor EntityId`
- `FIELD AIBT.BlackboardTypeDescriptor Enum32`
- `FIELD AIBT.BlackboardTypeDescriptor FixedString128`
- `FIELD AIBT.BlackboardTypeDescriptor FixedString32`
- `FIELD AIBT.BlackboardTypeDescriptor FixedString512`
- `FIELD AIBT.BlackboardTypeDescriptor FixedString64`
- `FIELD AIBT.BlackboardTypeDescriptor Float2`
- `FIELD AIBT.BlackboardTypeDescriptor Float32`
- `FIELD AIBT.BlackboardTypeDescriptor Float3`
- `FIELD AIBT.BlackboardTypeDescriptor Float64`
- `FIELD AIBT.BlackboardTypeDescriptor Int32`
- `FIELD AIBT.BlackboardTypeDescriptor Int64`
- `FIELD AIBT.BlackboardTypeDescriptor OperationId`
- `FIELD AIBT.BlackboardTypeDescriptor Quaternion`
- `METHOD System.Boolean TryGet(AIBT.BlackboardValueType,AIBT.BlackboardTypeDescriptor&)`

---

### `AIBT.Burst.AibtAsyncOperationBindingAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtBlackboardBindingAttribute`

- `METHOD System.Void .ctor(System.String,AIBT.Burst.BurstBlackboardAccess,AIBT.BlackboardScope,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtBurstNodeAttribute`

- `METHOD System.Void .ctor(System.String,System.UInt32,AIBT.Burst.BurstNodeKind,System.Type,System.Type,AIBT.NodeMemoryLifetime,System.Boolean,AIBT.Burst.BurstCancellationMode,AIBT.Burst.BurstNodeCost,AIBT.Burst.BurstNodeStatusMask)`

---

### `AIBT.Burst.AibtBurstValueAttribute`

- `METHOD System.Void .ctor(System.String,System.UInt32,System.String)`

---

### `AIBT.Burst.AibtCatalogSetAttribute`

- `METHOD System.Void .ctor(System.String,System.UInt32,System.Type[])`

---

### `AIBT.Burst.AibtCatalogShardAttribute`

- `METHOD System.Void .ctor(System.String,System.UInt32)`

---

### `AIBT.Burst.AibtCommandBindingAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtCompletionBindingAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtConfigFieldAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtMemoryFieldAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtNodeDocumentationAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.String,System.String,System.String[])`

---

### `AIBT.Burst.AibtObserverConditionAttribute`

- `METHOD System.Void .ctor()`

---

### `AIBT.Burst.AibtRandomStreamAttribute`

- `METHOD System.Void .ctor()`

---

### `AIBT.Burst.AibtSnapshotBindingAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32)`

---

### `AIBT.Burst.AibtValueFieldAttribute`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32)`

---

### `AIBT.Burst.AsyncOperationHandle`2`

_No public members declared directly on this type._

---

### `AIBT.Burst.BlackboardReadHandle`1`

_No public members declared directly on this type._

---

### `AIBT.Burst.BlackboardReadWriteHandle`1`

_No public members declared directly on this type._

---

### `AIBT.Burst.BlackboardWriteHandle`1`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstAbortContext`

- `METHOD AIBT.Burst.BurstContextResult TryBeginCancel(AIBT.Burst.AsyncOperationHandle`2<,>,AIBT.OperationId,AIBT.Burst.BurstValueWriter&)`

---

### `AIBT.Burst.BurstBlackboardAccess`

- `FIELD AIBT.Burst.BurstBlackboardAccess ReadWrite`
- `FIELD AIBT.Burst.BurstBlackboardAccess Read`
- `FIELD AIBT.Burst.BurstBlackboardAccess Write`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstCallbackPhase`

- `FIELD AIBT.Burst.BurstCallbackPhase Abort`
- `FIELD AIBT.Burst.BurstCallbackPhase Enter`
- `FIELD AIBT.Burst.BurstCallbackPhase Exit`
- `FIELD AIBT.Burst.BurstCallbackPhase Observer`
- `FIELD AIBT.Burst.BurstCallbackPhase Tick`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstCancellationMode`

- `FIELD AIBT.Burst.BurstCancellationMode AbortOnly`
- `FIELD AIBT.Burst.BurstCancellationMode Command`
- `FIELD AIBT.Burst.BurstCancellationMode NotApplicable`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstCatalogFingerprint`

- `METHOD System.Void .ctor(AIBT.Burst.BurstHash256)`
- `PROPERTY AIBT.Burst.BurstHash256 Value`

---

### `AIBT.Burst.BurstCatalogHandshake`

- `METHOD System.Void .ctor(System.UInt32,AIBT.Burst.BurstCatalogFingerprint,AIBT.Burst.BurstHash256,System.UInt32,System.UInt32,AIBT.Burst.BurstHash256,AIBT.Burst.BurstHash256,AIBT.Burst.BurstHash256)`
- `PROPERTY AIBT.Burst.BurstCatalogFingerprint Catalog`
- `PROPERTY AIBT.Burst.BurstHash256 AccessLayout`
- `PROPERTY AIBT.Burst.BurstHash256 ConfigurationLayout`
- `PROPERTY AIBT.Burst.BurstHash256 MemoryLayout`
- `PROPERTY AIBT.Burst.BurstHash256 NodeRegistry`
- `PROPERTY System.UInt32 AbiVersion`
- `PROPERTY System.UInt32 CompiledFormatVersion`
- `PROPERTY System.UInt32 ExecutionSemanticsVersion`

---

### `AIBT.Burst.BurstCatalogValidationCode`

- `FIELD AIBT.Burst.BurstCatalogValidationCode AbiVersionMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode AccessLayoutMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode CatalogMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode CompiledFormatMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode ConfigurationLayoutMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode MemoryLayoutMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode RegistryMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode SemanticsMismatch`
- `FIELD AIBT.Burst.BurstCatalogValidationCode Success`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstCatalogValidationResult`

- `METHOD System.Void .ctor(AIBT.Burst.BurstCatalogValidationCode,System.UInt16)`
- `PROPERTY AIBT.Burst.BurstCatalogValidationCode Code`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.UInt16 DiagnosticNumber`

---

### `AIBT.Burst.BurstCompletionOutcome`

- `FIELD AIBT.Burst.BurstCompletionOutcome Cancelled`
- `FIELD AIBT.Burst.BurstCompletionOutcome Failed`
- `FIELD AIBT.Burst.BurstCompletionOutcome Succeeded`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstConfigurationReader`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstContextResult`

- `FIELD AIBT.Burst.BurstContextResult AlreadyCommitted`
- `FIELD AIBT.Burst.BurstContextResult CapacityExceeded`
- `FIELD AIBT.Burst.BurstContextResult IncompleteValue`
- `FIELD AIBT.Burst.BurstContextResult InvalidEncoding`
- `FIELD AIBT.Burst.BurstContextResult InvalidHandle`
- `FIELD AIBT.Burst.BurstContextResult InvalidStatus`
- `FIELD AIBT.Burst.BurstContextResult Overflow`
- `FIELD AIBT.Burst.BurstContextResult PhaseViolation`
- `FIELD AIBT.Burst.BurstContextResult StaleCompletion`
- `FIELD AIBT.Burst.BurstContextResult Success`
- `FIELD AIBT.Burst.BurstContextResult TypeMismatch`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstDispatchFrame`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstEnterContext`

- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardRead(AIBT.Burst.BlackboardReadHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardRead(AIBT.Burst.BlackboardReadWriteHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardWrite(AIBT.Burst.BlackboardReadWriteHandle`1<>,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardWrite(AIBT.Burst.BlackboardWriteHandle`1<>,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginConsume(AIBT.Burst.CompletionHandle`1<>,AIBT.OperationId,AIBT.Burst.BurstCompletionOutcome&,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginEffect(AIBT.Burst.CommandHandle`1<>,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginSnapshotRead(AIBT.Burst.SnapshotReadHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginStart(AIBT.Burst.AsyncOperationHandle`2<,>,AIBT.Burst.BurstValueWriter&,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetTimeMicroseconds(System.Int64&)`
- `METHOD AIBT.Burst.BurstContextResult TryNextFloat32(System.Single&)`
- `METHOD AIBT.Burst.BurstContextResult TryNextUInt32(System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryNextUInt32(System.UInt32,System.UInt32&)`

---

### `AIBT.Burst.BurstExecutionBatch`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstExecutionCode`

- `FIELD AIBT.Burst.BurstExecutionCode Faulted`
- `FIELD AIBT.Burst.BurstExecutionCode Success`
- `FIELD AIBT.Burst.BurstExecutionCode ValidationFailed`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstExecutionResult`

- `METHOD System.Void .ctor(AIBT.Burst.BurstExecutionCode,System.UInt16,System.UInt32,System.UInt64)`
- `PROPERTY AIBT.Burst.BurstExecutionCode Code`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.UInt16 DiagnosticNumber`
- `PROPERTY System.UInt32 InstancesVisited`
- `PROPERTY System.UInt64 SegmentSteps`

---

### `AIBT.Burst.BurstExitContext`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstGeneratedRuntimeBridge`

- `METHOD AIBT.Burst.BurstContextResult TryAcquireDispatchFrame(AIBT.Burst.BurstExecutionBatch&,System.UInt32,System.UInt32,System.UInt32,AIBT.Burst.BurstCallbackPhase,AIBT.Burst.BurstDispatchFrame&)`
- `METHOD AIBT.Burst.BurstContextResult TryCommitBlackboardWrite(AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryCommitCancel(AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryCommitConsume(AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryCommitEffect(AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryCommitMemory(AIBT.Burst.BurstMemoryAccessor&)`
- `METHOD AIBT.Burst.BurstContextResult TryCommitStart(AIBT.Burst.BurstValueWriter&,AIBT.Burst.BurstValueWriter&,AIBT.OperationId&)`
- `METHOD AIBT.Burst.BurstContextResult TryCompleteAbort(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstDispatchFrame&)`
- `METHOD AIBT.Burst.BurstContextResult TryCompleteEnter(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstEnterContext&)`
- `METHOD AIBT.Burst.BurstContextResult TryCompleteExit(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstDispatchFrame&)`
- `METHOD AIBT.Burst.BurstContextResult TryCompleteObserver(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.ConditionResult)`
- `METHOD AIBT.Burst.BurstContextResult TryCompleteTick(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstTickContext&,AIBT.NodeStatus)`
- `METHOD AIBT.Burst.BurstContextResult TryCompleteValueRead(AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateAbortContext(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstAbortContext&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateConfigurationReader(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstConfigurationReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateEnterContext(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstEnterContext&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateExitContext(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstExitContext&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateMemoryAccessor(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstMemoryAccessor&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateObserverContext(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstObserverContext&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreateTickContext(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstTickContext&)`
- `METHOD AIBT.Burst.BurstContextResult TryFailDispatch(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstContextResult)`
- `METHOD AIBT.Burst.BurstContextResult TryGetAbortReason(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstNodeAbortReason&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetCatalogHandshake(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstCatalogHandshake&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetExecutionRequest(AIBT.Burst.BurstExecutionBatch&,System.UInt32&,System.UInt32&,System.UInt32&,AIBT.Burst.BurstCallbackPhase&,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetExecutionResult(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstExecutionResult&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetExitReason(AIBT.Burst.BurstDispatchFrame&,AIBT.Burst.BurstNodeExitReason&)`
- `METHOD AIBT.Burst.BurstContextResult TryPrepareSchedule(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstExecutionBatch&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadAsyncOperationHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadBlackboardReadHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadBlackboardReadWriteHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadBlackboardWriteHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadBoolean(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt32,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadCommandHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadCompletionHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryBoolean(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryFloat32(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Single&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryFloat64(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Double&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryInt16(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Int16&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryInt32(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Int32&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryInt64(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Int64&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryInt8(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.SByte&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryUInt16(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.UInt16&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryUInt32(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryUInt64(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadMemoryUInt8(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Byte&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadSnapshotHandle(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt64,System.UInt32,)`
- `METHOD AIBT.Burst.BurstContextResult TryReadUInt32(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt32,System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadUInt64(AIBT.Burst.BurstConfigurationReader&,System.UInt32,System.UInt32,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Byte&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Double&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Int16&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Int32&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Int64&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.SByte&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.Single&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.UInt16&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryReadValue(AIBT.Burst.BurstValueReader&,System.UInt32,System.UInt32,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryRejectBatch(AIBT.Burst.BurstExecutionBatch&,AIBT.Burst.BurstCatalogValidationResult&)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryBoolean(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Boolean)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryFloat32(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Single)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryFloat64(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Double)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryInt16(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Int16)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryInt32(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Int32)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryInt64(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Int64)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryInt8(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.SByte)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryUInt16(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.UInt16)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryUInt32(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.UInt32)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryUInt64(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.UInt64)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteMemoryUInt8(AIBT.Burst.BurstMemoryAccessor&,System.UInt32,System.UInt32,System.Byte)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Boolean)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Byte)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Double)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Int16)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Int32)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Int64)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.SByte)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.Single)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.UInt16)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.UInt32)`
- `METHOD AIBT.Burst.BurstContextResult TryWriteValue(AIBT.Burst.BurstValueWriter&,System.UInt32,System.UInt32,System.UInt64)`

---

### `AIBT.Burst.BurstHash256`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 Word0`
- `PROPERTY System.UInt32 Word1`
- `PROPERTY System.UInt32 Word2`
- `PROPERTY System.UInt32 Word3`
- `PROPERTY System.UInt32 Word4`
- `PROPERTY System.UInt32 Word5`
- `PROPERTY System.UInt32 Word6`
- `PROPERTY System.UInt32 Word7`

---

### `AIBT.Burst.BurstMemoryAccessor`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstNodeAbortReason`

- `FIELD AIBT.Burst.BurstNodeAbortReason Explicit`
- `FIELD AIBT.Burst.BurstNodeAbortReason HotReload`
- `FIELD AIBT.Burst.BurstNodeAbortReason ObserverLowerPriority`
- `FIELD AIBT.Burst.BurstNodeAbortReason ObserverSelf`
- `FIELD AIBT.Burst.BurstNodeAbortReason Timeout`
- `FIELD AIBT.Burst.BurstNodeAbortReason TreeStopped`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstNodeCost`

- `FIELD AIBT.Burst.BurstNodeCost High`
- `FIELD AIBT.Burst.BurstNodeCost Low`
- `FIELD AIBT.Burst.BurstNodeCost Medium`
- `FIELD AIBT.Burst.BurstNodeCost Trivial`
- `FIELD AIBT.Burst.BurstNodeCost Variable`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstNodeExitReason`

- `FIELD AIBT.Burst.BurstNodeExitReason Aborted`
- `FIELD AIBT.Burst.BurstNodeExitReason Failure`
- `FIELD AIBT.Burst.BurstNodeExitReason Success`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstNodeKind`

- `FIELD AIBT.Burst.BurstNodeKind Action`
- `FIELD AIBT.Burst.BurstNodeKind Condition`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstNodeStatusMask`

- `FIELD AIBT.Burst.BurstNodeStatusMask Failure`
- `FIELD AIBT.Burst.BurstNodeStatusMask None`
- `FIELD AIBT.Burst.BurstNodeStatusMask Running`
- `FIELD AIBT.Burst.BurstNodeStatusMask Success`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.BurstObserverContext`

- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardRead(AIBT.Burst.BlackboardReadHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardRead(AIBT.Burst.BlackboardReadWriteHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginSnapshotRead(AIBT.Burst.SnapshotReadHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetTimeMicroseconds(System.Int64&)`

---

### `AIBT.Burst.BurstTickContext`

- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardRead(AIBT.Burst.BlackboardReadHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardRead(AIBT.Burst.BlackboardReadWriteHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardWrite(AIBT.Burst.BlackboardReadWriteHandle`1<>,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginBlackboardWrite(AIBT.Burst.BlackboardWriteHandle`1<>,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginConsume(AIBT.Burst.CompletionHandle`1<>,AIBT.OperationId,AIBT.Burst.BurstCompletionOutcome&,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginEffect(AIBT.Burst.CommandHandle`1<>,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginSnapshotRead(AIBT.Burst.SnapshotReadHandle`1<>,AIBT.Burst.BurstValueReader&)`
- `METHOD AIBT.Burst.BurstContextResult TryBeginStart(AIBT.Burst.AsyncOperationHandle`2<,>,AIBT.Burst.BurstValueWriter&,AIBT.Burst.BurstValueWriter&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetTimeMicroseconds(System.Int64&)`
- `METHOD AIBT.Burst.BurstContextResult TryNextFloat32(System.Single&)`
- `METHOD AIBT.Burst.BurstContextResult TryNextUInt32(System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryNextUInt32(System.UInt32,System.UInt32&)`

---

### `AIBT.Burst.BurstValueReader`

_No public members declared directly on this type._

---

### `AIBT.Burst.BurstValueWriter`

_No public members declared directly on this type._

---

### `AIBT.Burst.CommandHandle`1`

_No public members declared directly on this type._

---

### `AIBT.Burst.CompletionHandle`1`

_No public members declared directly on this type._

---

### `AIBT.Burst.ConditionResult`

- `FIELD AIBT.Burst.ConditionResult Failure`
- `FIELD AIBT.Burst.ConditionResult Success`
- `FIELD System.Byte value__`

---

### `AIBT.Burst.SnapshotReadHandle`1`

_No public members declared directly on this type._

---

### `AIBT.CommandBatch`

- `METHOD System.Byte GetPayloadByte(System.Int32)`
- `METHOD System.ReadOnlySpan`1<System.Byte> GetPayload(AIBT.CommandRecord&)`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.CommandRecord>,System.Collections.Generic.IEnumerable`1<System.Byte>)`
- `PROPERTY AIBT.CommandBatch Empty`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CommandRecord> Records`
- `PROPERTY System.Int32 PayloadSize`

---

### `AIBT.CommandBatchMerger`

- `METHOD AIBT.CommandBatch Merge(System.Collections.Generic.IEnumerable`1<AIBT.CommandBatch>)`

---

### `AIBT.CommandPhase`

- `FIELD AIBT.CommandPhase Cancel`
- `FIELD AIBT.CommandPhase Execute`
- `FIELD System.Byte value__`

---

### `AIBT.CommandRecord`

- `METHOD System.Boolean Equals(AIBT.CommandRecord)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.CommandType,AIBT.OperationId,System.UInt32,System.UInt32,AIBT.CommandPhase,AIBT.TreeInstanceId,System.UInt64)`
- `PROPERTY AIBT.CommandPhase Phase`
- `PROPERTY AIBT.CommandType CommandType`
- `PROPERTY AIBT.OperationId OperationId`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.UInt32 PayloadOffset`
- `PROPERTY System.UInt32 PayloadSize`
- `PROPERTY System.UInt64 Sequence`

---

### `AIBT.CommandType`

- `METHOD System.Boolean Equals(AIBT.CommandType)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt64,System.UInt32)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Version`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.CompiledBlackboardAccessFlags`

- `FIELD AIBT.CompiledBlackboardAccessFlags None`
- `FIELD AIBT.CompiledBlackboardAccessFlags Observed`
- `FIELD AIBT.CompiledBlackboardAccessFlags Read`
- `FIELD AIBT.CompiledBlackboardAccessFlags Write`
- `FIELD System.Byte value__`

---

### `AIBT.CompiledBlackboardSlotRecord`

- `METHOD System.Void .ctor(System.UInt64,System.UInt64,System.UInt32,System.UInt64,AIBT.BlackboardScope,System.UInt32,System.UInt32,System.UInt32,System.UInt32,AIBT.CompiledBlackboardAccessFlags)`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.CompiledBlackboardAccessFlags AccessFlags`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 DefaultValueOffset`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 StableKeyId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.CompiledCompilerVersion`

- `METHOD System.Boolean Equals(AIBT.CompiledCompilerVersion)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt16,System.UInt16,System.UInt16,System.UInt32)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt16 Major`
- `PROPERTY System.UInt16 Minor`
- `PROPERTY System.UInt16 Patch`
- `PROPERTY System.UInt32 BuildRevision`

---

### `AIBT.CompiledDebugMapEntry`

- `METHOD System.Void .ctor(System.UInt32,AIBT.NodeId,System.String,System.String)`
- `PROPERTY AIBT.NodeId AuthoringNodeId`
- `PROPERTY System.String DisplayName`
- `PROPERTY System.String SourcePath`
- `PROPERTY System.UInt32 RuntimeNodeIndex`

---

### `AIBT.CompiledHash`

- `FIELD System.Int32 HexLength`
- `METHOD System.Boolean Equals(AIBT.CompiledHash)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.String)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.String HexadecimalValue`

---

### `AIBT.CompiledIndex`

- `FIELD System.UInt32 Invalid`

---

### `AIBT.CompiledNodeFlags`

- `FIELD AIBT.CompiledNodeFlags BurstDomain`
- `FIELD AIBT.CompiledNodeFlags MainThreadDomain`
- `FIELD AIBT.CompiledNodeFlags ManagedDomain`
- `FIELD AIBT.CompiledNodeFlags None`
- `FIELD AIBT.CompiledNodeFlags SupportsTracing`
- `FIELD System.UInt32 value__`

---

### `AIBT.CompiledNodeRecord`

- `METHOD System.Void .ctor(System.UInt64,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,AIBT.NodeMemoryLifetime,AIBT.CompiledRange,AIBT.CompiledNodeFlags,System.UInt32,AIBT.CompiledRange,AIBT.CompiledRange)`
- `PROPERTY AIBT.CompiledNodeFlags Flags`
- `PROPERTY AIBT.CompiledRange Children`
- `PROPERTY AIBT.CompiledRange ReadSlots`
- `PROPERTY AIBT.CompiledRange WriteSlots`
- `PROPERTY AIBT.NodeMemoryLifetime MemoryLifetime`
- `PROPERTY System.UInt32 ConfigAlignment`
- `PROPERTY System.UInt32 ConfigOffset`
- `PROPERTY System.UInt32 ConfigSize`
- `PROPERTY System.UInt32 DebugIdentityIndex`
- `PROPERTY System.UInt32 InstanceMemoryAlignment`
- `PROPERTY System.UInt32 InstanceMemoryOffset`
- `PROPERTY System.UInt32 InstanceMemorySize`
- `PROPERTY System.UInt32 NodeTypeVersion`
- `PROPERTY System.UInt64 NodeTypeId`

---

### `AIBT.CompiledObserverMode`

- `FIELD AIBT.CompiledObserverMode Both`
- `FIELD AIBT.CompiledObserverMode LowerPriority`
- `FIELD AIBT.CompiledObserverMode Self`
- `FIELD System.Byte value__`

---

### `AIBT.CompiledObserverRecord`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32,AIBT.CompiledObserverMode,AIBT.CompiledRange)`
- `PROPERTY AIBT.CompiledObserverMode Mode`
- `PROPERTY AIBT.CompiledRange WatchedSlots`
- `PROPERTY System.UInt32 ObserverNodeIndex`
- `PROPERTY System.UInt32 OwningReactiveCompositeIndex`

---

### `AIBT.CompiledProgram`

- `METHOD System.Void .ctor(AIBT.CompiledProgramHeader,System.Collections.Generic.IEnumerable`1<AIBT.CompiledNodeRecord>,System.Collections.Generic.IEnumerable`1<System.UInt32>,System.Collections.Generic.IEnumerable`1<System.UInt32>,System.Collections.Generic.IEnumerable`1<System.UInt32>,System.Collections.Generic.IEnumerable`1<AIBT.CompiledBlackboardSlotRecord>,System.Collections.Generic.IEnumerable`1<AIBT.CompiledObserverRecord>,System.Collections.Generic.IEnumerable`1<System.UInt32>,System.Collections.Generic.IEnumerable`1<System.Byte>,System.Collections.Generic.IEnumerable`1<System.Byte>,System.Collections.Generic.IEnumerable`1<AIBT.CompiledDebugMapEntry>)`
- `PROPERTY AIBT.CompiledProgramHeader Header`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CompiledBlackboardSlotRecord> BlackboardSlots`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CompiledDebugMapEntry> DebugMap`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CompiledNodeRecord> Nodes`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CompiledObserverRecord> Observers`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.Byte> ConfigBlob`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.Byte> DefaultValueBlob`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> ChildIndices`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> ReadSlotIndices`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> WatchedSlotIndices`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> WriteSlotIndices`

---

### `AIBT.CompiledProgramHeader`

- `FIELD System.UInt32 ExpectedMagic`
- `METHOD System.Void .ctor(System.UInt32,System.UInt32,AIBT.CompiledCompilerVersion,AIBT.CompiledHash,AIBT.CompiledHash,AIBT.CompiledHash,System.UInt32,AIBT.CompiledHash,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.Boolean)`
- `PROPERTY AIBT.CompiledCompilerVersion CompilerVersion`
- `PROPERTY AIBT.CompiledHash CanonicalPolicyHash`
- `PROPERTY AIBT.CompiledHash CanonicalSemanticHash`
- `PROPERTY AIBT.CompiledHash CompiledContentHash`
- `PROPERTY AIBT.CompiledHash NodeRegistryHash`
- `PROPERTY System.Boolean DeterministicModeCompatible`
- `PROPERTY System.UInt32 BlackboardSlotCount`
- `PROPERTY System.UInt32 CapabilityFlags`
- `PROPERTY System.UInt32 ChildIndexCount`
- `PROPERTY System.UInt32 CompiledFormatVersion`
- `PROPERTY System.UInt32 ConfigBlobSize`
- `PROPERTY System.UInt32 DebugMapCount`
- `PROPERTY System.UInt32 ExecutionSemanticsVersion`
- `PROPERTY System.UInt32 InstanceNodeMemorySize`
- `PROPERTY System.UInt32 Magic`
- `PROPERTY System.UInt32 NodeCount`
- `PROPERTY System.UInt32 PolicyFormatVersion`
- `PROPERTY System.UInt32 RequiredMaximumAlignment`
- `PROPERTY System.UInt32 RootNodeIndex`

---

### `AIBT.CompiledRange`

- `METHOD System.Boolean Equals(AIBT.CompiledRange)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt32,System.UInt32)`
- `PROPERTY System.Boolean IsEmpty`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt64 EndExclusive`

---

### `AIBT.CompletionBatch`

- `METHOD System.Byte GetPayloadByte(System.Int32)`
- `METHOD System.ReadOnlySpan`1<System.Byte> GetPayload(AIBT.CompletionRecord&)`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.CompletionRecord>,System.Collections.Generic.IEnumerable`1<System.Byte>)`
- `PROPERTY AIBT.CompletionBatch Empty`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CompletionRecord> Records`
- `PROPERTY System.Int32 PayloadSize`

---

### `AIBT.CompletionOutcome`

- `FIELD AIBT.CompletionOutcome Cancelled`
- `FIELD AIBT.CompletionOutcome Failed`
- `FIELD AIBT.CompletionOutcome Succeeded`
- `FIELD System.Byte value__`

---

### `AIBT.CompletionPayloadType`

- `METHOD System.Boolean Equals(AIBT.CompletionPayloadType)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt64,System.UInt32)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Version`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.CompletionRecord`

- `METHOD System.Boolean Equals(AIBT.CompletionRecord)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.OperationId,AIBT.CompletionOutcome,AIBT.CompletionPayloadType,System.UInt32,System.UInt32,System.UInt64,System.UInt64,AIBT.Revision)`
- `PROPERTY AIBT.CompletionOutcome Outcome`
- `PROPERTY AIBT.CompletionPayloadType PayloadType`
- `PROPERTY AIBT.OperationId OperationId`
- `PROPERTY AIBT.Revision SnapshotRevision`
- `PROPERTY System.UInt32 PayloadOffset`
- `PROPERTY System.UInt32 PayloadSize`
- `PROPERTY System.UInt64 SourceId`
- `PROPERTY System.UInt64 SourceSequence`

---

### `AIBT.Diagnostic`

- `METHOD System.Boolean Equals(AIBT.Diagnostic)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 CompareTo(AIBT.Diagnostic)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.DiagnosticCode,AIBT.DiagnosticSeverity,System.String,AIBT.DiagnosticLocation,System.Collections.Generic.IEnumerable`1<AIBT.DiagnosticLocation>)`
- `PROPERTY AIBT.DiagnosticCode Code`
- `PROPERTY AIBT.DiagnosticLocation Location`
- `PROPERTY AIBT.DiagnosticSeverity Severity`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.DiagnosticLocation> RelatedLocations`
- `PROPERTY System.String Message`

---

### `AIBT.DiagnosticCatalog`

- `METHOD AIBT.Diagnostic Create(AIBT.DiagnosticCode,System.String,AIBT.DiagnosticLocation,System.Collections.Generic.IEnumerable`1<AIBT.DiagnosticLocation>)`
- `METHOD System.Boolean TryGet(AIBT.DiagnosticCode,AIBT.DiagnosticDescriptor&)`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.DiagnosticDescriptor>)`
- `METHOD System.Void Validate(AIBT.Diagnostic,System.Boolean)`
- `PROPERTY System.Int32 Count`

---

### `AIBT.DiagnosticCode`

- `METHOD AIBT.DiagnosticCode Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.DiagnosticCode)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.DiagnosticCode&)`
- `METHOD System.Int32 CompareTo(AIBT.DiagnosticCode)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.String)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.String Value`

---

### `AIBT.DiagnosticCodeRange`

- `METHOD System.Boolean Contains(AIBT.DiagnosticCode)`
- `PROPERTY System.Int32 First`
- `PROPERTY System.Int32 Last`

---

### `AIBT.DiagnosticCodeRanges`

- `METHOD AIBT.DiagnosticCodeRange For(AIBT.DiagnosticSubsystem)`

---

### `AIBT.DiagnosticCollection`

- `METHOD System.Collections.Generic.IEnumerator`1<AIBT.Diagnostic> GetEnumerator()`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.Diagnostic>)`
- `PROPERTY AIBT.Diagnostic Item`
- `PROPERTY AIBT.DiagnosticCollection Empty`
- `PROPERTY System.Int32 Count`

---

### `AIBT.DiagnosticDescriptor`

- `METHOD System.Void .ctor(AIBT.DiagnosticCode,AIBT.DiagnosticSubsystem,AIBT.DiagnosticSeverity,AIBT.DiagnosticField,AIBT.DiagnosticField)`
- `PROPERTY AIBT.DiagnosticCode Code`
- `PROPERTY AIBT.DiagnosticField OptionalFields`
- `PROPERTY AIBT.DiagnosticField RequiredFields`
- `PROPERTY AIBT.DiagnosticSeverity DefaultSeverity`
- `PROPERTY AIBT.DiagnosticSubsystem Subsystem`

---

### `AIBT.DiagnosticField`

- `FIELD AIBT.DiagnosticField DocumentId`
- `FIELD AIBT.DiagnosticField JsonPointer`
- `FIELD AIBT.DiagnosticField LineAndColumn`
- `FIELD AIBT.DiagnosticField NodeId`
- `FIELD AIBT.DiagnosticField None`
- `FIELD AIBT.DiagnosticField RelatedLocations`
- `FIELD AIBT.DiagnosticField SuggestedOperation`
- `FIELD AIBT.DiagnosticField TreeId`
- `FIELD AIBT.DiagnosticField TreeInstanceId`
- `FIELD System.Int32 value__`

---

### `AIBT.DiagnosticLocation`

- `METHOD System.Boolean Equals(AIBT.DiagnosticLocation)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 CompareTo(AIBT.DiagnosticLocation)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.String,System.String,System.Nullable`1<System.Int32>,System.Nullable`1<System.Int32>,AIBT.TreeId,AIBT.NodeId,AIBT.TreeInstanceId)`
- `PROPERTY AIBT.NodeId NodeId`
- `PROPERTY AIBT.TreeId TreeId`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean HasDocumentId`
- `PROPERTY System.Boolean HasJsonPointer`
- `PROPERTY System.Boolean IsKnown`
- `PROPERTY System.Nullable`1<System.Int32> Column`
- `PROPERTY System.Nullable`1<System.Int32> Line`
- `PROPERTY System.String DocumentId`
- `PROPERTY System.String JsonPointer`

---

### `AIBT.DiagnosticSeverity`

- `FIELD AIBT.DiagnosticSeverity Error`
- `FIELD AIBT.DiagnosticSeverity Info`
- `FIELD AIBT.DiagnosticSeverity Warning`
- `FIELD System.Byte value__`

---

### `AIBT.DiagnosticSubsystem`

- `FIELD AIBT.DiagnosticSubsystem CoreRuntime`
- `FIELD AIBT.DiagnosticSubsystem Execution`
- `FIELD AIBT.DiagnosticSubsystem RegistryAndCompiler`
- `FIELD AIBT.DiagnosticSubsystem SemanticValidation`
- `FIELD AIBT.DiagnosticSubsystem SyntaxAndSerialization`
- `FIELD AIBT.DiagnosticSubsystem ToolingAndTestInput`
- `FIELD System.Int32 value__`

---

### `AIBT.EntityId`

- `METHOD AIBT.EntityId Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.EntityId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.EntityId&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.UInt64)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt64 Value`

---

### `AIBT.Enum32Value`

- `METHOD System.Boolean Equals(AIBT.Enum32Value)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt64,System.Int32)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.Int32 Value`
- `PROPERTY System.UInt64 ContractTypeId`

---

### `AIBT.Float2Value`

- `METHOD System.Boolean Equals(AIBT.Float2Value)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Single,System.Single)`
- `PROPERTY System.Single X`
- `PROPERTY System.Single Y`

---

### `AIBT.Float3Value`

- `METHOD System.Boolean Equals(AIBT.Float3Value)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Single,System.Single,System.Single)`
- `PROPERTY System.Single X`
- `PROPERTY System.Single Y`
- `PROPERTY System.Single Z`

---

### `AIBT.HotReloadClassificationResult`

The full result of classifying one compiled-program transition, per <c>ADR-P5-001</c>.

- `PROPERTY System.Boolean RequiresFullRestart`
- `PROPERTY System.Collections.Generic.IReadOnlyCollection`1<AIBT.NodeId> RestartSubtreeRootNodeIds`
- `PROPERTY System.Collections.Generic.IReadOnlyCollection`1<AIBT.NodeId> StructuralChildChangeNodeIds`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<AIBT.NodeId,AIBT.HotReloadNodeVerdict> NodeVerdicts`

---

### `AIBT.HotReloadCompatibilityClassifier`

Implements <c>Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md</c>'s per-node classification and subtree-localization rules over two compiled programs. Pure and side-effect-free: it never touches a live tree instance and performs no restart or migration itself (that is <c>P5-004</c>/<c>P5-005</c>/<c>P5-006</c>'s job).

- `METHOD AIBT.HotReloadClassificationResult Classify(AIBT.CompiledProgram,AIBT.CompiledProgram)`

---

### `AIBT.HotReloadNodeIdentitySignature`

The per-node facts <c>Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md</c> classifies a node's hot-reload compatibility from: its compiled node type identity and the instance-memory layout that identity implies. Computed directly from an existing <see cref="CompiledNodeRecord"/> -- no new field is added to the accepted <c>compiled-program-v1.md</c> format.

- `METHOD System.Boolean HasCompatibleLayout(AIBT.HotReloadNodeIdentitySignature)`
- `METHOD System.Boolean HasSameTypeAndVersion(AIBT.HotReloadNodeIdentitySignature)`
- `PROPERTY AIBT.NodeMemoryLifetime Lifetime`
- `PROPERTY System.UInt32 InstanceMemoryAlignment`
- `PROPERTY System.UInt32 InstanceMemorySize`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.HotReloadNodeVerdict`

One node's classification result, with an inspectable reason -- the same explainability discipline <c>execution-and-scheduling.md</c> requires of scheduler decisions applies to reload decisions (<c>Documentation~/hot-reload.md</c>'s "Editor workflows" section).

- `METHOD System.Void .ctor(AIBT.HotReloadNodeVerdictCategory,System.String)`
- `PROPERTY AIBT.HotReloadNodeVerdictCategory Category`
- `PROPERTY System.String Reason`

---

### `AIBT.HotReloadNodeVerdictCategory`

Per-node hot-reload classification, per <c>Documentation~/decisions/ADR-P5-001-hot-reload-compatibility-model.md</c>.

- `FIELD AIBT.HotReloadNodeVerdictCategory Dropped`
- `FIELD AIBT.HotReloadNodeVerdictCategory IncompatibleRestart`
- `FIELD AIBT.HotReloadNodeVerdictCategory Migrate`
- `FIELD AIBT.HotReloadNodeVerdictCategory New`
- `FIELD System.Int32 value__`

---

### `AIBT.HotReloadProgramIdentityMap`

Maps every node in a <see cref="CompiledProgram"/> to its stable authoring <see cref="NodeId"/> (via <see cref="CompiledProgram.DebugMap"/>), its <see cref="HotReloadNodeIdentitySignature"/>, and its current compiled index -- the data <c>ADR-P5-001</c>'s hot-reload compatibility model classifies from. Immutable once built; safe to keep for the lifetime of the <see cref="CompiledProgram"/> it was built from. Never mutated to track a live instance's own state -- this type is a read-only view of one compiled program, nothing more.

- `METHOD AIBT.HotReloadProgramIdentityMap Build(AIBT.CompiledProgram)`
- `METHOD System.Boolean TryGetRuntimeIndex(AIBT.NodeId,System.UInt32&)`
- `METHOD System.Boolean TryGetSignature(AIBT.NodeId,AIBT.HotReloadNodeIdentitySignature&)`
- `PROPERTY System.Collections.Generic.IReadOnlyCollection`1<AIBT.NodeId> NodeIds`

---

### `AIBT.IReferenceLeafBehavior`

- `METHOD AIBT.NodeStatus Tick(AIBT.ReferenceLeafContext&)`
- `METHOD System.Void Abort(AIBT.ReferenceLeafContext&,AIBT.NodeAbortReason)`
- `METHOD System.Void Enter(AIBT.ReferenceLeafContext&)`
- `METHOD System.Void Exit(AIBT.ReferenceLeafContext&,AIBT.NodeExitReason)`

---

### `AIBT.NativeAgentBindingV1`

- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 BindingId`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeAgentBlackboardV1`

- `METHOD AIBT.Burst.BurstContextResult TryRead(AIBT.NativeAgentExecuteLeaseV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryRead(AIBT.NativeAgentExecuteLeaseV2,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryRead(AIBT.NativeAgentExecutionViewV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryWrite(AIBT.NativeAgentExecuteLeaseV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,Unity.Collections.NativeArray`1<>,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryWrite(AIBT.NativeAgentExecuteLeaseV2,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,Unity.Collections.NativeArray`1<>,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryWrite(AIBT.NativeAgentExecutionViewV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,Unity.Collections.NativeArray`1<>,System.Boolean&)`

---

### `AIBT.NativeAgentContextCapacityV1`

- `METHOD System.Boolean TryDerive(AIBT.NativeProgramImageViewV2,System.UInt32,AIBT.NativeAgentContextCapacityV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 MaximumBindings`
- `PROPERTY System.UInt32 SlotVersions`
- `PROPERTY System.UInt32 ValueBytes`

---

### `AIBT.NativeAgentContextOwnerV1`

- `METHOD System.Boolean TryAbortExecuteWindow(AIBT.NativeAgentExecuteWindowV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAbortExecuteWindow(AIBT.NativeAgentExecuteWindowV2,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireNext(AIBT.NativeAgentExecuteWindowV1,AIBT.NativeAgentExecuteLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireNext(AIBT.NativeAgentExecuteWindowV2,AIBT.NativeAgentExecuteLeaseV2&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryBeginExecuteWindow(AIBT.NativeExecuteSelectionWindowOwnerV1,AIBT.NativeExecuteSelectionWindowV1,AIBT.NativeAgentExecuteWindowV2&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryBeginExecuteWindow(Unity.Collections.NativeArray`1<AIBT.TreeInstanceId>,AIBT.NativeAgentExecuteWindowV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryBind(AIBT.TreeInstanceId,AIBT.NativeProgramReadLeaseV2,AIBT.NativeInstanceArenaOwnerV1,AIBT.NativeAgentBindingV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCancelNext(AIBT.NativeAgentExecuteWindowV1,AIBT.TreeInstanceId,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCancelNext(AIBT.NativeAgentExecuteWindowV2,AIBT.TreeInstanceId,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryEndExecuteWindow(AIBT.NativeAgentExecuteWindowV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryEndExecuteWindow(AIBT.NativeAgentExecuteWindowV2,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeAgentExecuteLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeAgentExecuteLeaseV2,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseExecuteLease(AIBT.NativeAgentExecuteLeaseV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseExecuteLease(AIBT.NativeAgentExecuteLeaseV2,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReset(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryUnbind(AIBT.NativeAgentBindingV1,AIBT.NativeRuntimeFailureV1&)`
- `PROPERTY AIBT.AgentId AgentId`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeAgentContextRegistryV1`

- `METHOD System.Boolean TryCreate(System.UInt32,Unity.Collections.Allocator,AIBT.NativeAgentContextRegistryV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreateContext(AIBT.AgentId,AIBT.NativeProgramReadLeaseV2,AIBT.NativeAgentContextCapacityV1,AIBT.NativeAgentContextOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDestroyContext(AIBT.NativeAgentContextOwnerV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeRuntimeFailureV1&)`

---

### `AIBT.NativeAgentContextViewV1`

- `PROPERTY Unity.Collections.NativeArray`1<System.Byte> Values`
- `PROPERTY Unity.Collections.NativeArray`1<System.UInt64> Revision`
- `PROPERTY Unity.Collections.NativeArray`1<System.UInt64> SlotVersions`

---

### `AIBT.NativeAgentExecuteLeaseV1`

- `PROPERTY AIBT.NativeAgentContextViewV1 Context`
- `PROPERTY AIBT.NativeAgentExecuteWindowV1 Window`
- `PROPERTY AIBT.NativeAgentExecutionViewV1 View`
- `PROPERTY AIBT.NativeInstanceExecutionLeaseV2 TreeLease`
- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeAgentExecuteLeaseV2`

- `PROPERTY AIBT.NativeAgentContextViewV1 Context`
- `PROPERTY AIBT.NativeAgentExecuteWindowV2 Window`
- `PROPERTY AIBT.NativeAgentExecutionViewV1 View`
- `PROPERTY AIBT.NativeInstanceExecutionLeaseV2 TreeLease`
- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeAgentExecuteWindowV1`

- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 WindowId`

---

### `AIBT.NativeAgentExecuteWindowV2`

- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt32 SelectionGeneration`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 SelectionOwnerId`
- `PROPERTY System.UInt64 SelectionWindowId`
- `PROPERTY System.UInt64 WindowId`

---

### `AIBT.NativeAgentExecutionViewV1`

- `PROPERTY AIBT.NativeAgentContextViewV1 Context`
- `PROPERTY AIBT.NativeProgramImageViewV2 Program`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeBlackboardAccessBindingV2`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32,AIBT.BlackboardScope,System.UInt32,AIBT.NativeBlackboardAccessModeV2,System.UInt64,System.UInt32,System.UInt64,System.UInt32,AIBT.NativeBlackboardReductionKindV2)`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.NativeBlackboardAccessModeV2 Mode`
- `PROPERTY AIBT.NativeBlackboardReductionKindV2 Reduction`
- `PROPERTY System.UInt32 AccessOrdinal`
- `PROPERTY System.UInt32 NodeIndex`
- `PROPERTY System.UInt32 RegisteredTypeIndex`
- `PROPERTY System.UInt32 SlotIndex`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeBlackboardAccessModeV2`

- `FIELD AIBT.NativeBlackboardAccessModeV2 ReadWrite`
- `FIELD AIBT.NativeBlackboardAccessModeV2 Read`
- `FIELD AIBT.NativeBlackboardAccessModeV2 Write`
- `FIELD System.Byte value__`

---

### `AIBT.NativeBlackboardAccessRecordV2`

- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.NativeBlackboardAccessModeV2 Mode`
- `PROPERTY AIBT.NativeBlackboardReductionKindV2 Reduction`
- `PROPERTY System.UInt32 AccessOrdinal`
- `PROPERTY System.UInt32 NodeIndex`
- `PROPERTY System.UInt32 RegisteredTypeIndex`
- `PROPERTY System.UInt32 ResolvedSlotIndex`
- `PROPERTY System.UInt32 ScopeSlotIndex`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeBlackboardFieldEncodingV2`

- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Bool8`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 FixedBytes`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Float32BitsLE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Float64BitsLE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 GeneratedHandle`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Int16LE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Int32LE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Int64LE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Int8`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 Registered`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 UInt16LE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 UInt32LE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 UInt64LE`
- `FIELD AIBT.NativeBlackboardFieldEncodingV2 UInt8`
- `FIELD System.Byte value__`

---

### `AIBT.NativeBlackboardReductionKindV2`

- `FIELD AIBT.NativeBlackboardReductionKindV2 All`
- `FIELD AIBT.NativeBlackboardReductionKindV2 Any`
- `FIELD AIBT.NativeBlackboardReductionKindV2 First`
- `FIELD AIBT.NativeBlackboardReductionKindV2 Last`
- `FIELD AIBT.NativeBlackboardReductionKindV2 Max`
- `FIELD AIBT.NativeBlackboardReductionKindV2 Min`
- `FIELD AIBT.NativeBlackboardReductionKindV2 None`
- `FIELD AIBT.NativeBlackboardReductionKindV2 Sum`
- `FIELD System.Byte value__`

---

### `AIBT.NativeBlackboardScopeBindingV2`

- `METHOD System.Byte[] GetRawLayoutCopy()`
- `METHOD System.Byte[] GetSchemaBytesCopy()`
- `METHOD System.Void .ctor(AIBT.BlackboardScope,System.String,System.UInt32,System.UInt32,System.UInt32,System.Byte[],System.Byte[])`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.CompiledHash LayoutHash`
- `PROPERTY AIBT.CompiledHash SchemaHash`
- `PROPERTY System.String ContractId`
- `PROPERTY System.UInt32 ContractVersion`
- `PROPERTY System.UInt32 FirstSlot`
- `PROPERTY System.UInt32 SlotCount`
- `PROPERTY System.UInt64 ContractNumericId`

---

### `AIBT.NativeBlackboardScopeRecordV2`

- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.NativeHash256V1 LayoutHash`
- `PROPERTY AIBT.NativeHash256V1 SchemaHash`
- `PROPERTY System.UInt32 ContractVersion`
- `PROPERTY System.UInt32 FirstSlot`
- `PROPERTY System.UInt32 RawLayoutLength`
- `PROPERTY System.UInt32 RawLayoutOffset`
- `PROPERTY System.UInt32 SlotCount`
- `PROPERTY System.UInt64 ContractId`

---

### `AIBT.NativeBlackboardSlotAuthorityV2`

- `METHOD AIBT.NativeBlackboardSlotAuthorityV2 CreateBuiltIn(System.String,AIBT.BlackboardTypeDescriptor,System.String,System.Byte[])`
- `METHOD System.Byte[] GetCanonicalDefaultJsonCopy()`
- `METHOD System.Void .ctor(System.String,System.String,System.String,System.Byte[])`
- `PROPERTY System.String CanonicalKeyId`
- `PROPERTY System.String CanonicalTypeId`
- `PROPERTY System.String EnumContract`

---

### `AIBT.NativeBlackboardSlotBindingV2`

- `METHOD System.Void .ctor(System.UInt64,System.UInt64,System.UInt32,System.UInt64,AIBT.BlackboardScope,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,AIBT.CompiledBlackboardAccessFlags,System.UInt32,System.UInt32,AIBT.NativeBlackboardReductionKindV2)`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.CompiledBlackboardAccessFlags AccessFlags`
- `PROPERTY AIBT.NativeBlackboardReductionKindV2 Reduction`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 DefaultOffset`
- `PROPERTY System.UInt32 DefaultSize`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 RegisteredTypeIndex`
- `PROPERTY System.UInt32 ScopeDescriptorIndex`
- `PROPERTY System.UInt32 ScopeSlotIndex`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 StableKeyId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeBlackboardTypeIdV2`

- `METHOD AIBT.NativeBlackboardTypeIdV2 BuiltIn(AIBT.BlackboardTypeDescriptor,System.UInt64)`
- `METHOD AIBT.NativeBlackboardTypeIdV2 Registered(System.UInt32,AIBT.NativeRegisteredBlackboardTypeRecordV2)`
- `METHOD System.Boolean Equals(AIBT.NativeBlackboardTypeIdV2)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY AIBT.NativeHash256V1 SchemaHash`
- `PROPERTY System.Boolean IsRegistered`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 RegisteredTypeIndex`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 Version`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 EqualityContractId`
- `PROPERTY System.UInt64 SchemaId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeBlackboardWatchedSlotBindingV2`

- `METHOD System.Void .ctor(AIBT.BlackboardScope,System.UInt32)`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY System.UInt32 SlotIndex`

---

### `AIBT.NativeBudgetStateV1`

- `FIELD System.Byte Exhausted`
- `FIELD System.UInt32 ResumeCursor`
- `FIELD System.UInt32 StepLimit`
- `FIELD System.UInt32 StepsConsumed`

---

### `AIBT.NativeCommandAsyncCapacityV1`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 CancelCommandRecords`
- `PROPERTY System.UInt32 CommandPayloadBytes`
- `PROPERTY System.UInt32 CompletionInputRecords`
- `PROPERTY System.UInt32 CompletionPayloadBytes`
- `PROPERTY System.UInt32 CompletionSources`
- `PROPERTY System.UInt32 DiagnosticRecords`
- `PROPERTY System.UInt32 ExecuteCommandRecords`
- `PROPERTY System.UInt32 OperationCancellationPayloadBytes`
- `PROPERTY System.UInt32 OperationRecords`
- `PROPERTY System.UInt32 PendingCompletionRecords`

---

### `AIBT.NativeCommandAsyncDiagnosticCodeV1`

- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 AlreadyConsumedOperation`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 CancelledOperation`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 CompletionPayloadMismatch`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 DuplicateCompletionOrderingKey`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 NonIncreasingSourceSequence`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 None`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 StaleOperationGeneration`
- `FIELD AIBT.NativeCommandAsyncDiagnosticCodeV1 UnknownOperation`
- `FIELD System.UInt16 value__`

---

### `AIBT.NativeCommandAsyncLeaseV1`

- `PROPERTY AIBT.NativeCommandAsyncViewV1 View`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeCommandAsyncOwnerV1`

- `METHOD AIBT.Burst.BurstContextResult TryAcquireExecution(AIBT.NativeCommandAsyncLeaseV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreate(AIBT.TreeInstanceId,AIBT.NativeCommandAsyncCapacityV1,Unity.Collections.Allocator,AIBT.NativeCommandAsyncOwnerV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreate(AIBT.TreeInstanceId,AIBT.NativeCommandAsyncCapacityV1,Unity.Collections.Allocator,System.UInt64,System.UInt64,AIBT.NativeCommandAsyncOwnerV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryDispose()`
- `METHOD AIBT.Burst.BurstContextResult TryGetCommandStream(AIBT.NativeCommandAsyncLeaseV1&,AIBT.NativeCommandStreamViewV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetDiagnostic(AIBT.NativeCommandAsyncLeaseV1&,System.UInt32,AIBT.NativeCompletionDiagnosticV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetOperationState(AIBT.OperationId,AIBT.NativeOperationStateV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryRegisterDependency(AIBT.NativeCommandAsyncLeaseV1&,Unity.Jobs.JobHandle)`
- `METHOD AIBT.Burst.BurstContextResult TryRelease(AIBT.NativeCommandAsyncLeaseV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryResetPublishedBuffers()`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeCommandAsyncViewV1`

- `METHOD AIBT.Burst.BurstContextResult TryCancel(AIBT.OperationId,AIBT.CommandType,AIBT.NativePayloadSliceV1,System.Boolean&)`
- `METHOD AIBT.Burst.BurstContextResult TryConsume(AIBT.RuntimeNodeIndex,System.UInt32,AIBT.OperationId,AIBT.NativeCompletionExpectationV1,AIBT.NativeConsumedCompletionV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryEmitEffect(AIBT.CommandType,AIBT.CommandPhase,AIBT.NativePayloadSliceV1,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryFaultCancelAll(System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetDiagnostic(System.UInt32,AIBT.NativeCompletionDiagnosticV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryNormalizeCompletions(Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeCompletionInputRecordV1>,Unity.Collections.NativeArray`1+ReadOnly<System.Byte>,Unity.Collections.NativeArray`1+ReadOnly<System.UInt32>)`
- `METHOD AIBT.Burst.BurstContextResult TryRestart(System.UInt32&)`
- `METHOD AIBT.Burst.BurstContextResult TryStart(AIBT.RuntimeNodeIndex,System.UInt32,AIBT.CommandType,AIBT.CommandType,AIBT.NativePayloadSliceV1,AIBT.NativePayloadSliceV1,AIBT.OperationId&)`

---

### `AIBT.NativeCommandMergeCapacityV1`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 PayloadBytes`
- `PROPERTY System.UInt32 Records`

---

### `AIBT.NativeCommandMergeOwnerV1`

- `METHOD AIBT.Burst.BurstContextResult TryAddStream(AIBT.NativeCommandStreamViewV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryCreate(AIBT.NativeCommandMergeCapacityV1,Unity.Collections.Allocator,AIBT.NativeCommandMergeOwnerV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryDispose()`
- `METHOD AIBT.Burst.BurstContextResult TryFinalize(AIBT.NativeMergedCommandViewV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetOutput(AIBT.NativeMergedCommandViewV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryReset()`

---

### `AIBT.NativeCommandRecordV1`

- `PROPERTY AIBT.CommandPhase Phase`
- `PROPERTY AIBT.CommandType CommandType`
- `PROPERTY AIBT.OperationId OperationId`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.UInt32 PayloadOffset`
- `PROPERTY System.UInt32 PayloadSize`
- `PROPERTY System.UInt64 Sequence`

---

### `AIBT.NativeCommandStreamViewV1`

Borrowed command stream valid only for the captured lease and command epoch while owner storage is alive. Command publication changes, lease release, or reset invalidate copies. Calling it after successful owner disposal is outside the ABI lifetime contract.

- `METHOD AIBT.Burst.BurstContextResult TryGetPayloadByte(System.UInt32,System.Byte&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetRecord(System.UInt32,AIBT.NativeCommandRecordV1&)`
- `PROPERTY System.UInt32 CancelCount`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 ExecuteCount`
- `PROPERTY System.UInt32 PayloadCount`

---

### `AIBT.NativeCompiledBlackboardSlotRecordV1`

- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.CompiledBlackboardAccessFlags AccessFlags`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 DefaultValueOffset`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 StableKeyId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeCompiledNodeRecordV1`

- `PROPERTY AIBT.CompiledNodeFlags Flags`
- `PROPERTY AIBT.NodeMemoryLifetime MemoryLifetime`
- `PROPERTY System.UInt32 ChildCount`
- `PROPERTY System.UInt32 ChildOffset`
- `PROPERTY System.UInt32 ConfigAlignment`
- `PROPERTY System.UInt32 ConfigOffset`
- `PROPERTY System.UInt32 ConfigSize`
- `PROPERTY System.UInt32 DebugIdentityIndex`
- `PROPERTY System.UInt32 InstanceMemoryAlignment`
- `PROPERTY System.UInt32 InstanceMemoryOffset`
- `PROPERTY System.UInt32 InstanceMemorySize`
- `PROPERTY System.UInt32 NodeTypeVersion`
- `PROPERTY System.UInt32 ReadSlotCount`
- `PROPERTY System.UInt32 ReadSlotOffset`
- `PROPERTY System.UInt32 WriteSlotCount`
- `PROPERTY System.UInt32 WriteSlotOffset`
- `PROPERTY System.UInt64 NodeTypeId`

---

### `AIBT.NativeCompiledObserverRecordV1`

- `PROPERTY AIBT.CompiledObserverMode Mode`
- `PROPERTY System.UInt32 ObserverNodeIndex`
- `PROPERTY System.UInt32 OwningReactiveCompositeIndex`
- `PROPERTY System.UInt32 WatchedSlotCount`
- `PROPERTY System.UInt32 WatchedSlotOffset`

---

### `AIBT.NativeCompiledProgramHeaderV1`

- `PROPERTY AIBT.NativeHash256V1 CanonicalPolicyHash`
- `PROPERTY AIBT.NativeHash256V1 CanonicalSemanticHash`
- `PROPERTY AIBT.NativeHash256V1 CompiledContentHash`
- `PROPERTY AIBT.NativeHash256V1 NodeRegistryHash`
- `PROPERTY System.Byte DeterministicModeCompatible`
- `PROPERTY System.UInt16 CompilerMajor`
- `PROPERTY System.UInt16 CompilerMinor`
- `PROPERTY System.UInt16 CompilerPatch`
- `PROPERTY System.UInt32 BlackboardSlotCount`
- `PROPERTY System.UInt32 CapabilityFlags`
- `PROPERTY System.UInt32 ChildIndexCount`
- `PROPERTY System.UInt32 CompiledFormatVersion`
- `PROPERTY System.UInt32 CompilerBuildRevision`
- `PROPERTY System.UInt32 ConfigBlobSize`
- `PROPERTY System.UInt32 DebugMapCount`
- `PROPERTY System.UInt32 ExecutionSemanticsVersion`
- `PROPERTY System.UInt32 InstanceNodeMemorySize`
- `PROPERTY System.UInt32 Magic`
- `PROPERTY System.UInt32 NodeCount`
- `PROPERTY System.UInt32 PolicyFormatVersion`
- `PROPERTY System.UInt32 RequiredMaximumAlignment`
- `PROPERTY System.UInt32 RootNodeIndex`

---

### `AIBT.NativeCompletionDiagnosticV1`

- `PROPERTY AIBT.NativeCommandAsyncDiagnosticCodeV1 Code`
- `PROPERTY AIBT.OperationId OperationId`
- `PROPERTY System.UInt64 SourceId`
- `PROPERTY System.UInt64 SourceSequence`

---

### `AIBT.NativeCompletionExpectationV1`

- `METHOD AIBT.NativeCompletionExpectationV1 Typed(AIBT.CompletionPayloadType,System.UInt32)`
- `PROPERTY AIBT.NativeCompletionExpectationV1 Any`
- `PROPERTY AIBT.NativeCompletionExpectationV1 NoPayload`

---

### `AIBT.NativeCompletionInputRecordV1`

- `METHOD System.Void .ctor(AIBT.OperationId,AIBT.CompletionOutcome,AIBT.CompletionPayloadType,System.UInt32,System.UInt32,System.UInt64,System.UInt64,AIBT.Revision)`
- `PROPERTY AIBT.CompletionOutcome Outcome`
- `PROPERTY AIBT.CompletionPayloadType PayloadType`
- `PROPERTY AIBT.OperationId OperationId`
- `PROPERTY AIBT.Revision SnapshotRevision`
- `PROPERTY System.UInt32 PayloadOffset`
- `PROPERTY System.UInt32 PayloadSize`
- `PROPERTY System.UInt64 SourceId`
- `PROPERTY System.UInt64 SourceSequence`

---

### `AIBT.NativeConsumedCompletionV1`

Borrowed completion payload valid only for the captured lease and completion epoch while owner storage is alive. A later normalization or lease release invalidates every copy. Calling it after successful owner disposal is outside the ABI lifetime contract.

- `METHOD AIBT.Burst.BurstContextResult TryGetPayloadByte(System.UInt32,System.Byte&)`
- `PROPERTY AIBT.CompletionOutcome Outcome`
- `PROPERTY AIBT.CompletionPayloadType PayloadType`
- `PROPERTY AIBT.OperationId OperationId`
- `PROPERTY AIBT.Revision SnapshotRevision`
- `PROPERTY System.UInt32 PayloadOffset`
- `PROPERTY System.UInt32 PayloadSize`
- `PROPERTY System.UInt64 SourceId`
- `PROPERTY System.UInt64 SourceSequence`

---

### `AIBT.NativeDiagnosticAppendResultV1`

- `FIELD AIBT.NativeDiagnosticAppendResultV1 ChannelFaulted`
- `FIELD AIBT.NativeDiagnosticAppendResultV1 InvalidRecord`
- `FIELD AIBT.NativeDiagnosticAppendResultV1 Written`
- `FIELD System.Byte value__`

---

### `AIBT.NativeDiagnosticChannelCapacityV1`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 RecordCapacity`
- `PROPERTY System.UInt32 RelatedLocationCapacity`

---

### `AIBT.NativeDiagnosticChannelFailureV1`

- `METHOD System.Void .ctor(AIBT.NativeRuntimeDiagnosticCodeV1,AIBT.NativeDiagnosticResourceKindV1,System.UInt64,System.UInt64,System.UInt64,System.UInt32,System.UInt64)`
- `PROPERTY AIBT.NativeDiagnosticResourceKindV1 ResourceKind`
- `PROPERTY AIBT.NativeRuntimeDiagnosticCodeV1 Code`
- `PROPERTY System.Boolean IsSuccess`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 Capacity`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 Requested`

---

### `AIBT.NativeDiagnosticChannelLeaseV1`

- `PROPERTY AIBT.NativeDiagnosticWriterV1 Writer`
- `PROPERTY AIBT.NativeLeaseTokenV1 Token`

---

### `AIBT.NativeDiagnosticChannelOwnerV1`

- `METHOD System.Boolean TryAcquireWriter(AIBT.NativeDiagnosticChannelLeaseV1&,AIBT.NativeDiagnosticChannelFailureV1&)`
- `METHOD System.Boolean TryCreate(AIBT.NativeDiagnosticChannelCapacityV1,AIBT.TreeInstanceId,System.UInt32,Unity.Collections.Allocator,AIBT.NativeDiagnosticChannelOwnerV1&,AIBT.NativeDiagnosticChannelFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeDiagnosticChannelFailureV1&)`
- `METHOD System.Boolean TryGetSnapshot(AIBT.NativeDiagnosticChannelSnapshotV1&,AIBT.NativeDiagnosticChannelFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeDiagnosticChannelLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeDiagnosticChannelFailureV1&)`
- `METHOD System.Boolean TryReleaseWriter(AIBT.NativeDiagnosticChannelLeaseV1,AIBT.NativeDiagnosticChannelFailureV1&)`
- `METHOD System.Boolean TryReset(AIBT.NativeDiagnosticChannelFailureV1&)`
- `PROPERTY AIBT.NativeDiagnosticChannelCapacityV1 Capacity`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeDiagnosticChannelSnapshotV1`

- `PROPERTY AIBT.NativeDiagnosticRecordV1 Rejection`
- `PROPERTY System.Boolean IsFaulted`
- `PROPERTY System.UInt32 RecordCount`
- `PROPERTY System.UInt32 RelatedLocationCount`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticLocationV1> RelatedLocations`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticRecordV1> Records`

---

### `AIBT.NativeDiagnosticFieldIdV1`

- `FIELD AIBT.NativeDiagnosticFieldIdV1 Alignment`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Allocator`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Capacity`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Custom0`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Custom1`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Custom2`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Custom3`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 DroppedCount`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Generation`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 LeaseId`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Left`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 None`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Operation`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 OwnerId`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 OwnerKind`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 OwnerState`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Requested`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 ResourceKind`
- `FIELD AIBT.NativeDiagnosticFieldIdV1 Right`
- `FIELD System.Byte value__`

---

### `AIBT.NativeDiagnosticFieldPairV1`

- `METHOD System.Boolean Equals(AIBT.NativeDiagnosticFieldPairV1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.NativeDiagnosticFieldIdV1,AIBT.NativeDiagnosticValueKindV1,System.UInt64)`
- `PROPERTY AIBT.NativeDiagnosticFieldIdV1 FieldId`
- `PROPERTY AIBT.NativeDiagnosticValueKindV1 ValueKind`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt64 Value`

---

### `AIBT.NativeDiagnosticLocationFlagsV1`

- `FIELD AIBT.NativeDiagnosticLocationFlagsV1 DebugIdentity`
- `FIELD AIBT.NativeDiagnosticLocationFlagsV1 None`
- `FIELD AIBT.NativeDiagnosticLocationFlagsV1 RuntimeNode`
- `FIELD AIBT.NativeDiagnosticLocationFlagsV1 TreeInstance`
- `FIELD System.Byte value__`

---

### `AIBT.NativeDiagnosticLocationV1`

- `METHOD System.Boolean Equals(AIBT.NativeDiagnosticLocationV1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.NativeDiagnosticLocationFlagsV1,System.UInt64,System.UInt32,System.UInt32)`
- `PROPERTY AIBT.NativeDiagnosticLocationFlagsV1 Flags`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 DebugIdentityIndex`
- `PROPERTY System.UInt32 RuntimeNodeIndex`
- `PROPERTY System.UInt64 TreeInstanceId`

---

### `AIBT.NativeDiagnosticMergeV1`

- `METHOD System.Boolean TryMerge(AIBT.NativeDiagnosticChannelSnapshotV1&,AIBT.NativeDiagnosticChannelSnapshotV1&,Unity.Collections.NativeArray`1<AIBT.NativeDiagnosticRecordV1>,Unity.Collections.NativeArray`1<AIBT.NativeDiagnosticLocationV1>,System.UInt32&,System.UInt32&)`
- `METHOD System.Boolean TryMerge(Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticRecordV1>,System.UInt32,Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticLocationV1>,System.UInt32,Unity.Collections.NativeArray`1<AIBT.NativeDiagnosticRecordV1>,Unity.Collections.NativeArray`1<AIBT.NativeDiagnosticLocationV1>,System.UInt32&,System.UInt32&)`
- `METHOD System.Boolean TryMergeRecords(Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticRecordV1>,System.UInt32,Unity.Collections.NativeArray`1<AIBT.NativeDiagnosticRecordV1>,System.UInt32&)`
- `METHOD System.Boolean TrySelectRejection(Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticRecordV1>,System.UInt32,AIBT.NativeDiagnosticRecordV1&)`

---

### `AIBT.NativeDiagnosticOperationV1`

- `FIELD AIBT.NativeDiagnosticOperationV1 Acquire`
- `FIELD AIBT.NativeDiagnosticOperationV1 Append`
- `FIELD AIBT.NativeDiagnosticOperationV1 Dispose`
- `FIELD AIBT.NativeDiagnosticOperationV1 Initialize`
- `FIELD AIBT.NativeDiagnosticOperationV1 Merge`
- `FIELD AIBT.NativeDiagnosticOperationV1 Mutate`
- `FIELD AIBT.NativeDiagnosticOperationV1 None`
- `FIELD AIBT.NativeDiagnosticOperationV1 Project`
- `FIELD AIBT.NativeDiagnosticOperationV1 RegisterDependency`
- `FIELD AIBT.NativeDiagnosticOperationV1 Release`
- `FIELD AIBT.NativeDiagnosticOperationV1 Reset`
- `FIELD AIBT.NativeDiagnosticOperationV1 Schedule`
- `FIELD System.Byte value__`

---

### `AIBT.NativeDiagnosticProjectorV1`

- `METHOD System.Boolean TryProject(AIBT.NativeDiagnosticRecordV1&,System.Collections.Generic.IReadOnlyList`1<AIBT.CompiledDebugMapEntry>,System.Collections.Generic.IReadOnlyList`1<AIBT.NativeDiagnosticLocationV1>,AIBT.Diagnostic&)`

---

### `AIBT.NativeDiagnosticRecordV1`

- `FIELD AIBT.DiagnosticSeverity Severity`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field0`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field1`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field2`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field3`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field4`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field5`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field6`
- `FIELD AIBT.NativeDiagnosticFieldPairV1 Field7`
- `FIELD AIBT.NativeDiagnosticLocationV1 PrimaryLocation`
- `FIELD AIBT.NativeUpdatePhaseV1 Phase`
- `FIELD System.Byte FieldCount`
- `FIELD System.UInt16 CodeNumber`
- `FIELD System.UInt32 RelatedLocationCount`
- `FIELD System.UInt32 RelatedLocationOffset`
- `FIELD System.UInt32 WorkerOrdinal`
- `FIELD System.UInt64 Sequence`
- `FIELD System.UInt64 SnapshotRevision`
- `FIELD System.UInt64 UpdateId`
- `METHOD AIBT.NativeDiagnosticFieldPairV1 GetField(System.Int32)`
- `PROPERTY System.Boolean HasValidHeader`

---

### `AIBT.NativeDiagnosticResourceKindV1`

- `FIELD AIBT.NativeDiagnosticResourceKindV1 CommandPayload`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 CommandRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 CompletionPayload`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 CompletionRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 DiagnosticLocations`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 DiagnosticRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 InstanceBytes`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 LeaseCounter`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 MaximumAlignment`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 None`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 ProgramConfigOrDefaultBytes`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 ProgramRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 ScratchBytes`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 SharedContributionPayload`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 SharedContributionRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 SnapshotPayload`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 SnapshotRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 TracePayload`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 TraceRecords`
- `FIELD AIBT.NativeDiagnosticResourceKindV1 WorkItems`
- `FIELD System.Byte value__`

---

### `AIBT.NativeDiagnosticValueKindV1`

- `FIELD AIBT.NativeDiagnosticValueKindV1 Boolean`
- `FIELD AIBT.NativeDiagnosticValueKindV1 Enum`
- `FIELD AIBT.NativeDiagnosticValueKindV1 Identity`
- `FIELD AIBT.NativeDiagnosticValueKindV1 Signed`
- `FIELD AIBT.NativeDiagnosticValueKindV1 Unsigned`
- `FIELD System.Byte value__`

---

### `AIBT.NativeDiagnosticWriterV1`

- `METHOD AIBT.NativeDiagnosticAppendResultV1 TryAppend(AIBT.NativeDiagnosticRecordV1&)`
- `METHOD AIBT.NativeDiagnosticAppendResultV1 TryAppend(AIBT.NativeDiagnosticRecordV1&,Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeDiagnosticLocationV1>,System.UInt32)`

---

### `AIBT.NativeExecuteSelectionCapacityV1`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 MaximumEntries`
- `PROPERTY System.UInt32 MaximumReaders`

---

### `AIBT.NativeExecuteSelectionEntryV1`

- `METHOD System.Void .ctor(AIBT.TreeInstanceId,System.UInt32,System.UInt32)`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean HasSharedCapacity`
- `PROPERTY System.UInt32 SharedPayloadCapacity`
- `PROPERTY System.UInt32 SharedRecordCapacity`

---

### `AIBT.NativeExecuteSelectionReadLeaseV1`

- `PROPERTY AIBT.NativeExecuteSelectionViewV1 View`
- `PROPERTY AIBT.NativeExecuteSelectionWindowV1 Window`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeExecuteSelectionViewV1`

- `PROPERTY System.Boolean IsCreated`
- `PROPERTY System.UInt32 Count`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeExecuteSelectionEntryV1> Entries`

---

### `AIBT.NativeExecuteSelectionWindowOwnerV1`

- `METHOD System.Boolean TryAbort(AIBT.NativeExecuteSelectionWindowV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireReadLease(AIBT.NativeExecuteSelectionWindowV1,AIBT.NativeExecuteSelectionReadLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryBegin(Unity.Collections.NativeArray`1<AIBT.NativeExecuteSelectionEntryV1>,AIBT.NativeExecuteSelectionWindowV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreate(AIBT.NativeExecuteSelectionCapacityV1,Unity.Collections.Allocator,AIBT.NativeExecuteSelectionWindowOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryEnd(AIBT.NativeExecuteSelectionWindowV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseReadLease(AIBT.NativeExecuteSelectionReadLeaseV1,AIBT.NativeRuntimeFailureV1&)`
- `PROPERTY AIBT.NativeExecuteSelectionCapacityV1 Capacity`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeExecuteSelectionWindowV1`

- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 WindowId`

---

### `AIBT.NativeFrameLifecycleStateV1`

- `FIELD AIBT.NativeFrameLifecycleStateV1 Aborting`
- `FIELD AIBT.NativeFrameLifecycleStateV1 Entering`
- `FIELD AIBT.NativeFrameLifecycleStateV1 Exiting`
- `FIELD AIBT.NativeFrameLifecycleStateV1 Inactive`
- `FIELD AIBT.NativeFrameLifecycleStateV1 Running`
- `FIELD System.Byte value__`

---

### `AIBT.NativeFrameStateV1`

- `FIELD AIBT.NativeFrameLifecycleStateV1 LifecycleState`
- `FIELD AIBT.NodeStatus PendingStatus`
- `FIELD System.Byte HasPendingChildResult`
- `FIELD System.UInt32 ActivationGeneration`
- `FIELD System.UInt32 ChildCursor`
- `FIELD System.UInt32 NodeIndex`
- `FIELD System.UInt32 ParentFrameIndex`
- `FIELD System.UInt64 LastUpdateId`

---

### `AIBT.NativeHash256V1`

- `METHOD System.Boolean Equals(AIBT.NativeHash256V1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Byte GetByte(System.Int32)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.CompiledHash)`

---

### `AIBT.NativeInstanceArenaCapacityV1`

- `METHOD System.Boolean TryDerive(AIBT.NativeProgramImageViewV1,AIBT.NativeInstanceArenaCapacityV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 BudgetStateCount`
- `PROPERTY System.UInt32 FrameCount`
- `PROPERTY System.UInt32 GenerationCount`
- `PROPERTY System.UInt32 MaximumAlignment`
- `PROPERTY System.UInt32 NodeMemoryBytes`
- `PROPERTY System.UInt32 ObserverCount`
- `PROPERTY System.UInt32 ParallelBranchCapacity`
- `PROPERTY System.UInt32 TreeBlackboardBytes`
- `PROPERTY System.UInt32 UpdateStateCount`

---

### `AIBT.NativeInstanceArenaCapacityV2`

- `METHOD System.Boolean TryDerive(AIBT.NativeProgramImageViewV2,AIBT.NativeInstanceArenaCapacityV2&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Void .ctor(AIBT.NativeInstanceArenaCapacityV1,System.UInt32,System.UInt32)`
- `METHOD System.Void .ctor(AIBT.NativeInstanceArenaCapacityV1,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY AIBT.NativeInstanceArenaCapacityV1 Semantic`
- `PROPERTY System.UInt32 RandomStreamCount`
- `PROPERTY System.UInt32 TreeRevisionCount`
- `PROPERTY System.UInt32 TreeSlotVersions`

---

### `AIBT.NativeInstanceArenaOwnerV1`

- `METHOD System.Boolean TryAcquireExecutionLease(AIBT.NativeProgramReadLeaseV1,AIBT.NativeInstanceExecutionLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireExecutionLeaseV2(AIBT.NativeProgramReadLeaseV2,AIBT.NativeInstanceExecutionLeaseV2&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCompleteAbortedExit(System.UInt32,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCompleteTerminalExit(System.UInt32,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreate(AIBT.NativeProgramReadLeaseV1,AIBT.NativeInstanceArenaCapacityV1,Unity.Collections.Allocator,AIBT.NativeInstanceArenaOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreateV2(AIBT.NativeProgramReadLeaseV2,AIBT.NativeInstanceArenaCapacityV2,Unity.Collections.Allocator,AIBT.NativeInstanceArenaOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryInitializeRandomStreams(AIBT.NativeProgramReadLeaseV2,System.UInt64,System.UInt64,Unity.Collections.NativeArray`1<System.UInt32>,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeInstanceExecutionLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeInstanceExecutionLeaseV2,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseExecutionLease(AIBT.NativeInstanceExecutionLeaseV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseExecutionLease(AIBT.NativeInstanceExecutionLeaseV2,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReset(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryResetTreeBlackboard(AIBT.NativeProgramReadLeaseV2,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRestart(AIBT.NativeRuntimeFailureV1&)`
- `PROPERTY AIBT.NativeInstanceArenaCapacityV1 Capacity`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY System.Boolean HasBlackboardV2`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt32 ProgramGeneration`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 ProgramOwnerId`

---

### `AIBT.NativeInstanceArenaViewV1`

- `PROPERTY Unity.Collections.NativeArray`1<AIBT.NativeBudgetStateV1> BudgetState`
- `PROPERTY Unity.Collections.NativeArray`1<AIBT.NativeFrameStateV1> Frames`
- `PROPERTY Unity.Collections.NativeArray`1<AIBT.NativeObserverStateV1> Observers`
- `PROPERTY Unity.Collections.NativeArray`1<AIBT.NativeParallelBranchStateV1> ParallelBranches`
- `PROPERTY Unity.Collections.NativeArray`1<AIBT.NativeUpdateStateV1> UpdateState`
- `PROPERTY Unity.Collections.NativeArray`1<System.Byte> NodeMemory`
- `PROPERTY Unity.Collections.NativeArray`1<System.Byte> TreeBlackboard`
- `PROPERTY Unity.Collections.NativeArray`1<System.UInt32> Generations`

---

### `AIBT.NativeInstanceArenaViewV2`

- `PROPERTY AIBT.NativeInstanceArenaViewV1 Semantic`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> RandomNodeIndices`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt64> RandomIncrements`
- `PROPERTY Unity.Collections.NativeArray`1<System.UInt64> RandomStates`
- `PROPERTY Unity.Collections.NativeArray`1<System.UInt64> TreeRevision`
- `PROPERTY Unity.Collections.NativeArray`1<System.UInt64> TreeSlotVersions`

---

### `AIBT.NativeInstanceExecutionLeaseV1`

- `PROPERTY AIBT.NativeInstanceArenaViewV1 View`
- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeInstanceExecutionLeaseV2`

- `PROPERTY AIBT.NativeInstanceArenaViewV2 View`
- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeProgramImageViewV2 Program`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeLeaseTokenV1`

- `METHOD System.Boolean Equals(AIBT.NativeLeaseTokenV1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeMergedCommandViewV1`

Borrowed view valid only while its owner storage is alive. Reset invalidates copied views; calling a copied view after successful owner disposal is outside the ABI lifetime contract.

- `METHOD AIBT.Burst.BurstContextResult TryGetPayloadByte(System.UInt32,System.Byte&)`
- `METHOD AIBT.Burst.BurstContextResult TryGetRecord(System.UInt32,AIBT.NativeCommandRecordV1&)`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 PayloadCount`

---

### `AIBT.NativeNodeBlackboardAccessRangeV2`

- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 Offset`

---

### `AIBT.NativeObserverStateV1`

- `FIELD System.Byte HasLastConditionResult`
- `FIELD System.Byte LastConditionResult`
- `FIELD System.UInt32 ObserverNodeIndex`
- `FIELD System.UInt32 OwningReactiveCompositeIndex`

---

### `AIBT.NativeOperationStateV1`

- `FIELD AIBT.NativeOperationStateV1 Active`
- `FIELD AIBT.NativeOperationStateV1 Cancelled`
- `FIELD AIBT.NativeOperationStateV1 Consumed`
- `FIELD AIBT.NativeOperationStateV1 Empty`
- `FIELD System.Byte value__`

---

### `AIBT.NativeOwnerStateV1`

- `FIELD AIBT.NativeOwnerStateV1 Disposed`
- `FIELD AIBT.NativeOwnerStateV1 Executing`
- `FIELD AIBT.NativeOwnerStateV1 Initialized`
- `FIELD AIBT.NativeOwnerStateV1 Uninitialized`
- `FIELD System.Byte value__`

---

### `AIBT.NativeParallelBranchStateV1`

- `FIELD System.Byte State`
- `FIELD System.UInt32 CapacityOrdinal`
- `FIELD System.UInt32 NodeIndex`

---

### `AIBT.NativePayloadSliceV1`

- `METHOD System.Void .ctor(Unity.Collections.NativeArray`1+ReadOnly<System.Byte>,System.UInt32,System.UInt32)`
- `PROPERTY AIBT.NativePayloadSliceV1 Empty`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> Bytes`

---

### `AIBT.NativeProgramBlackboardBindingV2`

- `METHOD System.Byte[] GetOuterPreimageCopy()`
- `METHOD System.Void .ctor(AIBT.CompiledProgram,System.Byte[],System.Collections.Generic.IEnumerable`1<AIBT.NativeBlackboardScopeBindingV2>,System.Collections.Generic.IEnumerable`1<AIBT.NativeBlackboardSlotBindingV2>,System.Collections.Generic.IEnumerable`1<AIBT.NativeBlackboardSlotAuthorityV2>,System.Collections.Generic.IEnumerable`1<AIBT.NativeBlackboardAccessBindingV2>,System.Collections.Generic.IEnumerable`1<AIBT.NativeBlackboardWatchedSlotBindingV2>,System.Collections.Generic.IEnumerable`1<AIBT.NativeRegisteredBlackboardTypeBindingV2>,System.Collections.Generic.IEnumerable`1<AIBT.NativeRegisteredBlackboardFieldBindingV2>)`
- `PROPERTY AIBT.CompiledHash OuterContentHash`
- `PROPERTY AIBT.CompiledProgram SemanticProgram`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeBlackboardAccessBindingV2> Accesses`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeBlackboardScopeBindingV2> Scopes`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeBlackboardSlotAuthorityV2> SlotAuthorities`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeBlackboardSlotBindingV2> Slots`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeBlackboardWatchedSlotBindingV2> WatchedSlots`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeRegisteredBlackboardFieldBindingV2> RegisteredFields`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeRegisteredBlackboardTypeBindingV2> RegisteredTypes`

---

### `AIBT.NativeProgramImageCapacityV1`

- `METHOD AIBT.NativeProgramImageCapacityV1 Exact(AIBT.CompiledProgram)`
- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 BlackboardSlots`
- `PROPERTY System.UInt32 ChildIndices`
- `PROPERTY System.UInt32 ConfigBytes`
- `PROPERTY System.UInt32 DebugOrdinals`
- `PROPERTY System.UInt32 DefaultBytes`
- `PROPERTY System.UInt32 MaximumAlignment`
- `PROPERTY System.UInt32 NodeRecords`
- `PROPERTY System.UInt32 Observers`
- `PROPERTY System.UInt32 ReadSlotIndices`
- `PROPERTY System.UInt32 WatchedSlotIndices`
- `PROPERTY System.UInt32 WriteSlotIndices`

---

### `AIBT.NativeProgramImageCapacityV2`

- `METHOD AIBT.NativeProgramImageCapacityV2 Exact(AIBT.NativeProgramBlackboardBindingV2)`
- `METHOD System.Void .ctor(AIBT.NativeProgramImageCapacityV1,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY AIBT.NativeProgramImageCapacityV1 Semantic`
- `PROPERTY System.UInt32 Accesses`
- `PROPERTY System.UInt32 NodeAccessRanges`
- `PROPERTY System.UInt32 RegisteredFields`
- `PROPERTY System.UInt32 RegisteredTypes`
- `PROPERTY System.UInt32 ScopeDescriptors`
- `PROPERTY System.UInt32 ScopeLayoutBytes`
- `PROPERTY System.UInt32 Slots`

---

### `AIBT.NativeProgramImageOwnerV1`

- `METHOD System.Boolean TryAcquireReadLease(AIBT.NativeProgramReadLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireReadLeaseV2(AIBT.NativeProgramReadLeaseV2&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreate(AIBT.CompiledProgram,AIBT.NativeProgramImageCapacityV1,Unity.Collections.Allocator,AIBT.NativeProgramImageOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreateV2(AIBT.NativeProgramBlackboardBindingV2,AIBT.NativeProgramImageCapacityV2,Unity.Collections.Allocator,AIBT.NativeProgramImageOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeProgramReadLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeProgramReadLeaseV2,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseReadLease(AIBT.NativeProgramReadLeaseV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReleaseReadLease(AIBT.NativeProgramReadLeaseV2,AIBT.NativeRuntimeFailureV1&)`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY System.Boolean HasBlackboardV2`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.CompiledDebugMapEntry> HostDebugMap`
- `PROPERTY System.Int32 ActiveReaderCount`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeProgramImageViewV1`

- `PROPERTY AIBT.NativeCompiledProgramHeaderV1 Header`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeCompiledBlackboardSlotRecordV1> BlackboardSlots`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeCompiledNodeRecordV1> Nodes`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeCompiledObserverRecordV1> Observers`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> ConfigBlob`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> DefaultValueBlob`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> ChildIndices`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> DebugRuntimeNodeIndices`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> ReadSlotIndices`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> WatchedSlotIndices`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> WriteSlotIndices`

---

### `AIBT.NativeProgramImageViewV2`

- `PROPERTY AIBT.NativeCompiledProgramHeaderV1 Header`
- `PROPERTY AIBT.NativeProgramImageViewV1 Semantic`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeBlackboardAccessRecordV2> Accesses`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeBlackboardScopeRecordV2> Scopes`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeBlackboardSlotBindingV2> Slots`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeNodeBlackboardAccessRangeV2> NodeAccessRanges`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeRegisteredBlackboardFieldRecordV2> RegisteredFields`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeRegisteredBlackboardTypeRecordV2> RegisteredTypes`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> ScopeLayoutBytes`

---

### `AIBT.NativeProgramReadLeaseV1`

- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeProgramImageViewV1 View`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeProgramReadLeaseV2`

- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeProgramImageViewV2 View`

---

### `AIBT.NativeRegisteredBlackboardFieldBindingV2`

- `METHOD System.Void .ctor(System.UInt64,System.UInt64,System.UInt32,System.UInt32,System.UInt32,System.UInt32,AIBT.NativeBlackboardFieldEncodingV2,System.UInt64,AIBT.CompiledHash,System.UInt64)`
- `PROPERTY AIBT.CompiledHash RegisteredSchemaHash`
- `PROPERTY AIBT.NativeBlackboardFieldEncodingV2 Encoding`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 ValueTypeVersion`
- `PROPERTY System.UInt64 EqualityContractId`
- `PROPERTY System.UInt64 FieldId`
- `PROPERTY System.UInt64 RegisteredSchemaId`
- `PROPERTY System.UInt64 ValueTypeId`

---

### `AIBT.NativeRegisteredBlackboardFieldRecordV2`

- `PROPERTY AIBT.NativeBlackboardFieldEncodingV2 Encoding`
- `PROPERTY AIBT.NativeHash256V1 RegisteredSchemaHash`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 ValueTypeVersion`
- `PROPERTY System.UInt64 EqualityContractId`
- `PROPERTY System.UInt64 FieldId`
- `PROPERTY System.UInt64 RegisteredSchemaId`
- `PROPERTY System.UInt64 ValueTypeId`

---

### `AIBT.NativeRegisteredBlackboardTypeBindingV2`

- `METHOD System.Byte[] GetSchemaPreimageCopy()`
- `METHOD System.Void .ctor(AIBT.RegisteredUnmanagedTypeDescriptor,AIBT.CompiledHash,System.UInt32,System.UInt32)`
- `METHOD System.Void .ctor(AIBT.RegisteredUnmanagedTypeDescriptor,System.Byte[],System.UInt32,System.UInt32)`
- `PROPERTY AIBT.CompiledHash SchemaHash`
- `PROPERTY AIBT.RegisteredUnmanagedTypeDescriptor Descriptor`
- `PROPERTY System.UInt32 FieldCount`
- `PROPERTY System.UInt32 FirstField`

---

### `AIBT.NativeRegisteredBlackboardTypeRecordV2`

- `PROPERTY AIBT.NativeHash256V1 SchemaHash`
- `PROPERTY AIBT.RegisteredUnmanagedTypeDescriptor Descriptor`
- `PROPERTY System.UInt32 FieldCount`
- `PROPERTY System.UInt32 FirstField`

---

### `AIBT.NativeResourceKindV1`

- `FIELD AIBT.NativeResourceKindV1 AgentBindings`
- `FIELD AIBT.NativeResourceKindV1 AgentExecuteWindowOwners`
- `FIELD AIBT.NativeResourceKindV1 ExecuteSelectionEntries`
- `FIELD AIBT.NativeResourceKindV1 ExecuteSelectionReaders`
- `FIELD AIBT.NativeResourceKindV1 InstanceAgentBlackboard`
- `FIELD AIBT.NativeResourceKindV1 InstanceAgentRevision`
- `FIELD AIBT.NativeResourceKindV1 InstanceAgentSlotVersions`
- `FIELD AIBT.NativeResourceKindV1 InstanceBudgetState`
- `FIELD AIBT.NativeResourceKindV1 InstanceFrames`
- `FIELD AIBT.NativeResourceKindV1 InstanceGenerations`
- `FIELD AIBT.NativeResourceKindV1 InstanceNodeMemory`
- `FIELD AIBT.NativeResourceKindV1 InstanceObservers`
- `FIELD AIBT.NativeResourceKindV1 InstanceParallelBranches`
- `FIELD AIBT.NativeResourceKindV1 InstanceRandomIncrements`
- `FIELD AIBT.NativeResourceKindV1 InstanceRandomNodeIndices`
- `FIELD AIBT.NativeResourceKindV1 InstanceRandomStates`
- `FIELD AIBT.NativeResourceKindV1 InstanceSharedBlackboard`
- `FIELD AIBT.NativeResourceKindV1 InstanceSharedRevision`
- `FIELD AIBT.NativeResourceKindV1 InstanceSharedSlotVersions`
- `FIELD AIBT.NativeResourceKindV1 InstanceTreeBlackboard`
- `FIELD AIBT.NativeResourceKindV1 InstanceTreeRevision`
- `FIELD AIBT.NativeResourceKindV1 InstanceTreeSlotVersions`
- `FIELD AIBT.NativeResourceKindV1 InstanceUpdateState`
- `FIELD AIBT.NativeResourceKindV1 LeaseCounter`
- `FIELD AIBT.NativeResourceKindV1 LifecycleBatchLanes`
- `FIELD AIBT.NativeResourceKindV1 MaximumAlignment`
- `FIELD AIBT.NativeResourceKindV1 None`
- `FIELD AIBT.NativeResourceKindV1 ProgramBlackboardAccesses`
- `FIELD AIBT.NativeResourceKindV1 ProgramBlackboardSlots`
- `FIELD AIBT.NativeResourceKindV1 ProgramChildIndices`
- `FIELD AIBT.NativeResourceKindV1 ProgramConfigBytes`
- `FIELD AIBT.NativeResourceKindV1 ProgramDebugOrdinals`
- `FIELD AIBT.NativeResourceKindV1 ProgramDefaultBytes`
- `FIELD AIBT.NativeResourceKindV1 ProgramHash`
- `FIELD AIBT.NativeResourceKindV1 ProgramNodeAccessRanges`
- `FIELD AIBT.NativeResourceKindV1 ProgramNodes`
- `FIELD AIBT.NativeResourceKindV1 ProgramObservers`
- `FIELD AIBT.NativeResourceKindV1 ProgramReadSlotIndices`
- `FIELD AIBT.NativeResourceKindV1 ProgramRegisteredFields`
- `FIELD AIBT.NativeResourceKindV1 ProgramRegisteredTypes`
- `FIELD AIBT.NativeResourceKindV1 ProgramScopeDescriptors`
- `FIELD AIBT.NativeResourceKindV1 ProgramScopeLayoutBytes`
- `FIELD AIBT.NativeResourceKindV1 ProgramWatchedSlotIndices`
- `FIELD AIBT.NativeResourceKindV1 ProgramWriteSlotIndices`
- `FIELD AIBT.NativeResourceKindV1 SharedBindings`
- `FIELD AIBT.NativeResourceKindV1 SharedCommitReport`
- `FIELD AIBT.NativeResourceKindV1 SharedContributionPayload`
- `FIELD AIBT.NativeResourceKindV1 SharedContributionRecords`
- `FIELD AIBT.NativeResourceKindV1 SharedContributionStreams`
- `FIELD AIBT.NativeResourceKindV1 SharedReductionScratch`
- `FIELD System.Byte value__`

---

### `AIBT.NativeRuntimeDiagnosticCodeV1`

- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardEqualityFault`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardInvalidSlot`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardInvalidValue`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardMissingTypeBinding`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardRegistryMismatch`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardTypeMismatch`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardUndeclaredAccess`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardUnsupportedScope`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 BlackboardVersionOverflow`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeAllocatorInvalid`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeCapacityArithmeticOverflow`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeCapacityPlanInvalid`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeCompletionCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeDiagnosticCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeInstanceCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeLifetimeStateInvalid`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeLiveJobOwnershipViolation`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeOutputCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeProgramCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeSnapshotCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 NativeTraceCapacityExceeded`
- `FIELD AIBT.NativeRuntimeDiagnosticCodeV1 None`
- `FIELD System.UInt16 value__`

---

### `AIBT.NativeRuntimeFailureV1`

- `METHOD System.Boolean Equals(AIBT.NativeRuntimeFailureV1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.NativeRuntimeDiagnosticCodeV1,AIBT.NativeResourceKindV1,System.UInt64,System.UInt64,System.UInt32,System.UInt64,System.UInt32,System.UInt64)`
- `PROPERTY AIBT.NativeResourceKindV1 ResourceKind`
- `PROPERTY AIBT.NativeRuntimeDiagnosticCodeV1 Code`
- `PROPERTY System.Boolean IsSuccess`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 Capacity`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 Requested`

---

### `AIBT.NativeSharedBindingV1`

- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 BindingId`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeSharedBlackboardV1`

- `METHOD AIBT.Burst.BurstContextResult TryContribute(AIBT.NativeSharedContributionWriterV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,Unity.Collections.NativeArray`1<>)`
- `METHOD AIBT.Burst.BurstContextResult TryRead(AIBT.NativeSharedExecutionViewV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryWrite(AIBT.NativeSharedExecutionViewV1,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,Unity.Collections.NativeArray`1<>,System.Boolean&)`

---

### `AIBT.NativeSharedCommitReportV1`

- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 ChangedSlotCount`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 EligibleUpdateId`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 ReportId`
- `PROPERTY System.UInt64 Revision`
- `PROPERTY System.UInt64 SourceUpdateId`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt32> ChangedScopeSlots`

---

### `AIBT.NativeSharedContextCapacityV1`

- `METHOD System.Boolean TryDerive(AIBT.NativeProgramImageViewV2,System.UInt32,System.UInt32,System.UInt32,System.UInt32,AIBT.NativeSharedContextCapacityV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 ContributionPayloadBytes`
- `PROPERTY System.UInt32 ContributionRecords`
- `PROPERTY System.UInt32 MaximumBindings`
- `PROPERTY System.UInt32 MaximumSelectedInstances`
- `PROPERTY System.UInt32 SlotVersions`
- `PROPERTY System.UInt32 ValueBytes`

---

### `AIBT.NativeSharedContextOwnerV1`

- `METHOD System.Boolean TryAbortUpdate(AIBT.NativeSharedUpdateWindowV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireContributionStream(AIBT.NativeSharedUpdateWindowV1,AIBT.NativeSharedBindingV1,AIBT.NativeAgentExecuteLeaseV2,AIBT.NativeSharedContributionLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireContributionStream(AIBT.NativeSharedUpdateWindowV1,AIBT.NativeSharedBindingV1,AIBT.NativeInstanceExecutionLeaseV2,AIBT.NativeSharedContributionLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireExecutionView(AIBT.NativeSharedBindingV1,AIBT.NativeInstanceExecutionLeaseV2,AIBT.NativeSharedExecutionViewV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryAcquireReductionLease(AIBT.NativeSharedUpdateWindowV1,AIBT.NativeSharedReductionLeaseV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryBeginUpdate(AIBT.NativeExecuteSelectionWindowOwnerV1,AIBT.NativeExecuteSelectionWindowV1,AIBT.NativeSharedUpdateWindowV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryBind(AIBT.TreeInstanceId,AIBT.NativeProgramReadLeaseV2,AIBT.NativeInstanceArenaOwnerV1,AIBT.NativeSharedBindingV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCancelContributionStream(AIBT.NativeSharedContributionLeaseV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCompleteReduction(AIBT.NativeSharedReductionLeaseV1,AIBT.NativeSharedCommitReportV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryCreate(AIBT.NativeProgramReadLeaseV2,AIBT.NativeSharedContextCapacityV1,Unity.Collections.Allocator,AIBT.NativeSharedContextOwnerV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReduceUpdate(AIBT.NativeSharedUpdateWindowV1,AIBT.NativeSharedCommitReportV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeSharedContributionLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryRegisterReductionDependency(AIBT.NativeSharedReductionLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryReset(AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TrySealContributionStream(AIBT.NativeSharedContributionLeaseV1,AIBT.NativeSharedContributionStreamViewV1&,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Boolean TryUnbind(AIBT.NativeSharedBindingV1,AIBT.NativeRuntimeFailureV1&)`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY AIBT.NativeSharedContextCapacityV1 Capacity`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeSharedContextViewV1`

- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> Values`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt64> Revision`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.UInt64> SlotVersions`

---

### `AIBT.NativeSharedContributionLeaseV1`

- `PROPERTY AIBT.NativeInstanceExecutionLeaseV2 Execution`
- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeSharedContributionWriterV1 Writer`
- `PROPERTY AIBT.NativeSharedUpdateWindowV1 Update`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeSharedContributionRecordV1`

- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.UInt32 PayloadCapacity`
- `PROPERTY System.UInt32 PayloadLength`
- `PROPERTY System.UInt32 PayloadOffset`
- `PROPERTY System.UInt32 RecordCapacity`
- `PROPERTY System.UInt32 RegisteredTypeIndex`
- `PROPERTY System.UInt32 ScopeSlotIndex`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 EnumContractId`
- `PROPERTY System.UInt64 Sequence`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeSharedContributionStreamV1`

- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Capacity`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 PayloadByteCapacity`
- `PROPERTY System.UInt32 PayloadByteCount`
- `PROPERTY System.UInt64 ContributionSequence`

---

### `AIBT.NativeSharedContributionStreamViewV1`

- `PROPERTY AIBT.NativeSharedContributionStreamV1 Stream`
- `PROPERTY System.Boolean IsCreated`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeSharedContributionRecordV1> Records`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> Payload`

---

### `AIBT.NativeSharedContributionWriterV1`

- `PROPERTY System.Boolean IsCreated`

---

### `AIBT.NativeSharedExecutionViewV1`

- `PROPERTY AIBT.NativeProgramImageViewV2 Program`
- `PROPERTY AIBT.NativeSharedContextViewV1 Context`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeSharedReductionLeaseV1`

- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeSharedReductionViewV1 View`
- `PROPERTY AIBT.NativeSharedUpdateWindowV1 Update`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeSharedReductionV1`

- `METHOD AIBT.Burst.BurstContextResult TryReduce(AIBT.NativeSharedReductionViewV1)`

---

### `AIBT.NativeSharedReductionViewV1`

- `PROPERTY System.Boolean IsCreated`

---

### `AIBT.NativeSharedUpdateWindowV1`

- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Count`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt32 SelectionGeneration`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 SelectionOwnerId`
- `PROPERTY System.UInt64 SelectionWindowId`
- `PROPERTY System.UInt64 UpdateId`

---

### `AIBT.NativeSnapshotBuilderV1`

- `METHOD AIBT.Burst.BurstContextResult TryAdd(AIBT.NativeSnapshotTypeDescriptorV1&,)`
- `METHOD AIBT.Burst.BurstContextResult TryCreate(System.UInt32,System.UInt32,AIBT.NativeSnapshotBuilderV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryDeclareMissing(AIBT.NativeSnapshotTypeDescriptorV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryDispose()`
- `METHOD AIBT.Burst.BurstContextResult TryFreeze(System.UInt64,System.UInt32,AIBT.NativeSnapshotOwnerV1&)`
- `PROPERTY System.UInt32 EntryCount`
- `PROPERTY System.UInt32 PayloadBytesUsed`

---

### `AIBT.NativeSnapshotLeaseTokenV1`

- `METHOD System.Boolean Equals(AIBT.NativeSnapshotLeaseTokenV1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeSnapshotOwnerV1`

- `METHOD AIBT.Burst.BurstContextResult TryAcquireRead(AIBT.NativeSnapshotReadLeaseV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryDispose()`
- `METHOD AIBT.Burst.BurstContextResult TryRegisterDependency(AIBT.NativeSnapshotReadLeaseV1&,Unity.Jobs.JobHandle)`
- `METHOD AIBT.Burst.BurstContextResult TryRelease(AIBT.NativeSnapshotReadLeaseV1&)`
- `METHOD AIBT.Burst.BurstContextResult TryResolve(AIBT.NativeSnapshotTypeDescriptorV1&,)`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt32 ReaderCapacity`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 Revision`

---

### `AIBT.NativeSnapshotReadHandleV1`1`

- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeSnapshotReadLeaseV1`

- `PROPERTY AIBT.NativeSnapshotLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeSnapshotViewV1 View`
- `PROPERTY System.Boolean IsValid`

---

### `AIBT.NativeSnapshotTypeDescriptorV1`

Describes one explicitly registered snapshot binding. The schema and layout hashes, together with the physical size and alignment, make the same-process representation exact.

- `METHOD System.Boolean Equals(AIBT.NativeSnapshotTypeDescriptorV1)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt64,System.UInt64,System.UInt32,AIBT.Burst.BurstHash256,AIBT.Burst.BurstHash256,System.UInt32,System.UInt32)`
- `PROPERTY AIBT.Burst.BurstHash256 LayoutHash`
- `PROPERTY AIBT.Burst.BurstHash256 SchemaHash`
- `PROPERTY System.UInt32 Alignment`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 TypeVersion`
- `PROPERTY System.UInt64 BindingId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.NativeSnapshotViewV1`

An immutable, non-owning job view of one snapshot revision. Payload bytes use the registered same-process unmanaged layout only; they are not persisted, hashed, or treated as a cross-platform canonical encoding.

- `METHOD AIBT.Burst.BurstContextResult TryRead(AIBT.NativeSnapshotReadHandleV1`1<>,)`
- `PROPERTY System.UInt64 Revision`

---

### `AIBT.NativeTraceAppendResultV1`

- `FIELD AIBT.NativeTraceAppendResultV1 ChannelFaulted`
- `FIELD AIBT.NativeTraceAppendResultV1 Dropped`
- `FIELD AIBT.NativeTraceAppendResultV1 DuplicateSequence`
- `FIELD AIBT.NativeTraceAppendResultV1 Filtered`
- `FIELD AIBT.NativeTraceAppendResultV1 InvalidRecord`
- `FIELD AIBT.NativeTraceAppendResultV1 Written`
- `FIELD System.Byte value__`

---

### `AIBT.NativeTraceChannelCapacityV1`

- `METHOD System.Void .ctor(System.UInt32,System.UInt32,System.UInt32,System.UInt32)`
- `PROPERTY System.UInt32 EmissionCapacity`
- `PROPERTY System.UInt32 MaximumPayloadBytes`
- `PROPERTY System.UInt32 OrdinaryRecordCapacity`
- `PROPERTY System.UInt32 PayloadCapacity`
- `PROPERTY System.UInt32 RecordCapacity`

---

### `AIBT.NativeTraceChannelFailureV1`

- `METHOD System.Void .ctor(AIBT.NativeRuntimeDiagnosticCodeV1,AIBT.NativeDiagnosticResourceKindV1,System.UInt64,System.UInt64,System.UInt64,System.UInt32,System.UInt64)`
- `PROPERTY AIBT.NativeDiagnosticResourceKindV1 ResourceKind`
- `PROPERTY AIBT.NativeRuntimeDiagnosticCodeV1 Code`
- `PROPERTY System.Boolean IsSuccess`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 Capacity`
- `PROPERTY System.UInt64 LeaseId`
- `PROPERTY System.UInt64 OwnerId`
- `PROPERTY System.UInt64 Requested`

---

### `AIBT.NativeTraceChannelLeaseV1`

- `PROPERTY AIBT.NativeLeaseTokenV1 Token`
- `PROPERTY AIBT.NativeTraceWriterV1 Writer`

---

### `AIBT.NativeTraceChannelOwnerV1`

- `METHOD System.Boolean TryAcquireWriter(AIBT.NativeTraceChannelLeaseV1&,AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Boolean TryCreate(AIBT.NativeTraceChannelCapacityV1,AIBT.NativeTraceLevelV1,AIBT.TreeInstanceId,System.UInt32,Unity.Collections.Allocator,AIBT.NativeTraceChannelOwnerV1&,AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Boolean TryDispose(AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Boolean TryGetSnapshot(AIBT.NativeTraceChannelSnapshotV1&,AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Boolean TryRegisterDependency(AIBT.NativeTraceChannelLeaseV1,Unity.Jobs.JobHandle,AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Boolean TryReleaseWriter(AIBT.NativeTraceChannelLeaseV1,AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Boolean TryReset(AIBT.NativeTraceChannelFailureV1&)`
- `PROPERTY AIBT.NativeOwnerStateV1 State`
- `PROPERTY AIBT.NativeTraceChannelCapacityV1 Capacity`
- `PROPERTY AIBT.NativeTraceLevelV1 Level`
- `PROPERTY System.UInt32 Generation`
- `PROPERTY System.UInt64 OwnerId`

---

### `AIBT.NativeTraceChannelSnapshotV1`

- `PROPERTY System.Boolean IsFaulted`
- `PROPERTY System.UInt32 RecordCount`
- `PROPERTY System.UInt64 DroppedCount`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeTraceRecordV1> Records`
- `PROPERTY Unity.Collections.NativeArray`1+ReadOnly<System.Byte> Payload`

---

### `AIBT.NativeTraceEventKindV1`

- `FIELD AIBT.NativeTraceEventKindV1 BlackboardChanged`
- `FIELD AIBT.NativeTraceEventKindV1 BudgetYielded`
- `FIELD AIBT.NativeTraceEventKindV1 CommandEmitted`
- `FIELD AIBT.NativeTraceEventKindV1 CompletionConsumed`
- `FIELD AIBT.NativeTraceEventKindV1 CompletionDiscarded`
- `FIELD AIBT.NativeTraceEventKindV1 DiagnosticRaised`
- `FIELD AIBT.NativeTraceEventKindV1 ExecutionResumed`
- `FIELD AIBT.NativeTraceEventKindV1 NodeAbortStarted`
- `FIELD AIBT.NativeTraceEventKindV1 NodeEntered`
- `FIELD AIBT.NativeTraceEventKindV1 NodeExited`
- `FIELD AIBT.NativeTraceEventKindV1 NodeTicked`
- `FIELD AIBT.NativeTraceEventKindV1 ObserverEvaluated`
- `FIELD AIBT.NativeTraceEventKindV1 ObserverQueued`
- `FIELD AIBT.NativeTraceEventKindV1 SchedulerDecision`
- `FIELD AIBT.NativeTraceEventKindV1 TraceDroppedSummary`
- `FIELD AIBT.NativeTraceEventKindV1 UpdateCompleted`
- `FIELD AIBT.NativeTraceEventKindV1 UpdateStarted`
- `FIELD System.Byte value__`

---

### `AIBT.NativeTraceLevelV1`

- `FIELD AIBT.NativeTraceLevelV1 Detailed`
- `FIELD AIBT.NativeTraceLevelV1 Errors`
- `FIELD AIBT.NativeTraceLevelV1 Lifecycle`
- `FIELD AIBT.NativeTraceLevelV1 Off`
- `FIELD System.Byte value__`

---

### `AIBT.NativeTraceMergeV1`

- `METHOD System.Boolean TryMerge(AIBT.NativeTraceChannelSnapshotV1&,AIBT.NativeTraceChannelSnapshotV1&,Unity.Collections.NativeArray`1<AIBT.NativeTraceRecordV1>,Unity.Collections.NativeArray`1<System.Byte>,System.UInt32&,System.UInt32&)`
- `METHOD System.Boolean TryMerge(Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeTraceRecordV1>,System.UInt32,Unity.Collections.NativeArray`1+ReadOnly<System.Byte>,System.UInt32,Unity.Collections.NativeArray`1<AIBT.NativeTraceRecordV1>,Unity.Collections.NativeArray`1<System.Byte>,System.UInt32&,System.UInt32&)`
- `METHOD System.Boolean TryMerge(Unity.Collections.NativeArray`1+ReadOnly<AIBT.NativeTraceRecordV1>,System.UInt32,Unity.Collections.NativeArray`1<AIBT.NativeTraceRecordV1>,System.UInt32&)`

---

### `AIBT.NativeTraceNodeAbortReasonV1`

- `FIELD AIBT.NativeTraceNodeAbortReasonV1 Explicit`
- `FIELD AIBT.NativeTraceNodeAbortReasonV1 HotReload`
- `FIELD AIBT.NativeTraceNodeAbortReasonV1 ObserverLowerPriority`
- `FIELD AIBT.NativeTraceNodeAbortReasonV1 ObserverSelf`
- `FIELD AIBT.NativeTraceNodeAbortReasonV1 Timeout`
- `FIELD AIBT.NativeTraceNodeAbortReasonV1 TreeStopped`
- `FIELD System.Byte value__`

---

### `AIBT.NativeTraceNodeExitReasonV1`

- `FIELD AIBT.NativeTraceNodeExitReasonV1 Aborted`
- `FIELD AIBT.NativeTraceNodeExitReasonV1 Failure`
- `FIELD AIBT.NativeTraceNodeExitReasonV1 Success`
- `FIELD System.Byte value__`

---

### `AIBT.NativeTraceOptionalFieldsV1`

- `FIELD AIBT.NativeTraceOptionalFieldsV1 AbortReason`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 BlackboardVersions`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 DebugIdentity`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 DiagnosticCode`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 ExitReason`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 None`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 RuntimeNode`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 SourceNode`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 Status`
- `FIELD System.Byte value__`

---

### `AIBT.NativeTraceRecordV1`

- `FIELD AIBT.NativeHash256V1 TreeSemanticHash`
- `FIELD AIBT.NativeTraceEventKindV1 Kind`
- `FIELD AIBT.NativeTraceNodeAbortReasonV1 AbortReason`
- `FIELD AIBT.NativeTraceNodeExitReasonV1 ExitReason`
- `FIELD AIBT.NativeTraceOptionalFieldsV1 OptionalFields`
- `FIELD AIBT.NativeUpdatePhaseV1 Phase`
- `FIELD AIBT.NodeStatus Status`
- `FIELD System.UInt16 DiagnosticCodeNumber`
- `FIELD System.UInt32 DebugIdentityIndex`
- `FIELD System.UInt32 FormatVersion`
- `FIELD System.UInt32 PayloadLength`
- `FIELD System.UInt32 PayloadOffset`
- `FIELD System.UInt32 RuntimeNodeIndex`
- `FIELD System.UInt32 SourceNodeIndex`
- `FIELD System.UInt32 TraceFormatVersion`
- `FIELD System.UInt32 WorkerOrdinal`
- `FIELD System.UInt64 DroppedCount`
- `FIELD System.UInt64 NewBlackboardVersion`
- `FIELD System.UInt64 OldBlackboardVersion`
- `FIELD System.UInt64 OperationOrDecisionId`
- `FIELD System.UInt64 Sequence`
- `FIELD System.UInt64 SnapshotRevision`
- `FIELD System.UInt64 StableBlackboardKeyId`
- `FIELD System.UInt64 TreeInstanceId`
- `FIELD System.UInt64 UpdateId`
- `PROPERTY System.Boolean HasValidHeader`

---

### `AIBT.NativeTraceWriterV1`

- `METHOD AIBT.NativeTraceAppendResultV1 TryAppend(AIBT.NativeTraceRecordV1&)`
- `METHOD AIBT.NativeTraceAppendResultV1 TryAppend(AIBT.NativeTraceRecordV1&,Unity.Collections.NativeArray`1+ReadOnly<System.Byte>,System.UInt32)`

---

### `AIBT.NativeTreeBlackboardV1`

- `METHOD AIBT.Burst.BurstContextResult TryRead(AIBT.NativeProgramImageViewV2,AIBT.NativeInstanceArenaViewV2,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,,System.UInt64&)`
- `METHOD AIBT.Burst.BurstContextResult TryWrite(AIBT.NativeProgramImageViewV2,AIBT.NativeInstanceArenaViewV2,System.UInt32,System.UInt32,AIBT.NativeBlackboardTypeIdV2,Unity.Collections.NativeArray`1<>,System.Boolean&)`

---

### `AIBT.NativeUpdatePhaseV1`

- `FIELD AIBT.NativeUpdatePhaseV1 ApplyIntegrations`
- `FIELD AIBT.NativeUpdatePhaseV1 CollectInput`
- `FIELD AIBT.NativeUpdatePhaseV1 Execute`
- `FIELD AIBT.NativeUpdatePhaseV1 NormalizeInput`
- `FIELD AIBT.NativeUpdatePhaseV1 PublishCommands`
- `FIELD AIBT.NativeUpdatePhaseV1 PublishTraceAndMetrics`
- `FIELD AIBT.NativeUpdatePhaseV1 ReduceSharedWrites`
- `FIELD AIBT.NativeUpdatePhaseV1 SelectWork`
- `FIELD System.Byte value__`

---

### `AIBT.NativeUpdateStateV1`

- `FIELD System.UInt32 Phase`
- `FIELD System.UInt32 WorkCursor`
- `FIELD System.UInt64 UpdateId`

---

### `AIBT.NodeAbortReason`

- `FIELD AIBT.NodeAbortReason Explicit`
- `FIELD AIBT.NodeAbortReason HotReload`
- `FIELD AIBT.NodeAbortReason ObserverLowerPriority`
- `FIELD AIBT.NodeAbortReason ObserverSelf`
- `FIELD AIBT.NodeAbortReason Timeout`
- `FIELD AIBT.NodeAbortReason TreeStopped`
- `FIELD System.Byte value__`

---

### `AIBT.NodeExitReason`

- `FIELD AIBT.NodeExitReason Aborted`
- `FIELD AIBT.NodeExitReason Failure`
- `FIELD AIBT.NodeExitReason Success`
- `FIELD System.Byte value__`

---

### `AIBT.NodeId`

- `METHOD AIBT.NodeId Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.NodeId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.NodeId&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.String)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.String Value`

---

### `AIBT.NodeMemoryLifetime`

- `FIELD AIBT.NodeMemoryLifetime Activation`
- `FIELD AIBT.NodeMemoryLifetime Instance`
- `FIELD System.Byte value__`

---

### `AIBT.NodeStatus`

- `FIELD AIBT.NodeStatus Failure`
- `FIELD AIBT.NodeStatus Running`
- `FIELD AIBT.NodeStatus Success`
- `FIELD System.Byte value__`

---

### `AIBT.OperationId`

- `METHOD AIBT.OperationId Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.OperationId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.OperationId&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(AIBT.TreeInstanceId,AIBT.RuntimeNodeIndex,System.UInt32,System.UInt64)`
- `PROPERTY AIBT.RuntimeNodeIndex NodeIndex`
- `PROPERTY AIBT.TreeInstanceId TreeInstanceId`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 ActivationGeneration`
- `PROPERTY System.UInt64 Sequence`

---

### `AIBT.ProductionTreeHost`

Applies <c>ADR-P7-010</c> to production: a real component driving one compiled tree instance's lifecycle every frame during actual Play mode -- the missing piece every prior debugger/preview tool (<c>P3-009</c>, <c>P3-010</c>, <c>P3-011</c>, <c>P6-008</c>, <c>P6-012</c>) disclosed and worked around with a self-driven or benchmark-driven substitute. <para> Reuses <see cref="SchedulingPolicyDriver"/>'s own already-tested agent construction (<c>TryCreateAgents</c>) and disposal, but does not reuse its <c>TryRunImmediate</c>/ <c>TryRunBudgeted</c> driving loops -- those require every leaf's status to be supplied by the caller *in advance* (a benchmark-only shape; see their own doc comments), which cannot drive a real running tree whose leaves compute their own outcome. This host instead drives <see cref="NativeLifecycleMachineV1.TryAdvance"/>/<c>TryCompleteDispatch</c> directly, mirroring <c>SchedulingPolicyDriver.TryHandleStep</c>'s own exact per-step handling but resolving a real Tick's status through <see cref="DispatchLeaf"/> -- a delegate supplied by whoever builds this host, on demand, at the exact moment a dispatch is due. This keeps the host itself free of any <c>AIBT.Authoring</c> dependency (matching the ADR's own reasoning that a shipped game needs only <c>AIBT.Runtime</c> plus an already-compiled program): the real per-project leaf dispatch table (a generated <c>GenericNativeDispatchTranslatorV1</c>, <c>P7-009</c>) is resolved by the caller that constructs this host, never by the host itself. </para> <para> Scope, per the ADR's own decision 3: <c>Immediate</c>/<c>Budgeted</c> only. A single per-<see cref="GameObject"/> host cannot itself perform <c>BatchedJobsSameFrame</c>/ <c>PipelinedJobs</c>'s population-wide batch dispatch -- a population-level coordinator for those policies is explicit, disclosed future work, not attempted here. </para>

- `METHOD System.Boolean TryBootstrap(AIBT.CompiledProgram,AIBT.ProductionTreeHost+DispatchLeaf,AIBT.NativeTraceChannelCapacityV1,AIBT.NativeRuntimeFailureV1&)`
- `METHOD System.Void .ctor()`
- `PROPERTY AIBT.NativeTraceChannelOwnerV1 TraceChannelOwner`
- `PROPERTY System.Nullable`1<AIBT.NodeStatus> LastRootResult`
- `PROPERTY System.UInt64 TotalUpdates`

---

### `AIBT.QuaternionValue`

- `METHOD System.Boolean Equals(AIBT.QuaternionValue)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Single,System.Single,System.Single,System.Single)`
- `PROPERTY System.Single W`
- `PROPERTY System.Single X`
- `PROPERTY System.Single Y`
- `PROPERTY System.Single Z`

---

### `AIBT.ReferenceLeafContext`

- `METHOD System.Boolean TryReadBlackboard(System.UInt32,AIBT.BlackboardValue&)`
- `METHOD System.Boolean TryWriteBlackboard(System.UInt32,AIBT.BlackboardValue)`
- `PROPERTY System.ReadOnlySpan`1<System.Byte> Configuration`
- `PROPERTY System.Span`1<System.Byte> Memory`

---

### `AIBT.RegisteredUnmanagedTypeDescriptor`

- `METHOD System.Boolean Equals(AIBT.RegisteredUnmanagedTypeDescriptor)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.UInt64,System.UInt32,System.Int32,System.Int32,System.UInt64,System.UInt64,System.UInt32,System.UInt64)`
- `PROPERTY System.Boolean HasCanonicalSchema`
- `PROPERTY System.Boolean HasMigration`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.Int32 Alignment`
- `PROPERTY System.Int32 Size`
- `PROPERTY System.UInt32 MigrationSourceVersion`
- `PROPERTY System.UInt32 Version`
- `PROPERTY System.UInt64 CanonicalSchemaId`
- `PROPERTY System.UInt64 EqualityContractId`
- `PROPERTY System.UInt64 MigrationContractId`
- `PROPERTY System.UInt64 TypeId`

---

### `AIBT.Revision`

- `METHOD AIBT.Revision Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.Revision)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.Revision&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.UInt64)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt64 Value`

---

### `AIBT.RuntimeNodeIndex`

- `FIELD System.UInt32 InvalidValue`
- `METHOD AIBT.RuntimeNodeIndex Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.RuntimeNodeIndex)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.RuntimeNodeIndex&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.UInt32)`
- `PROPERTY AIBT.RuntimeNodeIndex Invalid`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt32 Value`

---

### `AIBT.StableHash`

- `FIELD System.UInt64 Fnv1A64OffsetBasis`
- `FIELD System.UInt64 Fnv1A64Prime`
- `METHOD System.String Sha256Hex(System.Byte[])`
- `METHOD System.String Sha256Hex(System.String)`
- `METHOD System.UInt64 Fnv1A64(System.Byte[])`
- `METHOD System.UInt64 Fnv1A64(System.String)`

---

### `AIBT.TreeId`

- `METHOD AIBT.TreeId Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.TreeId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.TreeId&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.String)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.String Value`

---

### `AIBT.TreeInstanceId`

- `METHOD AIBT.TreeInstanceId Parse(System.String)`
- `METHOD System.Boolean Equals(AIBT.TreeInstanceId)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryParse(System.String,AIBT.TreeInstanceId&)`
- `METHOD System.Int32 CompareTo(AIBT.TreeInstanceId)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `METHOD System.Void .ctor(System.UInt64)`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.UInt64 Value`
