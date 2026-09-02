# AIBT.Authoring -- public API reference (generated)

Source: live reflection over `AIBT.Authoring`'s own compiled public surface (`P7-014`). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

A type's own summary line is shown where an XML-doc `<summary>` exists in source; member-level doc-comment text is not yet correlated here (see this document's own generator comment for why) -- every member still gets its own full signature line regardless of whether prose exists for it.
109 public type(s).

---

### `AIBT.Authoring.AuthoringDiagnostic`

- `METHOD AIBT.Authoring.AuthoringDiagnostic CreateValidated(AIBT.DiagnosticCatalog,AIBT.Diagnostic,AIBT.Authoring.SuggestedDiagnosticOperation)`
- `METHOD System.Boolean Equals(AIBT.Authoring.AuthoringDiagnostic)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 CompareTo(AIBT.Authoring.AuthoringDiagnostic)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.Diagnostic,AIBT.Authoring.SuggestedDiagnosticOperation)`
- `PROPERTY AIBT.Authoring.SuggestedDiagnosticOperation SuggestedOperation`
- `PROPERTY AIBT.Diagnostic Diagnostic`

---

### `AIBT.Authoring.AuthoringDiagnosticCollection`

- `METHOD System.Collections.Generic.IEnumerator`1<AIBT.Authoring.AuthoringDiagnostic> GetEnumerator()`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.AuthoringDiagnostic>)`
- `PROPERTY AIBT.Authoring.AuthoringDiagnostic Item`
- `PROPERTY System.Int32 Count`

---

### `AIBT.Authoring.BlackboardDefaultValue`

- `METHOD AIBT.Authoring.BlackboardDefaultValue AgentId(AIBT.AgentId)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue AssetId(AIBT.AssetId)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Bool(System.Boolean)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue EntityId(AIBT.EntityId)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Enum32(System.String,System.Int32)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue FixedString128(System.String)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue FixedString32(System.String)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue FixedString512(System.String)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue FixedString64(System.String)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Float2(System.Single,System.Single)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Float3(System.Single,System.Single,System.Single)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Float32(System.Single)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Float64(System.Double)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Int32(System.Int32)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Int64(System.Int64)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue OperationId(AIBT.OperationId)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue Quaternion(System.Single,System.Single,System.Single,System.Single)`
- `METHOD AIBT.Authoring.BlackboardDefaultValue RegisteredSource(System.String,System.UInt32,System.String)`
- `METHOD System.Boolean TryGetRegisteredValue(AIBT.Authoring.SemanticObject&)`
- `METHOD System.Boolean TryGetRuntimeValue(AIBT.BlackboardValue&)`
- `PROPERTY AIBT.BlackboardValueType ValueType`
- `PROPERTY System.Boolean HasRuntimeValue`
- `PROPERTY System.Boolean IsCanonical`
- `PROPERTY System.String EnumContract`
- `PROPERTY System.String RegisteredTypeId`
- `PROPERTY System.String SourceText`
- `PROPERTY System.UInt32 RegisteredTypeVersion`

---

### `AIBT.Authoring.BlackboardDiagnosticCatalog`

- `METHOD AIBT.Diagnostic Create(AIBT.DiagnosticCode,System.String,AIBT.DiagnosticLocation,System.Collections.Generic.IEnumerable`1<AIBT.DiagnosticLocation>)`
- `PROPERTY AIBT.DiagnosticCatalog Catalog`

---

### `AIBT.Authoring.BlackboardDiagnosticCodes`

- `FIELD AIBT.DiagnosticCode DefaultTypeMismatch`
- `FIELD AIBT.DiagnosticCode InvalidDefaultValue`
- `FIELD AIBT.DiagnosticCode InvalidKeyId`
- `FIELD AIBT.DiagnosticCode InvalidKeyName`
- `FIELD AIBT.DiagnosticCode InvalidScope`
- `FIELD AIBT.DiagnosticCode InvalidType`
- `FIELD AIBT.DiagnosticCode MissingCanonicalSchema`
- `FIELD AIBT.DiagnosticCode NodeLocalTreeDeclaration`

---

### `AIBT.Authoring.BlackboardKeyDefinition`

- `METHOD System.Void .ctor(System.String,System.String,AIBT.Authoring.BlackboardTypeReference,AIBT.BlackboardScope,AIBT.Authoring.BlackboardDefaultValue,System.String)`
- `METHOD System.Void .ctor(System.String,System.String,AIBT.Authoring.BlackboardTypeReference,AIBT.BlackboardScope,AIBT.Authoring.BlackboardDefaultValue,System.String,AIBT.Authoring.BlackboardReductionKind)`
- `PROPERTY AIBT.Authoring.BlackboardDefaultValue DefaultValue`
- `PROPERTY AIBT.Authoring.BlackboardReductionKind Reduction`
- `PROPERTY AIBT.Authoring.BlackboardTypeReference Type`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY System.Boolean HasDefault`
- `PROPERTY System.String Description`
- `PROPERTY System.String Id`
- `PROPERTY System.String Name`

---

### `AIBT.Authoring.BlackboardNamingPolicy`

- `FIELD AIBT.Authoring.BlackboardNamingPolicy Any`
- `FIELD AIBT.Authoring.BlackboardNamingPolicy CamelCase`
- `FIELD AIBT.Authoring.BlackboardNamingPolicy PascalCase`
- `FIELD AIBT.Authoring.BlackboardNamingPolicy SnakeCase`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.BlackboardReductionKind`

- `FIELD AIBT.Authoring.BlackboardReductionKind All`
- `FIELD AIBT.Authoring.BlackboardReductionKind Any`
- `FIELD AIBT.Authoring.BlackboardReductionKind First`
- `FIELD AIBT.Authoring.BlackboardReductionKind Last`
- `FIELD AIBT.Authoring.BlackboardReductionKind Max`
- `FIELD AIBT.Authoring.BlackboardReductionKind Min`
- `FIELD AIBT.Authoring.BlackboardReductionKind None`
- `FIELD AIBT.Authoring.BlackboardReductionKind Sum`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.BlackboardSchemaValidator`

- `METHOD AIBT.DiagnosticCollection Validate(AIBT.Authoring.BlackboardKeyDefinition)`
- `METHOD AIBT.DiagnosticCollection Validate(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.BlackboardKeyDefinition>)`

---

### `AIBT.Authoring.BlackboardScopeContract`

- `METHOD System.Void .ctor(System.String,System.UInt32)`
- `PROPERTY System.String ContractId`
- `PROPERTY System.UInt32 ContractVersion`

---

### `AIBT.Authoring.BlackboardTypeReference`

- `METHOD AIBT.Authoring.BlackboardTypeReference BuiltIn(AIBT.BlackboardValueType)`
- `METHOD AIBT.Authoring.BlackboardTypeReference Enum32(System.String)`
- `METHOD AIBT.Authoring.BlackboardTypeReference Registered(System.String,AIBT.RegisteredUnmanagedTypeDescriptor)`
- `METHOD System.Boolean Equals(AIBT.Authoring.BlackboardTypeReference)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToString()`
- `PROPERTY AIBT.BlackboardTypeDescriptor RuntimeDescriptor`
- `PROPERTY AIBT.BlackboardValueType ValueType`
- `PROPERTY AIBT.RegisteredUnmanagedTypeDescriptor RegisteredDescriptor`
- `PROPERTY System.Boolean IsRegistered`
- `PROPERTY System.Boolean IsValid`
- `PROPERTY System.String CanonicalTypeId`
- `PROPERTY System.String EnumContract`
- `PROPERTY System.UInt64 EnumContractId`

---

### `AIBT.Authoring.BuiltInNodeManifests`

