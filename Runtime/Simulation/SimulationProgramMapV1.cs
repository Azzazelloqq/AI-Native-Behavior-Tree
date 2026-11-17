using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AIBT.AgentFrame;
using AIBT.Execution.Burst.Dispatch;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT.Simulation
{
    /// <summary>
    /// P10-010: the input and command channels one program uses, found from its generated leaves'
    /// catalog bindings and the catalog's binding ids (<see cref="IGeneratedBindingIdentityV1"/>).
    /// Channel indices follow first appearance in node order, so they are deterministic per program.
    /// </summary>
    internal sealed class SimulationProgramMapV1
    {
        internal struct Channel
        {
            internal ulong Id;
            /// <summary>A global catalog binding ordinal of this channel (all of them agree on type and layout).</summary>
            internal uint Binding;
            internal ulong TypeId;
            internal uint TypeVersion;
            internal uint CanonicalSize;
            internal int LeafCount;
        }

        /// <summary>
        /// P10-011: an external-action channel: one async-operation binding id, its start value
        /// (primary) and its cancel value (secondary).
        /// </summary>
        internal struct ActionChannel
        {
            internal ulong Id;
            internal uint Binding;
            internal ulong StartTypeId;
            internal uint StartTypeVersion;
            internal uint StartSize;
            internal ulong CancelTypeId;
            internal uint CancelTypeVersion;
            internal uint CancelSize;
        }

        private SimulationProgramMapV1(int bindingCount, int nodeCount)
        {
            InputChannelByBinding = new int[bindingCount];
            CommandChannelByBinding = new int[bindingCount];
            ActionChannelByBinding = new int[bindingCount];
            CompletionChannelByBinding = new int[bindingCount];
            for (var index = 0; index < bindingCount; index++)
            {
                InputChannelByBinding[index] = -1;
                CommandChannelByBinding[index] = -1;
                ActionChannelByBinding[index] = -1;
                CompletionChannelByBinding[index] = -1;
            }
            CompletionChannelsByNode = new int[nodeCount][];
        }

        /// <summary>By global catalog binding ordinal: the input channel of a SnapshotRead binding, else -1.</summary>
        internal int[] InputChannelByBinding { get; }
        /// <summary>By global catalog binding ordinal: the command channel of an EffectCommand binding, else -1.</summary>
        internal int[] CommandChannelByBinding { get; }
        /// <summary>P10-011: by global catalog binding ordinal: the action channel of an AsyncOperation binding, else -1.</summary>
        internal int[] ActionChannelByBinding { get; }
        /// <summary>P10-011: by global catalog binding ordinal: the completion channel of a Completion binding, else -1.</summary>
        internal int[] CompletionChannelByBinding { get; }
        /// <summary>P10-011: by node index, the completion channels the node declares (null for a node without completions).</summary>
        internal int[][] CompletionChannelsByNode { get; }
        internal List<Channel> Inputs { get; } = new List<Channel>();
        internal List<Channel> Commands { get; } = new List<Channel>();
        internal List<ActionChannel> Actions { get; } = new List<ActionChannel>();
        internal List<Channel> Completions { get; } = new List<Channel>();
        /// <summary>P10-011: the largest start, cancel and completion values of the program (an action record's payload stride).</summary>
        internal uint MaxStartSize { get; private set; }
        internal uint MaxCancelSize { get; private set; }
        internal uint MaxCompletionSize { get; private set; }
        /// <summary>Snapshot bindings resolve to target ordinals from here on, apart from blackboard slot indices.</summary>
        internal uint InputTargetBase { get; private set; }
        /// <summary>The catalog has effect bindings but no binding ids (generated before P10-010): commands are discarded.</summary>
        internal bool CommandsWithoutIds { get; private set; }

        internal int FindInput(ulong id) => Find(Inputs, id);
        internal int FindCommand(ulong id) => Find(Commands, id);

        internal int FindAction(ulong id)
        {
            for (var index = 0; index < Actions.Count; index++)
                if (Actions[index].Id == id) return index;
            return -1;
        }

        private static int Find(List<Channel> channels, ulong id)
        {
            for (var index = 0; index < channels.Count; index++)
                if (channels[index].Id == id) return index;
            return -1;
        }

        /// <summary>
        /// Builds the map, or refuses: <see cref="SimulationResult.CatalogTooOld"/> when the program has
        /// input, async-operation or completion bindings and the catalog has no binding ids (P10-011: a
        /// program with external actions on such a catalog is refused, never run with its actions dropped),
        /// <see cref="SimulationResult.TypeMismatch"/> when two bindings of one channel id disagree on type.
        /// </summary>
        internal static SimulationResult TryBuild(ProductionTreeProgram program, out SimulationProgramMapV1 map)
        {
            map = null;
            var catalog = program.Catalog;
            var binding = program.Definition.Binding;
            var nodes = binding.SemanticProgram.Nodes;
            var created = new SimulationProgramMapV1(catalog.Bindings.Length, nodes.Count) { InputTargetBase = (uint)binding.Slots.Count };
            var identity = catalog.Executor as IGeneratedBindingIdentityV1;
            var nodeCompletions = new List<int>();
            for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                if (NativeHotReloadInstance.ClassifyKind(node.NodeTypeId) != NativeLifecycleNodeKindV1.GeneratedLeaf) continue;
                if (!catalog.TryFindCase(node.NodeTypeId, node.NodeTypeVersion, out var caseIndex)) continue;
                var dispatchCase = catalog.Cases[(int)caseIndex];
                nodeCompletions.Clear();
                for (uint local = 0; local < dispatchCase.BindingCount; local++)
                {
                    var global = dispatchCase.FirstBinding + local;
                    var entry = catalog.Bindings[(int)global];
                    switch (entry.Kind)
                    {
                        case NativeBurstDispatchBindingKindV2.AsyncOperation:
                        {
                            if (identity == null || identity.BindingIdCount != catalog.Bindings.Length) return SimulationResult.CatalogTooOld;
                            if (!identity.TryGetBindingId(global, out var actionId)) return SimulationResult.CatalogTooOld;
                            var action = created.FindAction(actionId);
                            if (action < 0)
                            {
                                action = created.Actions.Count;
                                created.Actions.Add(new ActionChannel
                                {
                                    Id = actionId, Binding = global,
                                    StartTypeId = entry.PrimaryTypeNumericId, StartTypeVersion = entry.PrimaryTypeVersion, StartSize = entry.PrimaryValueSize,
                                    CancelTypeId = entry.SecondaryTypeNumericId, CancelTypeVersion = entry.SecondaryTypeVersion, CancelSize = entry.SecondaryValueSize,
                                });
                            }
                            else
                            {
                                var known = created.Actions[action];
                                if (known.StartTypeId != entry.PrimaryTypeNumericId || known.StartTypeVersion != entry.PrimaryTypeVersion
                                    || known.StartSize != entry.PrimaryValueSize || known.CancelTypeId != entry.SecondaryTypeNumericId
                                    || known.CancelTypeVersion != entry.SecondaryTypeVersion || known.CancelSize != entry.SecondaryValueSize)
                                    return SimulationResult.TypeMismatch;
                            }
                            created.ActionChannelByBinding[global] = action;
                            if (entry.PrimaryValueSize > created.MaxStartSize) created.MaxStartSize = entry.PrimaryValueSize;
                            if (entry.SecondaryValueSize > created.MaxCancelSize) created.MaxCancelSize = entry.SecondaryValueSize;
                            break;
                        }
                        case NativeBurstDispatchBindingKindV2.Completion:
                        {
                            if (identity == null || identity.BindingIdCount != catalog.Bindings.Length) return SimulationResult.CatalogTooOld;
                            if (!identity.TryGetBindingId(global, out var completionId)) return SimulationResult.CatalogTooOld;
                            var completion = Find(created.Completions, completionId);
                            var completionLeaves = 0;
                            for (uint field = 0; field < entry.PrimaryValueFieldCount; field++)
                                completionLeaves += (int)catalog.ValueFields[(int)(entry.FirstPrimaryValueField + field)].ElementCount;
                            if (completion < 0)
                            {
                                completion = created.Completions.Count;
                                created.Completions.Add(new Channel
                                {
                                    Id = completionId, Binding = global, TypeId = entry.PrimaryTypeNumericId, TypeVersion = entry.PrimaryTypeVersion,
                                    CanonicalSize = entry.PrimaryValueSize, LeafCount = completionLeaves,
                                });
                            }
                            else
                            {
                                var known = created.Completions[completion];
                                if (known.TypeId != entry.PrimaryTypeNumericId || known.TypeVersion != entry.PrimaryTypeVersion
                                    || known.CanonicalSize != entry.PrimaryValueSize || known.LeafCount != completionLeaves)
                                    return SimulationResult.TypeMismatch;
                            }
                            created.CompletionChannelByBinding[global] = completion;
                            if (!nodeCompletions.Contains(completion)) nodeCompletions.Add(completion);
                            if (entry.PrimaryValueSize > created.MaxCompletionSize) created.MaxCompletionSize = entry.PrimaryValueSize;
                            break;
                        }
                        case NativeBurstDispatchBindingKindV2.SnapshotRead:
                        case NativeBurstDispatchBindingKindV2.EffectCommand:
                            if (identity == null && entry.Kind == NativeBurstDispatchBindingKindV2.EffectCommand)
                            {
                                // A catalog generated before P10-010: its effects are discarded as in G1.
                                created.CommandsWithoutIds = true;
                                break;
                            }
                            if (identity == null || identity.BindingIdCount != catalog.Bindings.Length) return SimulationResult.CatalogTooOld;
                            if (!identity.TryGetBindingId(global, out var id)) return SimulationResult.CatalogTooOld;
                            var input = entry.Kind == NativeBurstDispatchBindingKindV2.SnapshotRead;
                            var channels = input ? created.Inputs : created.Commands;
                            var channel = Find(channels, id);
                            var leafCount = 0;
                            for (uint field = 0; field < entry.PrimaryValueFieldCount; field++)
                                leafCount += (int)catalog.ValueFields[(int)(entry.FirstPrimaryValueField + field)].ElementCount;
                            if (channel < 0)
                            {
                                channel = channels.Count;
                                channels.Add(new Channel
                                {
                                    Id = id, Binding = global, TypeId = entry.PrimaryTypeNumericId, TypeVersion = entry.PrimaryTypeVersion,
                                    CanonicalSize = entry.PrimaryValueSize, LeafCount = leafCount,
                                });
                            }
                            else
                            {
                                var known = channels[channel];
                                if (known.TypeId != entry.PrimaryTypeNumericId || known.TypeVersion != entry.PrimaryTypeVersion
                                    || known.CanonicalSize != entry.PrimaryValueSize || known.LeafCount != leafCount)
                                    return SimulationResult.TypeMismatch;
                            }
                            if (input) created.InputChannelByBinding[global] = channel;
                            else created.CommandChannelByBinding[global] = channel;
                            break;
                    }
                }
                if (nodeCompletions.Count != 0) created.CompletionChannelsByNode[nodeIndex] = nodeCompletions.ToArray();
            }
            map = created;
            return SimulationResult.Success;
        }
    }

    /// <summary>
    /// P10-010: matches a game type <c>T</c> to a catalog binding's value (type id, version and every
    /// leaf of the canonical layout) and returns the raw-to-canonical leaf table. Reflection runs only
    /// here, in setup calls (bind, first command read); never inside a step.
    /// </summary>
    internal static class SimulationValueLayoutV1
    {
        private struct RawLeaf
        {
            internal uint Offset;
            internal uint Size;
        }

        /// <summary>
        /// The layout of the binding's primary value, or with <paramref name="secondary"/> its secondary
        /// value (P10-011: the cancel value of an async-operation binding).
        /// </summary>
        internal static SimulationResult TryBuild<T>(
            GeneratedBurstCatalogV2 catalog, uint globalBinding, List<SimulationLeafV1> leaves, bool secondary = false)
            where T : unmanaged
        {
            leaves.Clear();
            var binding = catalog.Bindings[(int)globalBinding];
            var expectedTypeId = secondary ? binding.SecondaryTypeNumericId : binding.PrimaryTypeNumericId;
            var expectedVersion = secondary ? binding.SecondaryTypeVersion : binding.PrimaryTypeVersion;
            var firstField = secondary ? binding.FirstSecondaryValueField : binding.FirstPrimaryValueField;
            var fieldCount = secondary ? binding.SecondaryValueFieldCount : binding.PrimaryValueFieldCount;
            var valueSize = secondary ? binding.SecondaryValueSize : binding.PrimaryValueSize;
            var type = typeof(T);
            if (!TryIdentity(type, out var typeId, out var typeVersion)
                || StableHash.Fnv1A64(typeId) != expectedTypeId || typeVersion != expectedVersion)
                return SimulationResult.TypeMismatch;
            var rawFields = new List<List<RawLeaf>>();
            if (IsRegistered(type))
            {
                foreach (var field in OrderedFields(type))
                {
                    var fieldLeaves = new List<RawLeaf>();
                    if (!TryFlatten(field.FieldType, (uint)UnsafeUtility.GetFieldOffset(field), fieldLeaves)) return SimulationResult.TypeMismatch;
                    rawFields.Add(fieldLeaves);
                }
            }
            else
            {
                var single = new List<RawLeaf>();
                if (!TryFlatten(type, 0u, single)) return SimulationResult.TypeMismatch;
                rawFields.Add(single);
            }

            var used = 0;
            var rawSize = (uint)UnsafeUtility.SizeOf<T>();
            for (uint index = 0; index < fieldCount; index++)
            {
                var field = catalog.ValueFields[(int)(firstField + index)];
                if (field.CanonicalRuleKind != NativeBurstDispatchCanonicalRuleKindV2.None
                    || field.Encoding == NativeBurstDispatchFieldEncodingV2.GeneratedHandle
                    || field.FieldOrdinal >= (uint)rawFields.Count)
                    return SimulationResult.TypeMismatch;
                var fieldLeaves = rawFields[(int)field.FieldOrdinal];
                for (uint element = 0; element < field.ElementCount; element++)
                {
                    var leafIndex = field.FirstElementIndex + element;
                    if (leafIndex >= (uint)fieldLeaves.Count) return SimulationResult.TypeMismatch;
                    var raw = fieldLeaves[(int)leafIndex];
                    var canonical = field.ByteOffset + element * field.ElementSize;
                    if (raw.Size != field.ElementSize || raw.Offset + raw.Size > rawSize
                        || canonical + field.ElementSize > valueSize)
                        return SimulationResult.TypeMismatch;
                    leaves.Add(new SimulationLeafV1 { RawOffset = raw.Offset, CanonicalOffset = canonical, Size = raw.Size, Encoding = field.Encoding });
                    used++;
                }
            }
            var total = 0;
            for (var index = 0; index < rawFields.Count; index++) total += rawFields[index].Count;
            if (used != total) return SimulationResult.TypeMismatch;
            return SimulationResult.Success;
        }

        private static bool IsRegistered(Type type) => FindAttribute(type.GetCustomAttributesData(), "AIBT.Burst.AibtBurstValueAttribute") != null;

        /// <summary>P10-011: the value type id hash and version a binding of <paramref name="type"/> declares (registered or built-in), else false.</summary>
        internal static bool TryNumericIdentity(Type type, out ulong id, out uint version)
        {
            id = 0;
            if (!TryIdentity(type, out var text, out version)) return false;
            id = StableHash.Fnv1A64(text);
            return true;
        }

        private static bool TryIdentity(Type type, out string id, out uint version)
        {
            version = 1u;
            var registered = FindAttribute(type.GetCustomAttributesData(), "AIBT.Burst.AibtBurstValueAttribute");
            if (registered != null)
            {
                id = registered.ConstructorArguments.Count > 0 ? registered.ConstructorArguments[0].Value as string : null;
                if (registered.ConstructorArguments.Count > 1 && registered.ConstructorArguments[1].Value is uint registeredVersion)
                    version = registeredVersion;
                return id != null;
            }
            id = BuiltInId(type);
            return id != null;
        }

        private static string BuiltInId(Type type)
        {
            if (type == typeof(bool)) return "Bool";
            if (type == typeof(sbyte)) return "Int8";
            if (type == typeof(byte)) return "UInt8";
            if (type == typeof(short)) return "Int16";
            if (type == typeof(ushort)) return "UInt16";
            if (type == typeof(int)) return "Int32";
            if (type == typeof(uint)) return "UInt32";
            if (type == typeof(long)) return "Int64";
            if (type == typeof(ulong)) return "UInt64";
            if (type == typeof(float)) return "Float32";
            if (type == typeof(double)) return "Float64";
            if (type == typeof(Float2Value)) return "Float2";
            if (type == typeof(Float3Value)) return "Float3";
            if (type == typeof(QuaternionValue)) return "Quaternion";
            return null;
        }

        /// <summary>The generator's FlattenRuntimeValue, over the raw C# layout.</summary>
        private static bool TryFlatten(Type type, uint offset, List<RawLeaf> leaves)
        {
            if (type.IsPrimitive)
            {
                if (type == typeof(char) || type == typeof(IntPtr) || type == typeof(UIntPtr)) return false;
                leaves.Add(new RawLeaf { Offset = offset, Size = (uint)(type == typeof(bool) ? 1 : System.Runtime.InteropServices.Marshal.SizeOf(type)) });
                return true;
            }
            var floats = type == typeof(Float2Value) ? 2 : type == typeof(Float3Value) ? 3 : type == typeof(QuaternionValue) ? 4 : 0;
            if (floats != 0)
            {
                if (UnsafeUtility.SizeOf(type) != floats * 4) return false;
                for (var index = 0; index < floats; index++) leaves.Add(new RawLeaf { Offset = offset + (uint)index * 4u, Size = 4u });
                return true;
            }
            if (type.IsValueType && IsRegistered(type))
            {
                foreach (var field in OrderedFields(type))
                    if (!TryFlatten(field.FieldType, offset + (uint)UnsafeUtility.GetFieldOffset(field), leaves)) return false;
                return true;
            }
            return false;
        }

        /// <summary>Instance fields ordered as the generator orders them: by stable field id (the AibtValueField id, else the name), UTF-8 ordinal.</summary>
        private static List<FieldInfo> OrderedFields(Type type)
        {
            var fields = new List<FieldInfo>(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            fields.Sort((left, right) => CompareUtf8(StableId(left), StableId(right)));
            return fields;
        }

        private static string StableId(FieldInfo field)
        {
            var attribute = FindAttribute(field.GetCustomAttributesData(), "AIBT.Burst.AibtValueFieldAttribute");
            return attribute != null && attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is string id ? id : field.Name;
        }

        private static int CompareUtf8(string left, string right)
        {
            var a = Encoding.UTF8.GetBytes(left);
            var b = Encoding.UTF8.GetBytes(right);
            var length = Math.Min(a.Length, b.Length);
            for (var index = 0; index < length; index++)
                if (a[index] != b[index]) return a[index].CompareTo(b[index]);
            return a.Length.CompareTo(b.Length);
        }

        private static CustomAttributeData FindAttribute(IList<CustomAttributeData> attributes, string fullName)
        {
            for (var index = 0; index < attributes.Count; index++)
                if (attributes[index].AttributeType.FullName == fullName) return attributes[index];
            return null;
        }
    }
}
