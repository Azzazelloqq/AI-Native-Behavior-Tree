# AIBT.Agents -- public API reference (generated)

Source: live reflection over `AIBT.Agents`'s own compiled public surface (`P7-014`). Regenerate with the `AIBT/MCP/Regenerate Documentation` Editor menu command. Do not hand-edit -- edits are overwritten on the next regeneration.

A type's own summary line is shown where an XML-doc `<summary>` exists in source; member-level doc-comment text is not yet correlated here (see this document's own generator comment for why) -- every member still gets its own full signature line regardless of whether prose exists for it.
70 public type(s).

---

### `AIBT.AgentBlackboard`

An agent's blackboard, addressed by the key names declared in its tree. Read and write it from any script on the main thread: a write made between ticks is seen by the tree on its next tick, and a write made by a node is seen by the nodes that run after it. Supported value types: bool, int, long, float, double, Vector2, Vector3, Quaternion and string. The value type must match the key's declared type.

- `METHOD  Get(System.String)`
- `METHOD  GetOrDefault(System.String,)`
- `METHOD System.Boolean Has(System.String)`
- `METHOD System.Boolean TryGet(System.String,)`
- `METHOD System.Boolean TrySet(System.String,)`
- `METHOD System.Void Set(System.String,)`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<System.String> Keys`

---

### `AIBT.AgentEventResponse`

An inspector response to a named agent event.

- `FIELD System.String eventName`
- `FIELD UnityEngine.Events.UnityEvent response`
- `METHOD System.Void .ctor()`

---

### `AIBT.AgentEvents`

Named events a behavior tree raises to gameplay code, for example <c>"attacked"</c> raised by the <c>aibt.unity.raise-event</c> node. Subscribe from a script with <see cref="Subscribe"/>, or wire responses in the agent's inspector (Event Responses). Events are raised synchronously on the main thread, inside the tree's tick.

- `METHOD System.Int32 Raise(System.String)`
- `METHOD System.Void Subscribe(System.String,System.Action`1<AIBT.BehaviorTreeAgent>)`
- `METHOD System.Void Unsubscribe(System.String,System.Action`1<AIBT.BehaviorTreeAgent>)`

---

### `AIBT.AgentReference`

A named scene object a tree can address, for example <c>"player"</c>. Nodes that act on another object take the reference name as a parameter; an empty name means the agent itself.

- `FIELD System.String name`
- `FIELD UnityEngine.Object target`
- `METHOD System.Void .ctor(System.String,UnityEngine.Object)`

---

### `AIBT.AgentTickMode`

When a <see cref="BehaviorTreeAgent"/> advances its tree.

- `FIELD AIBT.AgentTickMode EveryFrame`
- `FIELD AIBT.AgentTickMode Interval`
- `FIELD AIBT.AgentTickMode Manual`
- `FIELD System.Int32 value__`

---

### `AIBT.BbAnyKey`

A node parameter that names a blackboard key of any type, for nodes that only care about the key itself (for example when it last changed), not its value.

- `METHOD System.String ToString()`
- `PROPERTY System.Boolean IsBound`
- `PROPERTY System.String Name`

---

### `AIBT.BbKey`1`

- `METHOD System.String ToString()`
- `PROPERTY System.Boolean IsBound`
- `PROPERTY System.String Name`

---

### `AIBT.BehaviorTreeAgent`

Runs a behavior tree on this GameObject. Assign a tree and press Play: no generated catalog, runtime asset or host wiring is needed. Nodes are ordinary C# classes (<see cref="BtAction"/>, <see cref="BtCondition"/>) that can use this GameObject directly, and gameplay scripts exchange data with the tree through <see cref="Blackboard"/>. For very large crowds use the native (Burst) path instead.