- `FIELD System.String CooldownTypeId`
- `FIELD System.String FailerTypeId`
- `FIELD System.String InverterTypeId`
- `FIELD System.String MemorySelectorTypeId`
- `FIELD System.String MemorySequenceTypeId`
- `FIELD System.String ParallelTypeId`
- `FIELD System.String ReactiveSelectorTypeId`
- `FIELD System.String ReactiveSequenceTypeId`
- `FIELD System.String RepeaterTypeId`
- `FIELD System.String SucceederTypeId`
- `FIELD System.String TimeoutTypeId`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeManifest> All`

---

### `AIBT.Authoring.CanonicalTreeJson`

- `METHOD AIBT.Authoring.TreeJsonReadResult Parse(System.Byte[],AIBT.Authoring.RegisteredBlackboardTypeCatalog,System.String)`
- `METHOD AIBT.Authoring.TreeJsonReadResult Parse(System.Byte[],System.String)`
- `METHOD AIBT.Authoring.TreeJsonReadResult Parse(System.String,AIBT.Authoring.RegisteredBlackboardTypeCatalog,System.String)`
- `METHOD AIBT.Authoring.TreeJsonReadResult Parse(System.String,System.String)`
- `METHOD AIBT.Authoring.TreeJsonWriteResult Serialize(AIBT.Authoring.TreeDocument)`
- `METHOD AIBT.Authoring.TreeJsonWriteResult Serialize(AIBT.Authoring.TreeDocument,AIBT.Authoring.RegisteredBlackboardTypeCatalog)`

---

### `AIBT.Authoring.DiagnosticJson`

- `METHOD System.Byte[] SerializeUtf8(AIBT.Authoring.AuthoringDiagnostic)`
- `METHOD System.String Serialize(AIBT.Authoring.AuthoringDiagnostic)`

---

### `AIBT.Authoring.DiagnosticOperationDescriptor`

- `METHOD System.Void .ctor(System.String,System.String,AIBT.Authoring.DiagnosticPayloadContract)`
- `PROPERTY AIBT.Authoring.DiagnosticPayloadContract PayloadContract`
- `PROPERTY System.String OperationId`
- `PROPERTY System.String PayloadType`

---

### `AIBT.Authoring.DiagnosticOperationPayload`

- `METHOD AIBT.Authoring.DiagnosticOperationPayload Array(AIBT.Authoring.DiagnosticOperationPayload[])`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload Array(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.DiagnosticOperationPayload>)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload From(System.Boolean)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload From(System.Double)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload From(System.Int32)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload From(System.Int64)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload From(System.Single)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload From(System.String)`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload Map(AIBT.Authoring.DiagnosticPayloadMember[])`
- `METHOD AIBT.Authoring.DiagnosticOperationPayload Map(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.DiagnosticPayloadMember>)`
- `METHOD System.Boolean Equals(AIBT.Authoring.DiagnosticOperationPayload)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 CompareTo(AIBT.Authoring.DiagnosticOperationPayload)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY AIBT.Authoring.DiagnosticOperationPayload Null`
- `PROPERTY AIBT.Authoring.DiagnosticPayloadKind Kind`
- `PROPERTY System.Boolean BooleanValue`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.DiagnosticOperationPayload> Items`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.DiagnosticPayloadMember> Members`
- `PROPERTY System.Double Float64Value`
- `PROPERTY System.Int32 Int32Value`
- `PROPERTY System.Int64 Int64Value`
- `PROPERTY System.Single Float32Value`
- `PROPERTY System.String StringValue`

---

### `AIBT.Authoring.DiagnosticOperationRegistry`

- `METHOD AIBT.Authoring.SuggestedDiagnosticOperation Create(System.String,System.String,AIBT.Authoring.DiagnosticOperationPayload)`
- `METHOD System.Boolean TryGet(System.String,AIBT.Authoring.DiagnosticOperationDescriptor&)`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.DiagnosticOperationDescriptor>)`
- `PROPERTY System.Int32 Count`

---

### `AIBT.Authoring.DiagnosticPayloadContract`

- `METHOD AIBT.Authoring.DiagnosticPayloadContract ArrayOf(AIBT.Authoring.DiagnosticPayloadContract)`
- `METHOD AIBT.Authoring.DiagnosticPayloadContract Map(AIBT.Authoring.DiagnosticPayloadPropertyContract[])`
- `METHOD AIBT.Authoring.DiagnosticPayloadContract Map(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.DiagnosticPayloadPropertyContract>)`
- `METHOD AIBT.Authoring.DiagnosticPayloadContract Scalar(AIBT.Authoring.DiagnosticPayloadKind)`
- `METHOD System.Void Validate(AIBT.Authoring.DiagnosticOperationPayload)`
- `PROPERTY AIBT.Authoring.DiagnosticPayloadContract ElementContract`
- `PROPERTY AIBT.Authoring.DiagnosticPayloadKind Kind`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.DiagnosticPayloadPropertyContract> Properties`

---

### `AIBT.Authoring.DiagnosticPayloadKind`

- `FIELD AIBT.Authoring.DiagnosticPayloadKind Array`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Boolean`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Float32`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Float64`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Int32`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Int64`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Map`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind Null`
- `FIELD AIBT.Authoring.DiagnosticPayloadKind String`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.DiagnosticPayloadMember`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.DiagnosticOperationPayload)`
- `PROPERTY AIBT.Authoring.DiagnosticOperationPayload Value`
- `PROPERTY System.String Name`

---

### `AIBT.Authoring.DiagnosticPayloadPropertyContract`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.DiagnosticPayloadContract,System.Boolean)`
- `PROPERTY AIBT.Authoring.DiagnosticPayloadContract Contract`
- `PROPERTY System.Boolean IsRequired`
- `PROPERTY System.String Name`

---

### `AIBT.Authoring.GeneratedAccessModeV2`

- `FIELD AIBT.Authoring.GeneratedAccessModeV2 ReadWrite`
- `FIELD AIBT.Authoring.GeneratedAccessModeV2 Read`
- `FIELD AIBT.Authoring.GeneratedAccessModeV2 Write`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.GeneratedBindingDescriptor`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.GeneratedBindingKind,AIBT.BlackboardScope,AIBT.Authoring.GeneratedPhaseCapability,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedTypeRecord>)`
- `PROPERTY AIBT.Authoring.GeneratedBindingKind Kind`
- `PROPERTY AIBT.Authoring.GeneratedPhaseCapability PhaseCapabilities`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY System.Boolean IsBlackboard`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedTypeRecord> Types`
- `PROPERTY System.String BindingId`
- `PROPERTY System.UInt32 Ordinal`
- `PROPERTY System.UInt64 NumericBindingId`

---

### `AIBT.Authoring.GeneratedBindingKind`

- `FIELD AIBT.Authoring.GeneratedBindingKind AsyncOperation`
- `FIELD AIBT.Authoring.GeneratedBindingKind BlackboardReadWrite`
- `FIELD AIBT.Authoring.GeneratedBindingKind BlackboardRead`
- `FIELD AIBT.Authoring.GeneratedBindingKind BlackboardWrite`
- `FIELD AIBT.Authoring.GeneratedBindingKind Completion`
- `FIELD AIBT.Authoring.GeneratedBindingKind EffectCommand`
- `FIELD AIBT.Authoring.GeneratedBindingKind SnapshotRead`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.GeneratedCompiledAccessRecordV2`

- `PROPERTY AIBT.Authoring.BlackboardReductionKind Reduction`
- `PROPERTY AIBT.Authoring.GeneratedAccessModeV2 Mode`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY System.UInt32 AccessOrdinal`
- `PROPERTY System.UInt32 NodeIndex`
- `PROPERTY System.UInt32 SlotIndex`

---

### `AIBT.Authoring.GeneratedCompiledProgramV2`

- `FIELD System.UInt32 AgentScopeCapability`
- `FIELD System.UInt32 SharedScopeCapability`
- `METHOD AIBT.NativeCompiledProgramHeaderV1 CreateNativeHeaderProjection()`
- `METHOD AIBT.NativeProgramBlackboardBindingV2 CreateNativeBlackboardBindingV2(AIBT.Authoring.RegisteredBlackboardTypeCatalog)`
- `METHOD System.Byte[] GetBytesCopy()`
- `METHOD System.Byte[] GetConfigBlobCopy()`
- `METHOD System.Byte[] GetDefaultValueBlobCopy()`
- `PROPERTY AIBT.Authoring.GeneratedScopeCompilationResult Scopes`
- `PROPERTY AIBT.CompiledHash ContentHash`
- `PROPERTY AIBT.CompiledProgram SemanticProgram`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedCompiledAccessRecordV2> Accesses`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedCompiledSlotRecordV2> Slots`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedWatchedSlotV2> WatchedSlots`
- `PROPERTY System.UInt32 CompiledFormatVersion`

---

### `AIBT.Authoring.GeneratedCompiledProgramV2Compiler`

- `METHOD AIBT.Authoring.GeneratedCompiledProgramV2Result Compile(AIBT.Authoring.TreeDocument,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedNodeDescriptor>,AIBT.Authoring.ReferenceCompilerOptions)`
- `METHOD AIBT.Authoring.GeneratedCompiledProgramV2Result Compile(AIBT.Authoring.TreeDocument,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedNodeDescriptor>,AIBT.Authoring.RegisteredBlackboardTypeCatalog,AIBT.Authoring.ReferenceCompilerOptions)`

---

### `AIBT.Authoring.GeneratedCompiledProgramV2Result`

- `PROPERTY AIBT.Authoring.GeneratedCompiledProgramV2 Program`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`

