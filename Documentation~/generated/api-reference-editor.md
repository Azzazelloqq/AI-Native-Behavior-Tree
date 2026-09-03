# AIBT.Editor -- public API reference (generated)

Source: live reflection over `AIBT.Editor`'s own compiled public surface (`P7-014`). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

A type's own summary line is shown where an XML-doc `<summary>` exists in source; member-level doc-comment text is not yet correlated here (see this document's own generator comment for why) -- every member still gets its own full signature line regardless of whether prose exists for it.
48 public type(s).

---

### `AIBT.Editor.Debugger.NativeDebuggerTraceView`

A read-only, allocating (UI-facing, not a native hot path) projection of one <see cref="NativeTraceChannelSnapshotV1"/>: which nodes are currently active (entered without a matching exit in this snapshot), the ordered step history, and diagnostic events.

- `PROPERTY System.Boolean IsFaulted`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeTraceRecordV1> DiagnosticEvents`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NativeTraceRecordV1> StepHistory`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> ActiveNodeIndices`
- `PROPERTY System.UInt64 DroppedCount`

---

### `AIBT.Editor.Debugger.NativeExecutionDebuggerSession`

Attaches to an already-created, caller-owned <see cref="NativeTraceChannelOwnerV1"/> and reads it -- nothing else. This session never creates, disposes, resets, or acquires a writer lease on the channel; it only calls the channel's existing public read API (<see cref="NativeTraceChannelOwnerV1.TryGetSnapshot"/>), which itself only succeeds when the owner is <see cref="NativeOwnerStateV1.Initialized"/> (i.e. no writer job is currently leased/ in-flight) -- so there is no code path here that can stall or perturb a live native pass. <para> Attach protocol: whatever owns a running native execution pass (a test harness today; a future Play-mode host once one exists -- see the P3-010 evidence's known limitations) hands this session its <see cref="NativeTraceChannelOwnerV1"/> reference directly via <see cref="Attach"/>. There is no discovery/registry mechanism, because there is nothing yet in AIBT's production code that would populate one; standalone-Player attachment is explicitly out of scope per this card. </para>

- `METHOD System.Boolean TryReadTrace(AIBT.Editor.Debugger.NativeDebuggerTraceView&,AIBT.NativeTraceChannelFailureV1&)`
- `METHOD System.Void .ctor()`
- `METHOD System.Void Attach(AIBT.NativeTraceChannelOwnerV1)`
- `METHOD System.Void Detach()`
- `PROPERTY System.Boolean IsAttached`

---

### `AIBT.Editor.Editing.SemanticEditHistory`

Undo/redo for semantic edits, as a snapshot stack over <see cref="TreeDocument"/> instances accepted by <see cref="SemanticEditTransaction"/> -- mirrors AIBT.Editor.Organization.LayoutHistory's shape for the same reason: every accepted edit already produces a distinct document, so undo/redo is just moving a cursor through them.

- `METHOD AIBT.Authoring.TreeDocument Redo()`
- `METHOD AIBT.Authoring.TreeDocument Undo()`
- `METHOD System.Void .ctor(AIBT.Authoring.TreeDocument)`
- `METHOD System.Void Do(AIBT.Authoring.TreeDocument)`
- `PROPERTY AIBT.Authoring.TreeDocument Current`
- `PROPERTY System.Boolean CanRedo`
- `PROPERTY System.Boolean CanUndo`

---

### `AIBT.Editor.Editing.SemanticEditOperations`

Mechanical, pure semantic-tree edits: add/remove node, connect/disconnect, reconfigure a parameter. Every operation returns a new <see cref="TreeDocument"/>; none validate or compile -- acceptance is decided uniformly by <see cref="SemanticEditTransaction"/> through the existing Authoring/compiler pipeline, per editor-and-layout.md's "AI tools modify semantics through domain operations" rule (human edits use the same operations).

- `METHOD AIBT.Authoring.TreeDocument AddNode(AIBT.Authoring.TreeDocument,AIBT.Authoring.NodeDocument,AIBT.NodeId,System.Nullable`1<System.Int32>)`
- `METHOD AIBT.Authoring.TreeDocument Connect(AIBT.Authoring.TreeDocument,AIBT.NodeId,AIBT.NodeId,System.Nullable`1<System.Int32>)`
- `METHOD AIBT.Authoring.TreeDocument Disconnect(AIBT.Authoring.TreeDocument,AIBT.NodeId,AIBT.NodeId)`
- `METHOD AIBT.Authoring.TreeDocument RemoveNode(AIBT.Authoring.TreeDocument,AIBT.NodeId)`
- `METHOD AIBT.Authoring.TreeDocument SetParameter(AIBT.Authoring.TreeDocument,AIBT.NodeId,System.String,AIBT.Authoring.SemanticValue)`

---

### `AIBT.Editor.Editing.SemanticEditResult`

- `METHOD System.Void .ctor(AIBT.Authoring.TreeDocument,AIBT.DiagnosticCollection,System.Boolean)`
- `PROPERTY AIBT.Authoring.TreeDocument Document`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY System.Boolean Accepted`