- `METHOD System.Void .ctor()`
- `METHOD System.Void Restart(System.Boolean)`
- `METHOD System.Void SetReference(System.String,UnityEngine.Object)`
- `METHOD System.Void Stop()`
- `METHOD System.Void Tick(System.Single)`
- `METHOD UnityEngine.GameObject ResolveGameObject(System.String)`
- `METHOD UnityEngine.Object GetReference(System.String)`
- `PROPERTY AIBT.AgentBlackboard Blackboard`
- `PROPERTY AIBT.AgentEvents Events`
- `PROPERTY AIBT.AgentTickMode TickMode`
- `PROPERTY AIBT.BehaviorTreeSource Tree`
- `PROPERTY System.Boolean HasFaulted`
- `PROPERTY System.Boolean IsRunning`
- `PROPERTY System.Boolean RestartOnComplete`
- `PROPERTY System.Boolean StaggerIntervalTicks`
- `PROPERTY System.Collections.Generic.IReadOnlyList`1<AIBT.AgentReference> References`
- `PROPERTY System.Nullable`1<AIBT.NodeStatus> LastResult`
- `PROPERTY System.Single TickInterval`
- `PROPERTY System.String LastError`

---

### `AIBT.BehaviorTreeSource`

A behavior tree as a Unity asset. Importing an <c>.aibt</c> file produces one; assign it to a <see cref="BehaviorTreeAgent"/>. The <c>.aibt</c> file stays the source of truth: this asset only carries its text into the Player.

- `METHOD AIBT.BehaviorTreeSource FromJson(System.String,System.String)`
- `METHOD System.Void .ctor()`
- `METHOD System.Void SetContent(System.String,System.String,System.String,System.String,System.Int32,System.String,System.String)`
- `PROPERTY System.Int32 NodeCount`
- `PROPERTY System.String Details`
- `PROPERTY System.String Json`
- `PROPERTY System.String SourceId`
- `PROPERTY System.String Status`
- `PROPERTY System.String TreeId`
- `PROPERTY System.String TreeName`

---

### `AIBT.BtAction`

A node that does something and may take several ticks. Override <see cref="OnTick"/>; return <see cref="NodeStatus.Running"/> to be ticked again, or Success/Failure to finish.

_No public members declared directly on this type._

---

### `AIBT.BtCondition`

A node that answers a yes/no question in a single tick. Override <see cref="Check"/>; true maps to Success and false to Failure. A condition must not change game state.

- `FIELD AIBT.ConditionInterrupt interrupt`

---

### `AIBT.BtIgnoreAttribute`

Excludes a public field from a node's parameters.

- `METHOD System.Void .ctor()`

---

### `AIBT.BtNode`

Base of every managed behavior-tree node. Derive from <see cref="BtAction"/> or <see cref="BtCondition"/>, not from this class. One instance exists per node per agent, so ordinary private fields are safe per-agent state.

- `METHOD  GetComponent()`
- `PROPERTY AIBT.AgentBlackboard Blackboard`
- `PROPERTY AIBT.BehaviorTreeAgent Agent`
- `PROPERTY System.Single DeltaTime`
- `PROPERTY System.Single TreeTime`
- `PROPERTY System.String NodeId`
- `PROPERTY UnityEngine.GameObject gameObject`
- `PROPERTY UnityEngine.Transform transform`

---

### `AIBT.BtNodeAttribute`

Registers a <see cref="BtAction"/> or <see cref="BtCondition"/> class as a behavior-tree node. The type ID is the stable, lowercase, project-qualified name written into tree files, for example <c>game.move-to</c>. Renaming the class does not change it.

- `METHOD System.Void .ctor(System.String)`
- `PROPERTY System.String Category`
- `PROPERTY System.String Summary`
- `PROPERTY System.String TypeId`
- `PROPERTY System.String WhenNotToUse`
- `PROPERTY System.String WhenToUse`
- `PROPERTY System.UInt32 Version`

---

### `AIBT.BtParamAttribute`

Optional settings for a node parameter. Every public instance field of a node is already a parameter; use this only to rename it or change whether a tree must supply it.

- `METHOD System.Void .ctor()`
- `METHOD System.Void .ctor(System.String)`
- `PROPERTY System.Boolean Optional`
- `PROPERTY System.Boolean Required`
- `PROPERTY System.String Name`

---