---

### `AIBT.Authoring.GeneratedCompiledSlotRecordV2`

- `PROPERTY AIBT.Authoring.GeneratedScopeSlot Slot`
- `PROPERTY AIBT.CompiledBlackboardAccessFlags AccessFlags`
- `PROPERTY System.UInt32 DefaultOffset`

---

### `AIBT.Authoring.GeneratedConfigurationPackResult`

- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Byte[] Bytes`

---

### `AIBT.Authoring.GeneratedConfigurationPacker`

- `METHOD AIBT.Authoring.GeneratedConfigurationPackResult Pack(AIBT.Authoring.GeneratedNodeDescriptor,AIBT.Authoring.SemanticObject,System.Collections.Generic.IReadOnlyDictionary`2<System.String,System.UInt32>,System.String,AIBT.NodeId)`

---

### `AIBT.Authoring.GeneratedFieldEncoding`

- `FIELD AIBT.Authoring.GeneratedFieldEncoding Bool8`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding FixedBytes`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Float32BitsLE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Float64BitsLE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding GeneratedHandle`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Int16LE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Int32LE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Int64LE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Int8`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding Registered`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding UInt16LE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding UInt32LE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding UInt64LE`
- `FIELD AIBT.Authoring.GeneratedFieldEncoding UInt8`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.GeneratedNodeAccessRecord`

- `PROPERTY AIBT.Authoring.BlackboardReductionKind Reduction`
- `PROPERTY AIBT.Authoring.GeneratedBindingDescriptor Binding`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.NodeId NodeId`
- `PROPERTY System.UInt32 AccessOrdinal`
- `PROPERTY System.UInt32 ScopeSlot`

---

### `AIBT.Authoring.GeneratedNodeDescriptor`

- `METHOD System.Void .ctor(AIBT.Authoring.NodeManifest,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedStorageField>,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedStorageField>,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedBindingDescriptor>)`
- `METHOD System.Void .ctor(AIBT.Authoring.NodeManifest,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedStorageField>,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedStorageField>,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedBindingDescriptor>,System.Byte,System.Boolean)`
- `PROPERTY AIBT.Authoring.NodeManifest Manifest`
- `PROPERTY AIBT.CompiledHash AccessLayoutHash`
- `PROPERTY AIBT.CompiledHash ConfigurationLayoutHash`
- `PROPERTY AIBT.CompiledHash MemoryLayoutHash`
- `PROPERTY System.Boolean HasRandomStream`
- `PROPERTY System.Byte CallbackCapabilities`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedBindingDescriptor> Bindings`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedStorageField> Configuration`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedStorageField> Memory`

---

### `AIBT.Authoring.GeneratedNodeRegistry`

- `METHOD AIBT.Authoring.NodeRegistryBuildResult Build(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedNodeDescriptor>,System.Boolean)`

---

### `AIBT.Authoring.GeneratedPhaseCapability`

- `FIELD AIBT.Authoring.GeneratedPhaseCapability Cancel`
- `FIELD AIBT.Authoring.GeneratedPhaseCapability Completion`
- `FIELD AIBT.Authoring.GeneratedPhaseCapability Execute`
- `FIELD AIBT.Authoring.GeneratedPhaseCapability None`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.GeneratedScopeCompilationInput`

- `METHOD System.Void .ctor(AIBT.Authoring.TreeDocument,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedNodeDescriptor>,System.String)`
- `PROPERTY AIBT.Authoring.TreeDocument Document`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedNodeDescriptor> GeneratedNodes`
- `PROPERTY System.String DocumentId`

---

### `AIBT.Authoring.GeneratedScopeCompilationResult`

- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<AIBT.NodeId,System.Byte[]> Configurations`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedNodeAccessRecord> Accesses`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedScopeDescriptor> Descriptors`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedScopeSlot> Slots`

---

### `AIBT.Authoring.GeneratedScopeCompilationSetResult`

- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedScopeCompilationResult> Results`

---

### `AIBT.Authoring.GeneratedScopeCompiler`

- `METHOD AIBT.Authoring.GeneratedScopeCompilationResult Compile(AIBT.Authoring.TreeDocument,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedNodeDescriptor>,AIBT.Authoring.RegisteredBlackboardTypeCatalog,System.String)`
- `METHOD AIBT.Authoring.GeneratedScopeCompilationResult Compile(AIBT.Authoring.TreeDocument,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedNodeDescriptor>,System.String)`
- `METHOD AIBT.Authoring.GeneratedScopeCompilationSetResult CompileSet(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedScopeCompilationInput>)`

---

### `AIBT.Authoring.GeneratedScopeDescriptor`

- `METHOD System.Byte[] GetRawLayoutCopy()`
- `METHOD System.Byte[] GetSchemaBytesCopy()`
- `PROPERTY AIBT.Authoring.BlackboardScopeContract Contract`
- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY AIBT.CompiledHash LayoutHash`
- `PROPERTY AIBT.CompiledHash SchemaHash`
- `PROPERTY System.UInt32 FirstSlot`
- `PROPERTY System.UInt32 SlotCount`

---

### `AIBT.Authoring.GeneratedScopeSlot`

- `PROPERTY AIBT.Authoring.BlackboardKeyDefinition Key`
- `PROPERTY System.Byte[] DefaultBytes`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 SlotIndex`

---

### `AIBT.Authoring.GeneratedShardMetadataArtifact`

- `PROPERTY AIBT.Authoring.RegisteredBlackboardTypeCatalog RegisteredTypes`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedNodeDescriptor> Nodes`
- `PROPERTY System.String ShardId`
- `PROPERTY System.UInt32 ShardVersion`

---

### `AIBT.Authoring.GeneratedShardMetadataMaterializer`

- `METHOD AIBT.Authoring.GeneratedShardMetadataArtifact MaterializeArtifact(System.String,System.String,System.String,System.String)`
- `METHOD AIBT.Authoring.GeneratedShardMetadataArtifact MaterializeArtifact(System.String,System.UInt32,System.String,System.String,System.String,System.String)`
- `METHOD System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedNodeDescriptor> Materialize(System.String,System.String,System.String,System.String)`

---

### `AIBT.Authoring.GeneratedStorageField`

- `METHOD System.Void .ctor(System.String,System.String,System.UInt32,System.UInt32,System.Byte,AIBT.Authoring.GeneratedFieldEncoding,System.String,System.String,AIBT.RegisteredUnmanagedTypeDescriptor)`
- `PROPERTY AIBT.Authoring.GeneratedFieldEncoding Encoding`
- `PROPERTY AIBT.RegisteredUnmanagedTypeDescriptor RegisteredDescriptor`
- `PROPERTY System.Byte Alignment`
- `PROPERTY System.String BindingId`
- `PROPERTY System.String FieldId`
- `PROPERTY System.String RegisteredSchemaHash`
- `PROPERTY System.String ValueTypeId`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`
- `PROPERTY System.UInt32 ValueTypeVersion`
- `PROPERTY System.UInt64 NumericFieldId`

---

### `AIBT.Authoring.GeneratedTypeRecord`

- `METHOD System.Void .ctor(AIBT.Authoring.GeneratedTypeRole,System.String,System.UInt32,System.String,AIBT.RegisteredUnmanagedTypeDescriptor)`
- `PROPERTY AIBT.Authoring.GeneratedTypeRole Role`
- `PROPERTY AIBT.RegisteredUnmanagedTypeDescriptor RegisteredDescriptor`
- `PROPERTY System.String CanonicalTypeId`
- `PROPERTY System.String SchemaHash`
- `PROPERTY System.UInt32 Version`
- `PROPERTY System.UInt64 NumericTypeId`

---

### `AIBT.Authoring.GeneratedTypeRole`

- `FIELD AIBT.Authoring.GeneratedTypeRole AsyncCancelPayload`
- `FIELD AIBT.Authoring.GeneratedTypeRole AsyncStartPayload`
- `FIELD AIBT.Authoring.GeneratedTypeRole CompletionPayload`
- `FIELD AIBT.Authoring.GeneratedTypeRole EffectPayload`
- `FIELD AIBT.Authoring.GeneratedTypeRole Value`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.GeneratedWatchedSlotV2`

- `PROPERTY AIBT.BlackboardScope Scope`
- `PROPERTY System.UInt32 SlotIndex`

---

### `AIBT.Authoring.HotReloadPreviewDriver`

Drives a hot-reloadable instance of the accepted Phase 1 managed reference executor (<c>ReferenceExecutionMachine</c>, internal to <c>AIBT.Runtime</c>) for the Editor workflow (<c>P5-008</c>), so <c>AIBT.Editor</c> (no internals visibility into <c>AIBT.Runtime</c>) can trigger and observe a real reload without crossing the assembly boundary itself. This type owns no reload semantics of its own: every classification and state transition is a direct call into <c>HotReloadCompatibilityClassifier</c>/<c>HotReloadStateMigration</c> (<c>P5-003</c>/<c>P5-005</c>/<c>P5-006</c>), mirroring exactly how <see cref="ReferencePreviewDriver"/> crosses the same boundary for stepping. <para> Same fixed Phase 1 fixture/built-in node-behavior set as <see cref="ReferencePreviewDriver"/> (<see cref="ReferencePreviewFixtureEnvironment"/>) -- and the same known limitation: AIBT ships no production per-project leaf-behavior registration mechanism yet. </para>

- `METHOD AIBT.Authoring.NodeRegistry CreatePreviewNodeRegistry()`
- `METHOD System.Boolean TryCreate(AIBT.Authoring.TreeDocument,System.String,AIBT.Authoring.HotReloadPreviewDriver&,AIBT.DiagnosticCollection&)`
- `METHOD System.Boolean TryReload(AIBT.Authoring.TreeDocument,System.String,AIBT.Authoring.HotReloadPreviewOutcome&,AIBT.DiagnosticCollection&)`
- `METHOD System.Nullable`1<AIBT.NodeStatus> RunOneTick(System.Int64)`
- `PROPERTY System.Nullable`1<AIBT.NodeStatus> TerminalResult`
- `PROPERTY System.UInt32 ActiveNodeCount`

---

### `AIBT.Authoring.HotReloadPreviewOutcome`

What one <see cref="HotReloadPreviewDriver.TryReload"/> call actually did -- the public, explainable shape <c>AIBT.Editor</c> (no internals visibility into <c>AIBT.Runtime</c>) displays to the user, per <c>Documentation~/hot-reload.md</c>'s "Editor workflows" section.

- `PROPERTY System.Boolean FellBackToFullRestart`
- `PROPERTY System.Boolean RequiredFullRestart`
- `PROPERTY System.Collections.Generic.IReadOnlyCollection`1<AIBT.NodeId> RestartSubtreeRootNodeIds`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<AIBT.NodeId,System.String> NodeVerdicts`
- `PROPERTY System.UInt32 DroppedNodeCount`
- `PROPERTY System.UInt32 MigratedNodeCount`
- `PROPERTY System.UInt32 ResetNodeCount`

---

### `AIBT.Authoring.IReferenceLeafBehaviorProvider`

- `METHOD AIBT.IReferenceLeafBehavior CreateBehavior()`
- `PROPERTY AIBT.Authoring.NodeManifest Manifest`

---

### `AIBT.Authoring.NodeAccessContract`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.NodeAccessMode)`
- `PROPERTY AIBT.Authoring.NodeAccessMode Mode`
- `PROPERTY System.String Key`

---

### `AIBT.Authoring.NodeAccessMode`

- `FIELD AIBT.Authoring.NodeAccessMode Read`
- `FIELD AIBT.Authoring.NodeAccessMode Write`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeBehaviorKind`

- `FIELD AIBT.Authoring.NodeBehaviorKind Action`
- `FIELD AIBT.Authoring.NodeBehaviorKind Composite`
- `FIELD AIBT.Authoring.NodeBehaviorKind Condition`
- `FIELD AIBT.Authoring.NodeBehaviorKind Decorator`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeBindingMap`

- `METHOD System.Boolean Equals(AIBT.Authoring.NodeBindingMap)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.String,System.String>>)`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<System.String,System.String> Values`

---

### `AIBT.Authoring.NodeCancellationMode`

- `FIELD AIBT.Authoring.NodeCancellationMode AbortOnly`
- `FIELD AIBT.Authoring.NodeCancellationMode Command`
- `FIELD AIBT.Authoring.NodeCancellationMode NotApplicable`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeCatalogQuery`

- `METHOD System.Boolean TryGetContract(System.String,Newtonsoft.Json.Linq.JObject&)`
- `METHOD System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeRegistryEntry> Page(System.Int32,System.Int32)`
- `METHOD System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeRegistryEntry> Search(System.String)`
- `METHOD System.String SerializeCatalog(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeRegistryEntry>)`
- `METHOD System.Void .ctor(AIBT.Authoring.NodeRegistry)`
- `PROPERTY AIBT.Authoring.NodeRegistry Registry`
- `PROPERTY System.Int32 Count`

---

### `AIBT.Authoring.NodeChildPolicy`

- `METHOD System.Void .ctor(System.UInt32,System.Nullable`1<System.UInt32>,System.Boolean)`
- `PROPERTY System.Boolean Ordered`
- `PROPERTY System.Nullable`1<System.UInt32> Maximum`
- `PROPERTY System.UInt32 Minimum`