---

### `AIBT.Editor.Editing.SemanticEditTransaction`

Gates every semantic edit through the same canonical parse/validate/compile pipeline an out-of-band `.aibt.json` edit or an AI domain operation would go through -- no separate, weaker in-editor validation path, per this card's acceptance criteria. An edit is applied speculatively, then accepted only if <see cref="ReferenceCompiler.Compile"/> (which itself calls <see cref="TreeValidator.Validate"/>) succeeds; otherwise the pre-edit document is returned untouched, with the real compiler/validator diagnostics attached.

- `METHOD AIBT.Editor.Editing.SemanticEditResult Apply(AIBT.Authoring.TreeDocument,System.Func`2<AIBT.Authoring.TreeDocument,AIBT.Authoring.TreeDocument>,AIBT.Authoring.NodeRegistry,AIBT.Authoring.ReferenceCompilerOptions)`

---

### `AIBT.Editor.Graph.BehaviorTreeGraphView`

Read-only rendering of a canonical <see cref="TreeDocument"/> as a graph. Never mutates the document and never writes to disk; positions are a transient default until a real *.aibt.layout.json reader exists (P3-005).

- `METHOD System.Void .ctor()`
- `METHOD System.Void Populate(AIBT.Authoring.TreeDocument,AIBT.Authoring.NodeRegistry)`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<AIBT.NodeId,AIBT.Editor.Graph.BehaviorTreeNode> NodesById`

---

### `AIBT.Editor.Graph.BehaviorTreeGraphWindow`

Hosts a <see cref="BehaviorTreeGraphView"/> over an existing .aibt.json document. Read-only.