### `AIBT.ConditionInterrupt`

What happens when a condition that already answered would now answer differently. A sequence or selector normally keeps running the child it chose; these modes make it choose again as soon as the condition changes, without waiting for that child to finish.

- `FIELD AIBT.ConditionInterrupt Both`
- `FIELD AIBT.ConditionInterrupt LowerPriority`
- `FIELD AIBT.ConditionInterrupt None`
- `FIELD AIBT.ConditionInterrupt Self`
- `FIELD System.Int32 value__`

---

### `AIBT.Nodes.AnimatorBoolNode`

- `FIELD System.Boolean value`
- `FIELD System.String parameter`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.AnimatorFloatNode`

- `FIELD AIBT.BbKey`1<System.Single> fromKey`
- `FIELD System.Single value`
- `FIELD System.String parameter`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.AnimatorTriggerNode`

- `FIELD System.String parameter`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CompareFloatKeysNode`

- `FIELD AIBT.BbKey`1<System.Single> left`
- `FIELD AIBT.BbKey`1<System.Single> right`
- `FIELD AIBT.Nodes.Comparison comparison`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CompareFloatNode`

- `FIELD AIBT.BbKey`1<System.Single> key`
- `FIELD AIBT.Nodes.Comparison comparison`
- `FIELD System.Single value`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CompareIntKeysNode`

- `FIELD AIBT.BbKey`1<System.Int32> left`
- `FIELD AIBT.BbKey`1<System.Int32> right`
- `FIELD AIBT.Nodes.Comparison comparison`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CompareIntNode`

- `FIELD AIBT.BbKey`1<System.Int32> key`
- `FIELD AIBT.Nodes.Comparison comparison`
- `FIELD System.Int32 value`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.Comparison`

- `FIELD AIBT.Nodes.Comparison Equal`
- `FIELD AIBT.Nodes.Comparison GreaterOrEqual`
- `FIELD AIBT.Nodes.Comparison GreaterThan`
- `FIELD AIBT.Nodes.Comparison LessOrEqual`
- `FIELD AIBT.Nodes.Comparison LessThan`
- `FIELD AIBT.Nodes.Comparison NotEqual`
- `FIELD System.Int32 value__`

---

### `AIBT.Nodes.CopyBoolNode`

- `FIELD AIBT.BbKey`1<System.Boolean> from`
- `FIELD AIBT.BbKey`1<System.Boolean> to`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CopyFloatNode`

- `FIELD AIBT.BbKey`1<System.Single> from`
- `FIELD AIBT.BbKey`1<System.Single> to`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CopyIntNode`

- `FIELD AIBT.BbKey`1<System.Int32> from`
- `FIELD AIBT.BbKey`1<System.Int32> to`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.CopyVector3Node`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> from`
- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> to`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.DestroySelfNode`

- `FIELD System.Single delay`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.DistanceNode`

- `FIELD AIBT.BbKey`1<System.Single> result`
- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> from`
- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> to`
- `FIELD System.Boolean flat`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.FailNode`

- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.GetPositionNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> result`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.HasReferenceNode`

- `FIELD System.Boolean requireActive`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IdleNode`

- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IncrementFloatNode`

- `FIELD AIBT.BbKey`1<System.Single> key`
- `FIELD System.Single amount`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IncrementIntNode`

- `FIELD AIBT.BbKey`1<System.Int32> key`
- `FIELD System.Int32 amount`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IsCooldownReadyNode`

- `FIELD AIBT.BbKey`1<System.Single> key`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IsTrueNode`

- `FIELD AIBT.BbKey`1<System.Boolean> key`
- `FIELD System.Boolean expected`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IsVector3SetNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> key`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.IsWithinDistanceNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> target`
- `FIELD System.Boolean keepHeight`
- `FIELD System.Single distance`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.KeepDistanceNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> target`
- `FIELD System.Boolean faceTarget`
- `FIELD System.Boolean keepHeight`
- `FIELD System.Single maxDistance`
- `FIELD System.Single minDistance`
- `FIELD System.Single speed`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.LineOfSightNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> target`
- `FIELD System.Int32 layerMask`
- `FIELD System.Single eyeHeight`
- `FIELD System.Single fieldOfView`
- `FIELD System.Single maxDistance`
- `FIELD System.Single targetHeight`
- `FIELD System.String targetReference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.LogLevel`