---

### `AIBT.Authoring.NodeConfigurationDescriptor`

- `METHOD System.Void .ctor(System.UInt32,System.Byte,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeConfigurationField>)`
- `PROPERTY System.Byte Alignment`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeConfigurationField> Fields`
- `PROPERTY System.UInt32 Size`

---

### `AIBT.Authoring.NodeConfigurationField`

- `METHOD System.Void .ctor(System.String,System.UInt32,System.UInt32,System.Byte)`
- `METHOD System.Void .ctor(System.String,System.UInt32,System.UInt32,System.Byte,System.Boolean)`
- `PROPERTY System.Boolean IsGeneratedHandle`
- `PROPERTY System.Byte Alignment`
- `PROPERTY System.String ParameterName`
- `PROPERTY System.UInt32 Offset`
- `PROPERTY System.UInt32 Size`

---

### `AIBT.Authoring.NodeCostHint`

- `FIELD AIBT.Authoring.NodeCostHint High`
- `FIELD AIBT.Authoring.NodeCostHint Low`
- `FIELD AIBT.Authoring.NodeCostHint Medium`
- `FIELD AIBT.Authoring.NodeCostHint Trivial`
- `FIELD AIBT.Authoring.NodeCostHint Variable`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeDocument`

- `METHOD AIBT.Authoring.NodeDocument CreateVersion2(AIBT.NodeId,System.String,System.Int32,AIBT.Authoring.NodeBindingMap,System.Collections.Generic.IEnumerable`1<AIBT.NodeId>,AIBT.Authoring.SemanticObject,AIBT.Authoring.NodeObserver,System.String,System.String,AIBT.Authoring.TagSet)`
- `METHOD AIBT.Authoring.NodeDocument WithBindings(AIBT.Authoring.NodeBindingMap)`
- `METHOD AIBT.Authoring.NodeDocument WithChildren(System.Collections.Generic.IEnumerable`1<AIBT.NodeId>)`
- `METHOD AIBT.Authoring.NodeDocument WithDetails(System.String,System.String,AIBT.Authoring.TagSet)`
- `METHOD AIBT.Authoring.NodeDocument WithObserver(AIBT.Authoring.NodeObserver)`
- `METHOD AIBT.Authoring.NodeDocument WithParameters(AIBT.Authoring.SemanticObject)`
- `METHOD AIBT.Authoring.NodeDocument WithType(System.String,System.Int32)`
- `METHOD System.Boolean Equals(AIBT.Authoring.NodeDocument)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(AIBT.NodeId,System.String,System.Int32,System.Collections.Generic.IEnumerable`1<AIBT.NodeId>,AIBT.Authoring.SemanticObject,AIBT.Authoring.NodeObserver,System.String,System.String,AIBT.Authoring.TagSet)`
- `METHOD System.Void .ctor(AIBT.NodeId,System.String,System.Int32,System.Collections.Generic.IEnumerable`1<AIBT.NodeId>,AIBT.Authoring.SemanticObject,AIBT.Authoring.NodeObserver,System.String,System.String,AIBT.Authoring.TagSet,AIBT.Authoring.NodeBindingMap)`
- `PROPERTY AIBT.Authoring.NodeBindingMap Bindings`
- `PROPERTY AIBT.Authoring.NodeObserver Observer`
- `PROPERTY AIBT.Authoring.SemanticObject Parameters`
- `PROPERTY AIBT.Authoring.TagSet Tags`
- `PROPERTY AIBT.NodeId Id`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NodeId> Children`
- `PROPERTY System.Int32 TypeVersion`
- `PROPERTY System.String Description`
- `PROPERTY System.String DisplayName`
- `PROPERTY System.String TypeId`

---

### `AIBT.Authoring.NodeExecutionDomain`

- `FIELD AIBT.Authoring.NodeExecutionDomain Burst`
- `FIELD AIBT.Authoring.NodeExecutionDomain MainThread`
- `FIELD AIBT.Authoring.NodeExecutionDomain Managed`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeManifest`

- `METHOD System.Void .ctor(System.String,System.UInt32,System.String,System.String,AIBT.Authoring.NodeBehaviorKind,System.String,System.String,AIBT.Authoring.NodeExecutionDomain,System.Boolean,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeParameterContract>,AIBT.Authoring.NodeChildPolicy,System.Collections.Generic.IEnumerable`1<System.String>,System.Collections.Generic.IEnumerable`1<System.String>,System.Collections.Generic.IEnumerable`1<System.String>,System.Collections.Generic.IEnumerable`1<AIBT.NodeStatus>,AIBT.Authoring.NodeMemoryDescriptor,AIBT.Authoring.NodeConfigurationDescriptor,AIBT.Authoring.NodeCancellationMode,AIBT.Authoring.NodeCostHint,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeManifestExample>,System.Boolean,System.String)`
- `PROPERTY AIBT.Authoring.NodeBehaviorKind Kind`
- `PROPERTY AIBT.Authoring.NodeCancellationMode Cancellation`
- `PROPERTY AIBT.Authoring.NodeChildPolicy ChildPolicy`
- `PROPERTY AIBT.Authoring.NodeConfigurationDescriptor Configuration`
- `PROPERTY AIBT.Authoring.NodeCostHint CostHint`
- `PROPERTY AIBT.Authoring.NodeExecutionDomain ExecutionDomain`
- `PROPERTY AIBT.Authoring.NodeMemoryDescriptor Memory`
- `PROPERTY System.Boolean Deprecated`
- `PROPERTY System.Boolean Deterministic`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeAccessContract> Accesses`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeManifestExample> Examples`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeParameterContract> Parameters`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NodeStatus> PossibleStatuses`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> Reads`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> SideEffects`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> Writes`
- `PROPERTY System.String Category`
- `PROPERTY System.String ReplacementTypeId`
- `PROPERTY System.String Summary`
- `PROPERTY System.String TypeId`
- `PROPERTY System.String WhenNotToUse`
- `PROPERTY System.String WhenToUse`
- `PROPERTY System.UInt32 Version`

---

### `AIBT.Authoring.NodeManifestExample`

- `METHOD Newtonsoft.Json.Linq.JObject GetParametersCopy()`
- `METHOD System.Void .ctor(System.String,System.String,System.String)`
- `PROPERTY System.String ExpectedBehavior`
- `PROPERTY System.String Title`

---

### `AIBT.Authoring.NodeManifestSource`

- `FIELD AIBT.Authoring.NodeManifestSource BuiltIn`
- `FIELD AIBT.Authoring.NodeManifestSource TestFixture`
- `FIELD AIBT.Authoring.NodeManifestSource UserExtension`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeMemoryDescriptor`

- `METHOD System.Void .ctor(System.UInt32,System.Byte,AIBT.NodeMemoryLifetime)`
- `PROPERTY AIBT.NodeMemoryLifetime Lifetime`
- `PROPERTY System.Byte Alignment`
- `PROPERTY System.UInt32 Size`

---

### `AIBT.Authoring.NodeObserver`

- `METHOD System.Boolean Equals(AIBT.Authoring.NodeObserver)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.String,System.Collections.Generic.IEnumerable`1<System.String>)`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> WatchedKeys`
- `PROPERTY System.String Mode`