- `METHOD AIBT.Editor.Graph.BehaviorTreeGraphWindow ShowWindow()`
- `METHOD System.Void .ctor()`
- `METHOD System.Void OpenFromBytes(System.Byte[],System.String,AIBT.Authoring.NodeRegistry)`
- `METHOD System.Void OpenFromPath(System.String,AIBT.Authoring.NodeRegistry)`
- `PROPERTY AIBT.Authoring.TreeDocument Document`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`

---

### `AIBT.Editor.Graph.BehaviorTreeNode`

- `METHOD System.Void .ctor(AIBT.Authoring.NodeDocument,AIBT.Authoring.NodeManifest)`
- `PROPERTY AIBT.Authoring.NodeManifest Manifest`
- `PROPERTY AIBT.NodeId NodeId`
- `PROPERTY System.String TypeId`
- `PROPERTY UnityEditor.Experimental.GraphView.Port InputPort`
- `PROPERTY UnityEditor.Experimental.GraphView.Port OutputPort`

---

### `AIBT.Editor.HotReload.HotReloadWorkflowWindow`

Surfaces hot reload as an explicit, explained Editor workflow (<c>P5-008</c>): load a tree, optionally run it, then explicitly reload it against a second file and see exactly what <see cref="HotReloadPreviewDriver.TryReload"/> actually did -- classification per node, the strategy applied, and the migrated/reset/dropped counts -- before or as the reload happens, never silently in the background. A single explicit "Reload" button is the only trigger; this window never watches files or reloads automatically.

- `METHOD AIBT.Editor.HotReload.HotReloadWorkflowWindow ShowWindow()`
- `METHOD System.Void .ctor()`
- `METHOD System.Void LoadFromPath(System.String)`
- `METHOD System.Void ReloadFromPath(System.String)`

---

### `AIBT.Editor.Layout.CanonicalLayoutJson`

Strict reader for *.aibt.layout.json, per editor-layout-v1.md. Mirrors AIBT.Authoring.CanonicalTreeJson's approach (Newtonsoft JsonTextReader with DuplicatePropertyNameHandling.Error, fail-closed on unknown fields) at a scale appropriate to the smaller layout schema.

- `METHOD AIBT.Editor.Layout.LayoutJsonReadResult Parse(System.Byte[],System.String,AIBT.Authoring.TreeDocument)`
- `METHOD AIBT.Editor.Layout.LayoutJsonReadResult Parse(System.String,System.String,AIBT.Authoring.TreeDocument)`

---

### `AIBT.Editor.Layout.CanonicalLayoutJsonWriter`

Canonical serializer for <see cref="LayoutDocument"/>, implementing editor-layout-v1.md's encoding rules (UTF-8 no BOM, LF, two-space indent, ordinal key order, Float32 shortest-round-trip formatting). Mirrors AIBT.Authoring.CanonicalTreeJsonWriter's structure; duplicated rather than shared because AIBT.Authoring does not grant InternalsVisibleTo to AIBT.Editor and this card's allowed changes are scoped to Editor/Layout/ only.

- `METHOD System.Byte[] Write(AIBT.Editor.Layout.LayoutDocument)`

---

### `AIBT.Editor.Layout.DeterministicAutoLayoutService`

Implements editor-layout-v1.md's deterministic auto-layout contract: Layout(semanticTree, layoutInputs) -> layoutOutput. Never reads or writes .aibt.json; consumes an already-loaded <see cref="TreeDocument"/> only.

- `METHOD AIBT.Editor.Layout.LayoutDocument Layout(AIBT.Authoring.TreeDocument,AIBT.Editor.Layout.LayoutDocument)`
- `METHOD System.Byte[] LayoutToBytes(AIBT.Authoring.TreeDocument,AIBT.Editor.Layout.LayoutDocument)`

---

### `AIBT.Editor.Layout.LayoutDirection`

Direction values from editor-layout-v1.md's "direction" header field.

- `FIELD AIBT.Editor.Layout.LayoutDirection LeftToRight`
- `FIELD AIBT.Editor.Layout.LayoutDirection TopToBottom`
- `FIELD System.Int32 value__`

---

### `AIBT.Editor.Layout.LayoutDocument`

In-memory model of a *.aibt.layout.json document: node positions/pinning (P3-004), plus groups, sticky notes, and edge reroutes (P3-005) -- all user-authored, presentation-only data, per editor-layout-v1.md.

- `FIELD System.Int32 CurrentFormatVersion`
- `FIELD System.String CurrentFormat`
- `METHOD System.Void .ctor(AIBT.TreeId,AIBT.Editor.Layout.LayoutDirection,System.Collections.Generic.IReadOnlyDictionary`2<AIBT.NodeId,AIBT.Editor.Layout.LayoutNodePlacement>,System.Collections.Generic.IReadOnlyDictionary`2<System.String,AIBT.Editor.Layout.LayoutGroup>,System.Collections.Generic.IReadOnlyDictionary`2<System.String,AIBT.Editor.Layout.LayoutNote>,System.Collections.Generic.IReadOnlyDictionary`2<AIBT.Editor.Layout.LayoutEdgeKey,AIBT.Editor.Layout.LayoutReroute>)`
- `PROPERTY AIBT.Editor.Layout.LayoutDirection Direction`
- `PROPERTY AIBT.TreeId TreeId`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<AIBT.Editor.Layout.LayoutEdgeKey,AIBT.Editor.Layout.LayoutReroute> Reroutes`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<AIBT.NodeId,AIBT.Editor.Layout.LayoutNodePlacement> Nodes`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<System.String,AIBT.Editor.Layout.LayoutGroup> Groups`
- `PROPERTY System.Collections.Generic.IReadOnlyDictionary`2<System.String,AIBT.Editor.Layout.LayoutNote> Notes`

---

### `AIBT.Editor.Layout.LayoutEdgeKey`

The key of editor-layout-v1.md's "reroutes" map: an edge identity, "fromNodeId|toNodeId".

- `METHOD System.Boolean Equals(AIBT.Editor.Layout.LayoutEdgeKey)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.String ToKeyString()`
- `METHOD System.Void .ctor(AIBT.NodeId,AIBT.NodeId)`
- `PROPERTY AIBT.NodeId FromNodeId`
- `PROPERTY AIBT.NodeId ToNodeId`

---

### `AIBT.Editor.Layout.LayoutGroup`

One entry of editor-layout-v1.md's "groups" map.

- `METHOD System.Void .ctor(System.String,System.Collections.Generic.IEnumerable`1<AIBT.NodeId>,System.String,System.String,System.Boolean)`
- `PROPERTY System.Boolean Locked`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.NodeId> MemberNodeIds`
- `PROPERTY System.String Color`
- `PROPERTY System.String Description`
- `PROPERTY System.String Title`

---

### `AIBT.Editor.Layout.LayoutIdentity`

editor-layout-v1.md's GroupId/NoteId identity rule: the same authoring-identity grammar as NodeId.

- `METHOD System.Boolean IsValid(System.String)`

---

### `AIBT.Editor.Layout.LayoutJsonDiagnosticCodes`

editor-layout-v1.md's AIBT1101-1111 diagnostic codes for *.aibt.layout.json.

- `FIELD AIBT.DiagnosticCode DuplicateProperty`
- `FIELD AIBT.DiagnosticCode InvalidDirection`
- `FIELD AIBT.DiagnosticCode InvalidSyntax`
- `FIELD AIBT.DiagnosticCode InvalidUnicode`
- `FIELD AIBT.DiagnosticCode InvalidUtf8`
- `FIELD AIBT.DiagnosticCode NodeInMultipleGroups`
- `FIELD AIBT.DiagnosticCode OrphanedReroute`
- `FIELD AIBT.DiagnosticCode SchemaViolation`
- `FIELD AIBT.DiagnosticCode TreeIdMismatch`
- `FIELD AIBT.DiagnosticCode UnknownNodeReference`
- `FIELD AIBT.DiagnosticCode UnsupportedVersion`

---

### `AIBT.Editor.Layout.LayoutJsonReadResult`

Result of <see cref="CanonicalLayoutJson.Parse(byte[], string, AIBT.Authoring.TreeDocument)"/>.

- `METHOD System.Void .ctor(AIBT.Editor.Layout.LayoutDocument,AIBT.DiagnosticCollection,System.String,System.Byte[])`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY AIBT.Editor.Layout.LayoutDocument Document`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Byte[] SourceUtf8`
- `PROPERTY System.String SourceText`

---

### `AIBT.Editor.Layout.LayoutNodePlacement`

One entry of editor-layout-v1.md's "nodes" map.

- `METHOD System.Void .ctor(AIBT.Editor.Layout.LayoutPoint,System.Boolean)`
- `PROPERTY AIBT.Editor.Layout.LayoutPoint Position`
- `PROPERTY System.Boolean Pinned`

---

### `AIBT.Editor.Layout.LayoutNote`

One entry of editor-layout-v1.md's "notes" map.

- `METHOD System.Void .ctor(System.String,AIBT.Editor.Layout.LayoutPoint,AIBT.Editor.Layout.LayoutPoint,System.String)`
- `PROPERTY AIBT.Editor.Layout.LayoutPoint Position`
- `PROPERTY AIBT.Editor.Layout.LayoutPoint Size`
- `PROPERTY System.String Color`
- `PROPERTY System.String Text`

---

### `AIBT.Editor.Layout.LayoutPoint`

A Float32 canvas point, per editor-layout-v1.md's Float2 position/size fields.

- `METHOD System.Boolean Equals(AIBT.Editor.Layout.LayoutPoint)`
- `METHOD System.Boolean Equals(System.Object)`
- `METHOD System.Int32 GetHashCode()`
- `METHOD System.Void .ctor(System.Single,System.Single)`
- `PROPERTY System.Single X`
- `PROPERTY System.Single Y`

---

### `AIBT.Editor.Layout.LayoutReroute`

One entry of editor-layout-v1.md's "reroutes" map.

- `METHOD System.Void .ctor(System.Collections.Generic.IEnumerable`1<AIBT.Editor.Layout.LayoutPoint>)`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Editor.Layout.LayoutPoint> Waypoints`

---

### `AIBT.Editor.Migration.MigrationNotificationWindow`

P7-006 (ADR-P7-005): a non-blocking notification listing every project document with a node the migration engine could rewrite in memory, and a per-document button to persist that same migration to disk. A plain <see cref="EditorWindow"/> -- never <c>ShowModalUtility</c> -- so it never gates anything, including the MCP/AI-agent path, which already gets the same in-memory migration transparently on every <c>validate</c>/<c>compile</c> call regardless of whether this window is ever opened.

- `METHOD AIBT.Editor.Migration.MigrationNotificationWindow ShowWindow()`
- `METHOD System.Void .ctor()`
- `METHOD System.Void Scan(AIBT.Authoring.Migration.NodeMigrationRegistry,System.String,AIBT.Authoring.NodeRegistry)`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.TreeId> LastScanMigratableTreeIds`

---

### `AIBT.Editor.Organization.LayoutHistory`

Undo/redo for manual organization actions, as a snapshot stack over immutable <see cref="LayoutDocument"/> instances -- every <see cref="LayoutOrganizationOperations"/> call returns a new document, so undo/redo is just moving a cursor through prior snapshots.

- `METHOD AIBT.Editor.Layout.LayoutDocument Redo()`
- `METHOD AIBT.Editor.Layout.LayoutDocument Undo()`
- `METHOD System.Void .ctor(AIBT.Editor.Layout.LayoutDocument)`
- `METHOD System.Void Do(AIBT.Editor.Layout.LayoutDocument)`
- `PROPERTY AIBT.Editor.Layout.LayoutDocument Current`
- `PROPERTY System.Boolean CanRedo`
- `PROPERTY System.Boolean CanUndo`

---

### `AIBT.Editor.Organization.LayoutLoadResult`

- `METHOD System.Void .ctor(AIBT.Editor.Layout.LayoutDocument,AIBT.DiagnosticCollection,System.Boolean)`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY AIBT.Editor.Layout.LayoutDocument Document`
- `PROPERTY System.Boolean Success`
- `PROPERTY System.Boolean UsedDefault`

---

### `AIBT.Editor.Organization.LayoutOrganizationOperations`

Pure, immutable manual-organization operations over a <see cref="LayoutDocument"/>: pin, group, comment (sticky note), and reroute. Every operation returns a new document; none ever touches the semantic tree, per editor-and-layout.md's "presentation-only" rule.

- `METHOD AIBT.Editor.Layout.LayoutDocument AddOrUpdateGroup(AIBT.Editor.Layout.LayoutDocument,System.String,System.String,System.Collections.Generic.IEnumerable`1<AIBT.NodeId>,System.String,System.String,System.Boolean)`
- `METHOD AIBT.Editor.Layout.LayoutDocument AddOrUpdateNote(AIBT.Editor.Layout.LayoutDocument,System.String,System.String,AIBT.Editor.Layout.LayoutPoint,AIBT.Editor.Layout.LayoutPoint,System.String)`
- `METHOD AIBT.Editor.Layout.LayoutDocument AddOrUpdateReroute(AIBT.Editor.Layout.LayoutDocument,AIBT.NodeId,AIBT.NodeId,System.Collections.Generic.IEnumerable`1<AIBT.Editor.Layout.LayoutPoint>)`
- `METHOD AIBT.Editor.Layout.LayoutDocument Pin(AIBT.Editor.Layout.LayoutDocument,AIBT.NodeId)`
- `METHOD AIBT.Editor.Layout.LayoutDocument RemoveGroup(AIBT.Editor.Layout.LayoutDocument,System.String)`
- `METHOD AIBT.Editor.Layout.LayoutDocument RemoveNote(AIBT.Editor.Layout.LayoutDocument,System.String)`
- `METHOD AIBT.Editor.Layout.LayoutDocument RemoveReroute(AIBT.Editor.Layout.LayoutDocument,AIBT.NodeId,AIBT.NodeId)`
- `METHOD AIBT.Editor.Layout.LayoutDocument SetNodePosition(AIBT.Editor.Layout.LayoutDocument,AIBT.NodeId,AIBT.Editor.Layout.LayoutPoint)`
- `METHOD AIBT.Editor.Layout.LayoutDocument Unpin(AIBT.Editor.Layout.LayoutDocument,AIBT.NodeId)`

---

### `AIBT.Editor.Organization.LayoutPersistenceController`

Orchestrates *.aibt.layout.json load/save next to a .aibt.json document. A missing layout file falls back to P3-004's deterministic auto-layout (never blocks on absence); a present but invalid file surfaces diagnostics rather than being silently discarded, per editor-and-layout.md's collaboration rules.

- `METHOD AIBT.Editor.Organization.LayoutLoadResult Load(System.String,AIBT.Authoring.TreeDocument)`
- `METHOD System.String LayoutPathFor(System.String)`
- `METHOD System.Void Save(System.String,AIBT.Editor.Layout.LayoutDocument)`

---

### `AIBT.Editor.Patching.LayoutDiff`

- `METHOD AIBT.Editor.Patching.LayoutDiff Between(AIBT.Editor.Layout.LayoutDocument,AIBT.Editor.Layout.LayoutDocument)`
- `PROPERTY AIBT.Editor.Patching.LayoutDiff Empty`
- `PROPERTY System.Boolean IsEmpty`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Editor.Patching.LayoutDiffEntry> Entries`

---

### `AIBT.Editor.Patching.LayoutDiffEntry`

- `METHOD System.Void .ctor(AIBT.Editor.Patching.LayoutDiffTarget,System.String,AIBT.Editor.Patching.LayoutDiffKind)`
- `PROPERTY AIBT.Editor.Patching.LayoutDiffKind Kind`
- `PROPERTY AIBT.Editor.Patching.LayoutDiffTarget Target`
- `PROPERTY System.String Key`

---

### `AIBT.Editor.Patching.LayoutDiffKind`

- `FIELD AIBT.Editor.Patching.LayoutDiffKind Added`
- `FIELD AIBT.Editor.Patching.LayoutDiffKind Changed`
- `FIELD AIBT.Editor.Patching.LayoutDiffKind Moved`
- `FIELD AIBT.Editor.Patching.LayoutDiffKind PinChanged`
- `FIELD AIBT.Editor.Patching.LayoutDiffKind Removed`
- `FIELD System.Int32 value__`

---

### `AIBT.Editor.Patching.LayoutDiffTarget`

- `FIELD AIBT.Editor.Patching.LayoutDiffTarget Group`
- `FIELD AIBT.Editor.Patching.LayoutDiffTarget Node`
- `FIELD AIBT.Editor.Patching.LayoutDiffTarget Note`
- `FIELD AIBT.Editor.Patching.LayoutDiffTarget Reroute`
- `FIELD System.Int32 value__`

---

### `AIBT.Editor.Patching.LayoutPatchResult`

- `METHOD System.Void .ctor(AIBT.Editor.Layout.LayoutDocument,System.Boolean,AIBT.DiagnosticCollection,AIBT.Editor.Patching.LayoutDiff,System.String)`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY AIBT.Editor.Layout.LayoutDocument Document`
- `PROPERTY AIBT.Editor.Patching.LayoutDiff Diff`
- `PROPERTY System.Boolean Accepted`
- `PROPERTY System.String ResultHash`

---

### `AIBT.Editor.Patching.LayoutPatchTransaction`

Implements ADR-P6-002's layout-patch model: a content-hash precondition (LayoutDocument has no revision field) around composed <see cref="AIBT.Editor.Organization.LayoutOrganizationOperations"/> calls. Unlike the semantic case, layout operations have no compiler/validator -- the one real failure mode is a thrown <see cref="ArgumentException"/> (e.g. a group-membership conflict), caught here and turned into the same accept-or-reject-unchanged contract <see cref="SemanticPatchTransaction"/> provides.

- `METHOD AIBT.Editor.Patching.LayoutPatchResult Apply(AIBT.Editor.Layout.LayoutDocument,System.String,System.Collections.Generic.IReadOnlyList`1<System.Func`2<AIBT.Editor.Layout.LayoutDocument,AIBT.Editor.Layout.LayoutDocument>>)`

---

### `AIBT.Editor.Patching.SemanticDiff`

- `METHOD AIBT.Editor.Patching.SemanticDiff Between(AIBT.Authoring.TreeDocument,AIBT.Authoring.TreeDocument)`
- `PROPERTY AIBT.Editor.Patching.SemanticDiff Empty`
- `PROPERTY System.Boolean IsEmpty`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Editor.Patching.SemanticDiffEntry> Entries`

---

### `AIBT.Editor.Patching.SemanticDiffEntry`

- `METHOD System.Void .ctor(AIBT.NodeId,AIBT.Editor.Patching.SemanticDiffKind)`
- `PROPERTY AIBT.Editor.Patching.SemanticDiffKind Kind`
- `PROPERTY AIBT.NodeId NodeId`

---

### `AIBT.Editor.Patching.SemanticDiffKind`

- `FIELD AIBT.Editor.Patching.SemanticDiffKind Added`
- `FIELD AIBT.Editor.Patching.SemanticDiffKind Changed`
- `FIELD AIBT.Editor.Patching.SemanticDiffKind Removed`
- `FIELD System.Int32 value__`

---

### `AIBT.Editor.Patching.SemanticPatchResult`

- `METHOD System.Void .ctor(AIBT.Authoring.TreeDocument,System.Boolean,AIBT.DiagnosticCollection,AIBT.Editor.Patching.SemanticDiff)`
- `PROPERTY AIBT.Authoring.TreeDocument Document`
- `PROPERTY AIBT.DiagnosticCollection Diagnostics`
- `PROPERTY AIBT.Editor.Patching.SemanticDiff Diff`
- `PROPERTY System.Boolean Accepted`
- `PROPERTY System.UInt64 ResultRevision`

---

### `AIBT.Editor.Patching.SemanticPatchTransaction`

Implements ADR-P6-002's semantic-patch model: an expected-revision precondition around the already-accepted <see cref="SemanticEditTransaction"/> primitive, composing an ordered list of operations into one candidate and producing a structured diff. No second validation/compilation path -- every accept/reject decision is <see cref="SemanticEditTransaction.Apply"/>'s own.

- `METHOD AIBT.Editor.Patching.SemanticPatchResult Apply(AIBT.Authoring.TreeDocument,System.UInt64,System.Collections.Generic.IReadOnlyList`1<System.Func`2<AIBT.Authoring.TreeDocument,AIBT.Authoring.TreeDocument>>,AIBT.Authoring.NodeRegistry,AIBT.Authoring.ReferenceCompilerOptions)`

---

### `AIBT.Editor.Preview.BehaviorTreePreviewWindow`

Steps/plays a <c>.aibt.json</c> tree through <see cref="ReferencePreviewDriver"/> (the Phase 1 managed reference executor, driven as-is) and highlights the active node(s) live. Never writes to <c>.aibt.json</c> or <c>.aibt.layout.json</c> -- this window only reads a document into memory and steps a driver over it; <see cref="LoadDocument"/> can be called again with an edited <see cref="TreeDocument"/> (e.g. the result of a <c>SemanticEditTransaction</c>) to preview an edit without restarting the editor.

- `METHOD AIBT.Editor.Preview.BehaviorTreePreviewWindow ShowWindow()`
- `METHOD System.Void .ctor()`
- `METHOD System.Void LoadDocument(AIBT.Authoring.TreeDocument,System.String)`
- `METHOD System.Void LoadFromPath(System.String)`

---

### `AIBT.Editor.Trace.DiagnosticCorrelation`

A diagnostic event correlated to the step and graph state that produced it.

- `PROPERTY AIBT.NativeTraceRecordV1 Record`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> ActiveRuntimeNodeIndicesAtStep`
- `PROPERTY System.Int32 StepIndex`

---

### `AIBT.Editor.Trace.TraceStepEntry`

One trace event plus the active-node set immediately after it was applied.

- `PROPERTY AIBT.NativeTraceRecordV1 Record`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.UInt32> ActiveRuntimeNodeIndicesAfter`
- `PROPERTY System.Int32 Index`

---

### `AIBT.Editor.Trace.TraceTimelineModel`

A scrubbable timeline over one <see cref="NativeDebuggerTraceView"/> snapshot (P3-010's read-only channel view -- this type never reads the channel itself, only replays a view already produced by <c>NativeExecutionDebuggerSession.TryReadTrace</c>, per this card's "pure consumer" scope). Replay is a pure function of the ordered step history: walking NodeEntered/NodeExited events forward and recording the active set after each step, so scrubbing to step N reproduces exactly the active-node state that step actually produced.

- `METHOD AIBT.Editor.Trace.TraceTimelineModel Build(AIBT.Editor.Debugger.NativeDebuggerTraceView)`
- `METHOD System.Collections.Generic.IReadOnlyList`1<System.UInt32> ActiveRuntimeNodeIndicesAtStep(System.Int32)`
- `PROPERTY AIBT.Editor.Trace.TraceTimelineModel Empty`
- `PROPERTY System.Boolean HasDroppedEvents`
- `PROPERTY System.Boolean IsFaulted`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Editor.Trace.DiagnosticCorrelation> Diagnostics`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Editor.Trace.TraceStepEntry> Steps`
- `PROPERTY System.UInt64 DroppedCount`

---

### `AIBT.Editor.Trace.TraceTimelineWindow`

Visualizes the execution trace read by P3-010's <see cref="NativeExecutionDebuggerSession"/>: a scrubbable step timeline, live/scrubbed active-node highlighting on a private <see cref="BehaviorTreeGraphView"/> instance (P3-003's read-only adapter, consumed as-is, not modified), and a diagnostic-event list correlated to the step/node that produced each one. This window never reads the trace channel itself and never changes P3-010's attach/read protocol -- it only calls <see cref="NativeExecutionDebuggerSession.TryReadTrace"/> and replays the resulting view via <see cref="TraceTimelineModel"/>.

- `METHOD AIBT.Editor.Trace.TraceTimelineWindow ShowWindow()`
- `METHOD System.Void .ctor()`
- `METHOD System.Void AttachSession(AIBT.Editor.Debugger.NativeExecutionDebuggerSession)`
- `METHOD System.Void LoadGraphContext(AIBT.Authoring.TreeDocument,AIBT.Authoring.NodeRegistry,AIBT.CompiledProgram)`
- `METHOD System.Void Refresh()`
- `PROPERTY AIBT.Editor.Trace.TraceTimelineModel CurrentModel`
- `PROPERTY System.Int32 CurrentStepIndex`

---

### `AIBT.Editor.Validation.DiagnosticGraphLocation`

Classifies one <see cref="Diagnostic"/> to a stable graph location, per this card's acceptance criterion that every diagnostic code renders with a location, not just a raw code/message dump. Every AIBT.Authoring.TreeValidator diagnostic that concerns a node sets Location.NodeId directly (confirmed by inspection of TreeValidator.Location/Create), so resolution never needs to parse the tree structure itself -- only the diagnostic's own Location.

- `METHOD AIBT.Editor.Validation.DiagnosticGraphLocation Resolve(AIBT.Diagnostic)`
- `PROPERTY AIBT.Diagnostic Diagnostic`
- `PROPERTY AIBT.Editor.Validation.DiagnosticGraphLocationKind Kind`
- `PROPERTY AIBT.NodeId NodeId`
- `PROPERTY System.String FieldName`

---

### `AIBT.Editor.Validation.DiagnosticGraphLocationKind`

- `FIELD AIBT.Editor.Validation.DiagnosticGraphLocationKind Document`
- `FIELD AIBT.Editor.Validation.DiagnosticGraphLocationKind Field`
- `FIELD AIBT.Editor.Validation.DiagnosticGraphLocationKind Node`
- `FIELD System.Int32 value__`

---

### `AIBT.Editor.Validation.DiagnosticGraphSummary`

Document-level view over a <see cref="DiagnosticCollection"/>: per-severity counts and a classified marker per diagnostic, ready for a graph editor to render. Always rebuilt fresh from the current diagnostics (never cached/mutated in place) -- fixing the underlying issue and recomputing diagnostics naturally drops the stale marker, satisfying this card's "clears without requiring a manual refresh action" acceptance criterion by construction.

- `METHOD AIBT.Editor.Validation.DiagnosticGraphSummary Build(AIBT.DiagnosticCollection)`
- `METHOD System.Collections.Generic.IEnumerable`1<AIBT.NodeId> NodesWithMarkers()`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.Editor.Validation.DiagnosticGraphLocation> Markers`
- `PROPERTY System.Int32 ErrorCount`
- `PROPERTY System.Int32 InfoCount`
- `PROPERTY System.Int32 TotalCount`
- `PROPERTY System.Int32 WarningCount`