- `FIELD AIBT.Nodes.LogLevel Error`
- `FIELD AIBT.Nodes.LogLevel Info`
- `FIELD AIBT.Nodes.LogLevel Warning`
- `FIELD System.Int32 value__`

---

### `AIBT.Nodes.LogNode`

- `FIELD AIBT.Nodes.LogLevel level`
- `FIELD System.String message`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.LookAtNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> target`
- `FIELD System.Boolean keepHeight`
- `FIELD System.Single degreesPerSecond`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.MoveDirectionNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> direction`
- `FIELD System.Boolean faceDirection`
- `FIELD System.Single duration`
- `FIELD System.Single speed`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.MoveTowardsNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> target`
- `FIELD System.Boolean faceDirection`
- `FIELD System.Boolean keepHeight`
- `FIELD System.Single speed`
- `FIELD System.Single stopDistance`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.NavMoveToNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> target`
- `FIELD System.Single repathDistance`
- `FIELD System.Single speed`
- `FIELD System.Single stopDistance`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.NavStopNode`

- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.PlayAudioNode`

- `FIELD System.Boolean waitUntilFinished`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.RaiseEventNode`

- `FIELD System.String eventName`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.RandomChanceNode`

- `FIELD System.Single chance`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.RandomFloatNode`

- `FIELD AIBT.BbKey`1<System.Single> key`
- `FIELD System.Single max`
- `FIELD System.Single min`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.RandomIntNode`

- `FIELD AIBT.BbKey`1<System.Int32> key`
- `FIELD System.Int32 max`
- `FIELD System.Int32 min`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.RandomPointNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> center`
- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> result`
- `FIELD System.Boolean flat`
- `FIELD System.Single minRadius`
- `FIELD System.Single radius`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.RotateTowardsNode`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> direction`
- `FIELD System.Boolean keepUpright`
- `FIELD System.Single degreesPerSecond`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.SetActiveNode`

- `FIELD System.Boolean active`
- `FIELD System.String reference`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.SetBoolNode`

- `FIELD AIBT.BbKey`1<System.Boolean> key`
- `FIELD System.Boolean value`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.SetFloatNode`

- `FIELD AIBT.BbKey`1<System.Single> key`
- `FIELD System.Single value`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.SetIntNode`

- `FIELD AIBT.BbKey`1<System.Int32> key`
- `FIELD System.Int32 value`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.SetVector3Node`

- `FIELD AIBT.BbKey`1<UnityEngine.Vector3> key`
- `FIELD System.Single x`
- `FIELD System.Single y`
- `FIELD System.Single z`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.StartCooldownNode`

- `FIELD AIBT.BbKey`1<System.Single> key`
- `FIELD System.Single seconds`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.SucceedNode`

- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.TimeSinceChangedNode`

- `FIELD AIBT.BbAnyKey key`
- `FIELD AIBT.Nodes.Comparison comparison`
- `FIELD System.Single seconds`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.ToggleBoolNode`

- `FIELD AIBT.BbKey`1<System.Boolean> key`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.TurnByNode`

- `FIELD System.Single degreesPerSecond`
- `FIELD System.Single degrees`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.WaitFramesNode`

- `FIELD System.Int32 frames`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.WaitRandomNode`

- `FIELD System.Single maxSeconds`
- `FIELD System.Single minSeconds`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.WaitSecondsNode`

- `FIELD System.Single randomExtra`
- `FIELD System.Single seconds`
- `METHOD System.Void .ctor()`

---

### `AIBT.Nodes.WaitUntilNode`

- `FIELD AIBT.BbKey`1<System.Boolean> key`
- `FIELD System.Boolean expected`
- `FIELD System.Single timeoutSeconds`
- `METHOD System.Void .ctor()`