---

### `AIBT.Authoring.NodeParameterCondition`

- `METHOD System.Void .ctor(System.String,System.String)`
- `PROPERTY System.String ParameterName`
- `PROPERTY System.String RequiredValue`

---

### `AIBT.Authoring.NodeParameterContract`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.NodeParameterType,System.Boolean,System.Nullable`1<System.UInt64>,System.Collections.Generic.IEnumerable`1<System.String>,AIBT.Authoring.NodeParameterCondition,AIBT.Authoring.NodeParameterCondition)`
- `PROPERTY AIBT.Authoring.NodeParameterCondition ForbiddenUnless`
- `PROPERTY AIBT.Authoring.NodeParameterCondition RequiredWhen`
- `PROPERTY AIBT.Authoring.NodeParameterType Type`
- `PROPERTY System.Boolean Required`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> AllowedValues`
- `PROPERTY System.Nullable`1<System.UInt64> Minimum`
- `PROPERTY System.String Name`

---

### `AIBT.Authoring.NodeParameterType`

- `FIELD AIBT.Authoring.NodeParameterType Boolean`
- `FIELD AIBT.Authoring.NodeParameterType StringEnum`
- `FIELD AIBT.Authoring.NodeParameterType UInt32`
- `FIELD AIBT.Authoring.NodeParameterType UInt64`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.NodeRegistry`

- `METHOD System.Boolean TryGet(System.String,AIBT.Authoring.NodeRegistryEntry&)`
- `METHOD System.Boolean TryGet(System.UInt64,AIBT.Authoring.NodeRegistryEntry&)`
- `METHOD System.Collections.Generic.IEnumerator`1<AIBT.Authoring.NodeRegistryEntry> GetEnumerator()`
- `PROPERTY AIBT.Authoring.NodeRegistryCapabilityFlags Capabilities`
- `PROPERTY AIBT.Authoring.NodeRegistryEntry Item`
- `PROPERTY System.Int32 Count`
- `PROPERTY System.String Hash`

---

### `AIBT.Authoring.NodeRegistryBuildResult`

- `PROPERTY AIBT.Authoring.NodeRegistry Registry`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`

---

### `AIBT.Authoring.NodeRegistryBuilder`

- `METHOD AIBT.Authoring.NodeRegistryBuildResult Build()`
- `METHOD AIBT.Authoring.NodeRegistryBuilder AddProjectExtension(AIBT.Authoring.NodeManifest,AIBT.IReferenceLeafBehavior)`
- `METHOD AIBT.Authoring.NodeRegistryBuilder AddUserExtension(AIBT.Authoring.NodeManifest)`
- `METHOD AIBT.Authoring.NodeRegistryBuilder CreateWithBuiltIns()`
- `METHOD System.Boolean TryGetProjectLeafBehavior(System.String,AIBT.IReferenceLeafBehavior&)`
- `METHOD System.Void .ctor()`

---

### `AIBT.Authoring.NodeRegistryCapabilityFlags`

- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags Burst`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags MainThread`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags Managed`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags NonDeterministic`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags None`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags ReferenceHandlerBindings`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags SideEffects`
- `FIELD AIBT.Authoring.NodeRegistryCapabilityFlags UserExtensions`
- `FIELD System.UInt16 value__`

---

### `AIBT.Authoring.NodeRegistryEntry`

- `PROPERTY AIBT.Authoring.NodeManifest Manifest`
- `PROPERTY AIBT.Authoring.NodeManifestSource Source`
- `PROPERTY System.Boolean HasReferenceHandlerBinding`
- `PROPERTY System.UInt64 NumericTypeId`

---

### `AIBT.Authoring.ProjectManifestQuery`

- `METHOD Newtonsoft.Json.Linq.JObject Build(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.TreeDocument>)`
- `METHOD System.Void .ctor(AIBT.Authoring.NodeRegistry,AIBT.Authoring.ProjectPolicySnapshot)`

---

### `AIBT.Authoring.ProjectPolicySnapshot`

- `METHOD System.Boolean TryParse(System.String,AIBT.Authoring.ProjectPolicySnapshot&,AIBT.Diagnostic&)`
- `METHOD System.Boolean TryReadFile(System.String,AIBT.Authoring.ProjectPolicySnapshot&,AIBT.Diagnostic&)`
- `PROPERTY System.Boolean AllowMainThreadNodes`
- `PROPERTY System.Boolean AllowManagedNodes`
- `PROPERTY System.Boolean AllowSideEffects`
- `PROPERTY System.Boolean ForbidUnboundedRepeaters`
- `PROPERTY System.Boolean RequireDeterministicNodes`
- `PROPERTY System.Boolean RequireEventDrivenServices`
- `PROPERTY System.Boolean RequireNodeDescriptions`
- `PROPERTY System.Boolean RequireTreeDescription`
- `PROPERTY System.Boolean SupportsAgentScope`
- `PROPERTY System.Boolean SupportsSharedScope`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> ForbiddenNodeTypes`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> WarningsAsErrors`
- `PROPERTY System.Nullable`1<System.Double> MaxEstimatedCost`
- `PROPERTY System.Nullable`1<System.Int32> MaxNodesPerTree`
- `PROPERTY System.Nullable`1<System.Int32> MaxTreeDepth`
- `PROPERTY System.String BlackboardNaming`
- `PROPERTY System.String UnreachableNodes`

---

### `AIBT.Authoring.ReferenceCompilationPolicy`

- `METHOD AIBT.CompiledHash ComputeHash()`
- `METHOD System.Byte[] ToCanonicalUtf8()`
- `METHOD System.Void .ctor(System.Nullable`1<System.Int32>,System.Nullable`1<System.Int32>,System.Boolean,System.Boolean,System.Boolean,System.Boolean,AIBT.Authoring.BlackboardNamingPolicy,System.Collections.Generic.IEnumerable`1<System.String>,System.Boolean,System.Boolean,AIBT.Authoring.UnreachableNodePolicy,System.Boolean,System.Boolean,System.Collections.Generic.IEnumerable`1<AIBT.DiagnosticCode>,System.Nullable`1<System.Double>,System.Boolean,System.Boolean)`
- `PROPERTY AIBT.Authoring.BlackboardNamingPolicy BlackboardNaming`
- `PROPERTY AIBT.Authoring.ReferenceCompilationPolicy Phase1`
- `PROPERTY AIBT.Authoring.UnreachableNodePolicy UnreachableNodes`
- `PROPERTY System.Boolean AllowMainThreadNodes`
- `PROPERTY System.Boolean AllowManagedNodes`
- `PROPERTY System.Boolean AllowSideEffects`
- `PROPERTY System.Boolean ForbidUnboundedRepeaters`
- `PROPERTY System.Boolean RequireDeterministicNodes`
- `PROPERTY System.Boolean RequireEventDrivenServices`
- `PROPERTY System.Boolean RequireNodeDescriptions`
- `PROPERTY System.Boolean RequireTreeDescription`
- `PROPERTY System.Boolean SupportsAgentScope`
- `PROPERTY System.Boolean SupportsSharedScope`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.DiagnosticCode> WarningsAsErrors`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> ForbiddenNodeTypes`
- `PROPERTY System.Nullable`1<System.Double> MaxEstimatedCost`
- `PROPERTY System.Nullable`1<System.Int32> MaxNodesPerTree`
- `PROPERTY System.Nullable`1<System.Int32> MaxTreeDepth`

---

### `AIBT.Authoring.ReferenceCompilationPolicyCodec`

- `FIELD System.String Format`
- `FIELD System.UInt32 FormatVersion`
- `METHOD System.Boolean IsExactCanonicalEncoding(AIBT.Authoring.ReferenceCompilationPolicy,System.Byte[])`
- `METHOD System.Byte[] Serialize(AIBT.Authoring.ReferenceCompilationPolicy)`

---

### `AIBT.Authoring.ReferenceCompilationResult`

- `PROPERTY AIBT.CompiledProgram Program`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`

---

### `AIBT.Authoring.ReferenceCompiler`

- `FIELD System.UInt32 CompiledFormatVersion`
- `FIELD System.UInt32 ExecutionSemanticsVersion`
- `METHOD AIBT.Authoring.ReferenceCompilationResult Compile(AIBT.Authoring.TreeDocument,AIBT.Authoring.NodeRegistry,AIBT.Authoring.ReferenceCompilerOptions)`

---

### `AIBT.Authoring.ReferenceCompilerDiagnosticCodes`

- `FIELD AIBT.DiagnosticCode ConfigurationPacking`
- `FIELD AIBT.DiagnosticCode DefaultValuePacking`
- `FIELD AIBT.DiagnosticCode GeneratedLayoutMismatch`
- `FIELD AIBT.DiagnosticCode InvalidCompiledStructure`
- `FIELD AIBT.DiagnosticCode InvalidGeneratedBinding`
- `FIELD AIBT.DiagnosticCode InvalidOptions`
- `FIELD AIBT.DiagnosticCode InvalidReduction`
- `FIELD AIBT.DiagnosticCode LayoutOverflow`
- `FIELD AIBT.DiagnosticCode MissingScopeContract`
- `FIELD AIBT.DiagnosticCode PolicyHashMismatch`
- `FIELD AIBT.DiagnosticCode ScopeContractMismatch`
- `FIELD AIBT.DiagnosticCode SharedReductionMissing`
- `FIELD AIBT.DiagnosticCode StableIdentityCollision`
- `FIELD AIBT.DiagnosticCode UnsupportedCapability`
- `FIELD AIBT.DiagnosticCode UnsupportedReduction`

---

### `AIBT.Authoring.ReferenceCompilerOptions`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.ReferenceCompilationPolicy,AIBT.CompiledCompilerVersion,System.Boolean)`
- `PROPERTY AIBT.Authoring.ReferenceCompilationPolicy Policy`
- `PROPERTY AIBT.CompiledCompilerVersion CompilerVersion`
- `PROPERTY System.Boolean StripDisplayMetadata`
- `PROPERTY System.String SourceId`

---

### `AIBT.Authoring.ReferencePreviewBlackboardValue`

One blackboard slot as observed at an inspection boundary.

- `METHOD System.Void .ctor(System.String,System.UInt64,System.Boolean,AIBT.BlackboardValue,System.UInt64,System.UInt32)`
- `PROPERTY AIBT.BlackboardValue BuiltInValue`
- `PROPERTY System.Boolean IsRegistered`
- `PROPERTY System.String Key`
- `PROPERTY System.UInt32 RegisteredTypeVersion`
- `PROPERTY System.UInt64 RegisteredTypeId`
- `PROPERTY System.UInt64 Version`

---

### `AIBT.Authoring.ReferencePreviewDriver`

Drives the accepted Phase 1 managed reference executor (<c>ReferenceExecutionMachine</c>, internal to <c>AIBT.Runtime</c>) for editor preview/stepping, so in-editor stepping semantics cannot drift from the accepted headless oracle. This type owns no execution semantics of its own: every state transition is a direct call into the existing machine's public <c>BeginUpdate</c>/<c>AdvanceOneStep</c>/<c>Restart</c> API; this class only translates its internal contracts to a public shape that <c>AIBT.Editor</c> (which has no internals visibility into <c>AIBT.Runtime</c>) can consume. <para> The executable node-behavior set is fixed to the already-shipped Phase 1 fixture/built-in registries (<c>ReferenceLeafRegistry.CreatePhase1Fixtures()</c> and the sibling <c>CreatePhase1BuiltIns()</c> composite/decorator/parallel registries already used by the headless behavior-case runner). AIBT ships no production per-project leaf-behavior registration mechanism yet, so only trees built from built-in composites/decorators and the <c>aibt.test.success</c>/<c>aibt.test.failure</c>/<c>aibt.test.running</c> fixture leaves can be previewed; this is a known limitation, not a silent weakening (see the P3-009 evidence). </para>

- `METHOD AIBT.Authoring.NodeRegistry CreatePreviewNodeRegistry()`
- `METHOD AIBT.Authoring.ReferencePreviewEnvelope BeginTick(System.Int64)`
- `METHOD AIBT.Authoring.ReferencePreviewEnvelope Restart()`
- `METHOD AIBT.Authoring.ReferencePreviewEnvelope RunTick(System.Collections.Generic.ISet`1<AIBT.NodeId>,System.Int64)`
- `METHOD AIBT.Authoring.ReferencePreviewEnvelope StepAtomic()`
- `METHOD AIBT.Authoring.ReferencePreviewInspection CaptureInspection()`
- `METHOD System.Boolean TryCreate(AIBT.Authoring.TreeDocument,System.String,AIBT.Authoring.ReferencePreviewDriver&,AIBT.DiagnosticCollection&)`
- `PROPERTY System.Boolean HasOpenTick`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NodeId> ActiveNodeIds`
- `PROPERTY System.Nullable`1<AIBT.NodeStatus> TerminalResult`

---

### `AIBT.Authoring.ReferencePreviewEnvelope`

Mirrors <c>ReferenceExecutionEnvelope</c> (internal, Runtime) for a single driver call.

- `METHOD System.Void .ctor(AIBT.Authoring.ReferencePreviewProgress,System.Nullable`1<AIBT.NodeStatus>,System.UInt64,System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.ReferencePreviewTraceEvent>)`
- `PROPERTY AIBT.Authoring.ReferencePreviewProgress Progress`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.ReferencePreviewTraceEvent> TraceEvents`
- `PROPERTY System.Nullable`1<AIBT.NodeStatus> RootResult`
- `PROPERTY System.UInt64 Steps`

---

### `AIBT.Authoring.ReferencePreviewInspection`

A stable-boundary snapshot of execution state, mirroring the internal <c>ReferenceExecutionInspection</c> plus the active node identities the internal type omits.

- `METHOD System.Void .ctor(System.Collections.Generic.IReadOnlyList`1<AIBT.NodeId>,System.UInt32,System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.ReferencePreviewBlackboardValue>)`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.ReferencePreviewBlackboardValue> Blackboard`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NodeId> ActiveNodeIds`
- `PROPERTY System.UInt32 ActiveOperationCount`

---

### `AIBT.Authoring.ReferencePreviewProgress`

Mirrors <c>ReferenceExecutionProgress</c> (internal, Runtime) across the assembly boundary.

- `FIELD AIBT.Authoring.ReferencePreviewProgress Completed`
- `FIELD AIBT.Authoring.ReferencePreviewProgress Faulted`
- `FIELD AIBT.Authoring.ReferencePreviewProgress Rejected`
- `FIELD AIBT.Authoring.ReferencePreviewProgress Suspended`
- `FIELD AIBT.Authoring.ReferencePreviewProgress Waiting`
- `FIELD System.Int32 value__`

---

### `AIBT.Authoring.ReferencePreviewTraceEvent`

One atomic reference-executor trace event, translated from the internal <c>ReferenceTraceRecord</c> into the public authoring node identity space so editor code (which has no internals visibility into <c>AIBT.Runtime</c>) can consume it.

- `METHOD System.Void .ctor(System.UInt64,AIBT.Authoring.ReferencePreviewTraceEventKind,System.Nullable`1<AIBT.NodeId>,System.Nullable`1<AIBT.NodeStatus>,System.Nullable`1<AIBT.NodeId>)`
- `PROPERTY AIBT.Authoring.ReferencePreviewTraceEventKind Kind`
- `PROPERTY System.Nullable`1<AIBT.NodeId> Node`
- `PROPERTY System.Nullable`1<AIBT.NodeId> SourceNode`
- `PROPERTY System.Nullable`1<AIBT.NodeStatus> Status`
- `PROPERTY System.UInt64 Sequence`

---

### `AIBT.Authoring.ReferencePreviewTraceEventKind`

Mirrors <c>ReferenceTraceEventKind</c> (internal, Runtime) across the assembly boundary.

- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind BlackboardChanged`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind BudgetYielded`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind CommandEmitted`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind CompletionConsumed`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind CompletionDiscarded`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind DiagnosticRaised`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind ExecutionResumed`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind NodeAbortStarted`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind NodeEntered`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind NodeExited`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind NodeTicked`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind ObserverEvaluated`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind ObserverQueued`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind UpdateCompleted`
- `FIELD AIBT.Authoring.ReferencePreviewTraceEventKind UpdateStarted`
- `FIELD System.Int32 value__`

---

### `AIBT.Authoring.RegisteredBlackboardTypeCatalog`

- `METHOD AIBT.Authoring.BlackboardDefaultValue CreateDefault(System.String,System.UInt32,AIBT.Authoring.SemanticObject)`
- `METHOD System.Boolean TryGet(System.String,System.UInt32,AIBT.Authoring.RegisteredBlackboardTypeCatalogEntry&)`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.RegisteredBlackboardTypeCatalogEntry>)`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.RegisteredBlackboardTypeCatalogEntry> Entries`

---

### `AIBT.Authoring.RegisteredBlackboardTypeCatalogEntry`

- `METHOD System.Void .ctor(System.String,System.UInt32,System.String,System.String,AIBT.RegisteredUnmanagedTypeDescriptor,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.GeneratedStorageField>)`
- `PROPERTY AIBT.RegisteredUnmanagedTypeDescriptor Descriptor`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.GeneratedStorageField> Fields`
- `PROPERTY System.String CanonicalSchemaId`
- `PROPERTY System.String CanonicalTypeId`
- `PROPERTY System.String SchemaHash`
- `PROPERTY System.UInt32 Version`

---

### `AIBT.Authoring.SemanticObject`

- `METHOD System.Boolean Equals(AIBT.Authoring.SemanticObject)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryGetValue(System.String,AIBT.Authoring.SemanticValue&)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.SemanticProperty>)`
- `PROPERTY AIBT.Authoring.SemanticObject Empty`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.SemanticProperty> Properties`

---

### `AIBT.Authoring.SemanticProperty`

- `METHOD System.Boolean Equals(AIBT.Authoring.SemanticProperty)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.String,AIBT.Authoring.SemanticValue)`
- `PROPERTY AIBT.Authoring.SemanticValue Value`
- `PROPERTY System.String Name`

---

### `AIBT.Authoring.SemanticValue`

- `METHOD AIBT.Authoring.SemanticValue FromArray(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.SemanticValue>)`
- `METHOD AIBT.Authoring.SemanticValue FromBoolean(System.Boolean)`
- `METHOD AIBT.Authoring.SemanticValue FromInt64(System.Int64)`
- `METHOD AIBT.Authoring.SemanticValue FromNumber(System.Double)`
- `METHOD AIBT.Authoring.SemanticValue FromObject(AIBT.Authoring.SemanticObject)`
- `METHOD AIBT.Authoring.SemanticValue FromString(System.String)`
- `METHOD AIBT.Authoring.SemanticValue FromUInt64(System.UInt64)`
- `METHOD System.Boolean Equals(AIBT.Authoring.SemanticValue)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Boolean TryGetArray(System.Collections.Generic.IReadOnlyList`1[[AIBT.Authoring.SemanticValue, AIBT.Authoring, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]&)`
- `METHOD System.Boolean TryGetBoolean(System.Boolean&)`
- `METHOD System.Boolean TryGetInt64(System.Int64&)`
- `METHOD System.Boolean TryGetNumber(System.Double&)`
- `METHOD System.Boolean TryGetObject(AIBT.Authoring.SemanticObject&)`
- `METHOD System.Boolean TryGetString(System.String&)`
- `METHOD System.Boolean TryGetUInt64(System.UInt64&)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY AIBT.Authoring.SemanticValue Null`
- `PROPERTY AIBT.Authoring.SemanticValueKind Kind`

---

### `AIBT.Authoring.SemanticValueKind`

- `FIELD AIBT.Authoring.SemanticValueKind Array`
- `FIELD AIBT.Authoring.SemanticValueKind Boolean`
- `FIELD AIBT.Authoring.SemanticValueKind Null`
- `FIELD AIBT.Authoring.SemanticValueKind Number`
- `FIELD AIBT.Authoring.SemanticValueKind Object`
- `FIELD AIBT.Authoring.SemanticValueKind SignedInteger`
- `FIELD AIBT.Authoring.SemanticValueKind String`
- `FIELD AIBT.Authoring.SemanticValueKind UnsignedInteger`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.SuggestedDiagnosticOperation`

- `METHOD System.Boolean Equals(AIBT.Authoring.SuggestedDiagnosticOperation)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 CompareTo(AIBT.Authoring.SuggestedDiagnosticOperation)`
- `METHOD System.Int32 GetHashCode()`
- `PROPERTY AIBT.Authoring.DiagnosticOperationPayload Payload`
- `PROPERTY System.String OperationId`
- `PROPERTY System.String PayloadType`

---

### `AIBT.Authoring.TagSet`

- `METHOD System.Boolean Contains(System.String)`
- `METHOD System.Boolean Equals(AIBT.Authoring.TagSet)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<System.String>)`
- `PROPERTY AIBT.Authoring.TagSet Empty`
- `PROPERTY System.Boolean HasDuplicateValues`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> Values`

---

### `AIBT.Authoring.TreeDocument`

- `FIELD System.Int32 CurrentFormatVersion`
- `FIELD System.Int32 LatestFormatVersion`
- `FIELD System.String CurrentFormat`
- `METHOD AIBT.Authoring.TreeDocument CreateVersion2(AIBT.TreeId,System.String,AIBT.NodeId,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeDocument>,AIBT.Authoring.BlackboardScopeContract,AIBT.Authoring.BlackboardScopeContract,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.BlackboardKeyDefinition>,System.String,AIBT.Authoring.TagSet,AIBT.Authoring.SemanticObject,AIBT.Revision)`
- `METHOD System.Boolean RemoveNodeAt(System.Int32)`
- `METHOD System.Boolean ReplaceNodeAt(System.Int32,AIBT.Authoring.NodeDocument)`
- `METHOD System.Boolean SetBlackboard(System.Collections.Generic.IEnumerable`1<AIBT.Authoring.BlackboardKeyDefinition>)`
- `METHOD System.Boolean SetDescription(System.String)`
- `METHOD System.Boolean SetFormat(System.String,System.Int32)`
- `METHOD System.Boolean SetIdentity(AIBT.TreeId)`
- `METHOD System.Boolean SetMetadata(AIBT.Authoring.SemanticObject)`
- `METHOD System.Boolean SetName(System.String)`
- `METHOD System.Boolean SetRoot(AIBT.NodeId)`
- `METHOD System.Boolean SetTags(AIBT.Authoring.TagSet)`
- `METHOD System.Void .ctor(System.String,System.Int32,AIBT.TreeId,System.String,AIBT.NodeId,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeDocument>,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.BlackboardKeyDefinition>,System.String,AIBT.Authoring.TagSet,AIBT.Authoring.SemanticObject,AIBT.Revision)`
- `METHOD System.Void .ctor(System.String,System.Int32,AIBT.TreeId,System.String,AIBT.NodeId,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.NodeDocument>,System.Collections.Generic.IEnumerable`1<AIBT.Authoring.BlackboardKeyDefinition>,System.String,AIBT.Authoring.TagSet,AIBT.Authoring.SemanticObject,AIBT.Revision,AIBT.Authoring.BlackboardScopeContract,AIBT.Authoring.BlackboardScopeContract)`
- `METHOD System.Void AddNode(AIBT.Authoring.NodeDocument)`
- `PROPERTY AIBT.Authoring.BlackboardScopeContract AgentContract`
- `PROPERTY AIBT.Authoring.BlackboardScopeContract SharedContract`
- `PROPERTY AIBT.Authoring.SemanticObject Metadata`
- `PROPERTY AIBT.Authoring.TagSet Tags`
- `PROPERTY AIBT.NodeId Root`
- `PROPERTY AIBT.Revision Revision`
- `PROPERTY AIBT.TreeId TreeId`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.BlackboardKeyDefinition> Blackboard`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Authoring.NodeDocument> Nodes`
- `PROPERTY System.Int32 FormatVersion`
- `PROPERTY System.String Description`
- `PROPERTY System.String Format`
- `PROPERTY System.String Name`

---

### `AIBT.Authoring.TreeJsonDiagnosticCodes`

- `FIELD AIBT.DiagnosticCode DuplicateProperty`
- `FIELD AIBT.DiagnosticCode InvalidSyntax`
- `FIELD AIBT.DiagnosticCode InvalidUnicode`
- `FIELD AIBT.DiagnosticCode InvalidUtf8`
- `FIELD AIBT.DiagnosticCode MissingRegisteredSchema`
- `FIELD AIBT.DiagnosticCode SchemaViolation`
- `FIELD AIBT.DiagnosticCode UnrepresentableDocument`
- `FIELD AIBT.DiagnosticCode UnsupportedVersion`

---

### `AIBT.Authoring.TreeJsonReadResult`

- `PROPERTY AIBT.Authoring.TreeDocument Document`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Byte[] SourceUtf8`
- `PROPERTY System.String SourceText`

---

### `AIBT.Authoring.TreeJsonWriteResult`

- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Byte[] SemanticHash`
- `PROPERTY System.Byte[] Utf8`

---

### `AIBT.Authoring.TreeValidationDiagnosticCatalog`

- `PROPERTY AIBT.DiagnosticCatalog Catalog`

---

### `AIBT.Authoring.TreeValidationDiagnosticCodes`

- `FIELD AIBT.DiagnosticCode ChildPolicy`
- `FIELD AIBT.DiagnosticCode Cycle`
- `FIELD AIBT.DiagnosticCode DuplicateChild`
- `FIELD AIBT.DiagnosticCode DuplicateNodeIdentity`
- `FIELD AIBT.DiagnosticCode DuplicateParameter`
- `FIELD AIBT.DiagnosticCode DuplicateWatchedKey`
- `FIELD AIBT.DiagnosticCode InvalidDocument`
- `FIELD AIBT.DiagnosticCode InvalidFormat`
- `FIELD AIBT.DiagnosticCode InvalidNodeIdentity`
- `FIELD AIBT.DiagnosticCode InvalidObserverContext`
- `FIELD AIBT.DiagnosticCode InvalidObserverMode`
- `FIELD AIBT.DiagnosticCode InvalidRoot`
- `FIELD AIBT.DiagnosticCode InvalidTreeIdentity`
- `FIELD AIBT.DiagnosticCode InvalidWatchedKey`
- `FIELD AIBT.DiagnosticCode MissingBlackboardAccess`
- `FIELD AIBT.DiagnosticCode MissingChild`
- `FIELD AIBT.DiagnosticCode MissingParameter`
- `FIELD AIBT.DiagnosticCode MissingTreeName`
- `FIELD AIBT.DiagnosticCode MultipleParents`
- `FIELD AIBT.DiagnosticCode ParallelThreshold`
- `FIELD AIBT.DiagnosticCode ParameterConstraint`
- `FIELD AIBT.DiagnosticCode ParameterType`
- `FIELD AIBT.DiagnosticCode PolicyViolation`
- `FIELD AIBT.DiagnosticCode UnknownNodeType`
- `FIELD AIBT.DiagnosticCode UnknownParameter`
- `FIELD AIBT.DiagnosticCode UnreachableNode`
- `FIELD AIBT.DiagnosticCode UnsupportedBlackboardScope`
- `FIELD AIBT.DiagnosticCode UnsupportedBlackboardWrite`
- `FIELD AIBT.DiagnosticCode UnsupportedExecutionDomain`
- `FIELD AIBT.DiagnosticCode UnsupportedFormatVersion`
- `FIELD AIBT.DiagnosticCode UnsupportedNodeCapability`
- `FIELD AIBT.DiagnosticCode UnsupportedNodeVersion`

---

### `AIBT.Authoring.TreeValidationPolicy`

- `METHOD System.Void .ctor(System.Nullable`1<System.Int32>,System.Nullable`1<System.Int32>,System.Boolean,System.Boolean,System.Boolean,System.Boolean,AIBT.Authoring.BlackboardNamingPolicy,System.Collections.Generic.IEnumerable`1<System.String>,System.Boolean,System.Boolean,System.Collections.Generic.IEnumerable`1<AIBT.DiagnosticCode>,System.Nullable`1<System.Double>,System.Boolean,System.Boolean)`
- `PROPERTY AIBT.Authoring.BlackboardNamingPolicy BlackboardNaming`
- `PROPERTY System.Boolean AllowMainThreadNodes`
- `PROPERTY System.Boolean AllowManagedNodes`
- `PROPERTY System.Boolean AllowSideEffects`
- `PROPERTY System.Boolean ForbidUnboundedRepeaters`
- `PROPERTY System.Boolean RequireDeterministicNodes`
- `PROPERTY System.Boolean RequireEventDrivenServices`
- `PROPERTY System.Boolean RequireNodeDescriptions`
- `PROPERTY System.Boolean RequireTreeDescription`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.DiagnosticCode> WarningsAsErrors`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> ForbiddenNodeTypes`
- `PROPERTY System.Nullable`1<System.Double> MaxEstimatedCost`
- `PROPERTY System.Nullable`1<System.Int32> MaxNodesPerTree`
- `PROPERTY System.Nullable`1<System.Int32> MaxTreeDepth`

---

### `AIBT.Authoring.TreeValidator`

- `METHOD AIBT.DiagnosticCollection Validate(AIBT.Authoring.TreeDocument,AIBT.Authoring.NodeRegistry,AIBT.Authoring.ValidationOptions)`
- `METHOD AIBT.DiagnosticCollection Validate(AIBT.Authoring.TreeDocument,AIBT.Authoring.NodeRegistry,AIBT.Authoring.ValidationOptions,AIBT.Authoring.RegisteredBlackboardTypeCatalog)`

---

### `AIBT.Authoring.UnreachableNodePolicy`

- `FIELD AIBT.Authoring.UnreachableNodePolicy Allow`
- `FIELD AIBT.Authoring.UnreachableNodePolicy Error`
- `FIELD AIBT.Authoring.UnreachableNodePolicy Warning`
- `FIELD System.Byte value__`

---

### `AIBT.Authoring.ValidationOptions`

- `METHOD System.Void .ctor(System.String,AIBT.Authoring.UnreachableNodePolicy,System.Boolean,System.Boolean,AIBT.Authoring.TreeValidationPolicy)`
- `PROPERTY AIBT.Authoring.TreeValidationPolicy Policy`
- `PROPERTY AIBT.Authoring.UnreachableNodePolicy UnreachableNodes`
- `PROPERTY AIBT.Authoring.ValidationOptions Phase1`
- `PROPERTY System.Boolean SupportsAgentScope`
- `PROPERTY System.Boolean SupportsSharedScope`
- `PROPERTY System.String DocumentId`
