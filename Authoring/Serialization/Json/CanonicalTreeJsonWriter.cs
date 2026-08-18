using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AIBT.Authoring
{
    internal sealed class CanonicalTreeJsonWriter
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly bool _semanticOnly;
        private int _depth;
        private int _formatVersion;

        private CanonicalTreeJsonWriter(bool semanticOnly)
        {
            _semanticOnly = semanticOnly;
        }

        public static byte[] Write(TreeDocument document, bool semanticOnly)
        {
            var writer = new CanonicalTreeJsonWriter(semanticOnly);
            writer.WriteDocument(document);
            writer._builder.Append('\n');
            return new UTF8Encoding(false, true).GetBytes(writer._builder.ToString());
        }

        private void WriteDocument(TreeDocument document)
        {
            _formatVersion = document.FormatVersion;
            BeginObject();
            var first = true;
            Property(ref first, "format", () => String(document.Format));
            Property(ref first, "formatVersion", () => Integer(document.FormatVersion));
            Property(ref first, "treeId", () => String(document.TreeId.Value));
            if (!_semanticOnly) Property(ref first, "name", () => String(document.Name));
            if (!_semanticOnly && document.Description != null)
                Property(ref first, "description", () => String(document.Description));
            Property(ref first, "root", () => String(document.Root.Value));
            if (document.FormatVersion == TreeDocument.LatestFormatVersion
                && (document.AgentContract != null || document.SharedContract != null))
                Property(ref first, "blackboardContracts", () => WriteScopeContracts(document));
            if (document.Blackboard.Count > 0)
                Property(ref first, "blackboard", () => WriteBlackboard(document.Blackboard));
            Property(ref first, "nodes", () => WriteNodes(document.Nodes));
            if (!_semanticOnly && document.Tags.Values.Count > 0)
                Property(ref first, "tags", () => WriteSortedStrings(document.Tags.Values));
            if (!_semanticOnly && document.Metadata.Properties.Count > 0)
                Property(ref first, "metadata", () => WriteSemanticObject(document.Metadata));
            EndObject(first);
        }

        private void WriteBlackboard(IReadOnlyList<BlackboardKeyDefinition> keys)
        {
            var ordered = new List<BlackboardKeyDefinition>(keys);
            ordered.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id, right.Id));
            BeginObject();
            var first = true;
            foreach (var key in ordered)
                Property(ref first, key.Id, () => WriteBlackboardEntry(key));
            EndObject(first);
        }

        private void WriteBlackboardEntry(BlackboardKeyDefinition key)
        {
            BeginObject();
            var first = true;
            Property(ref first, "type", () => String(key.Type.CanonicalTypeId));
            if (_formatVersion == TreeDocument.LatestFormatVersion
                && key.Scope != BlackboardScope.Tree && key.Type.RuntimeDescriptor.Version != 0
                && key.Type.IsValid)
                Property(ref first, "typeVersion", () => Integer(key.Type.RuntimeDescriptor.Version));
            if (key.Type.ValueType == BlackboardValueType.Enum32)
                Property(ref first, "enumContract", () => String(key.Type.EnumContract));
            if (key.Scope != BlackboardScope.Tree)
                Property(ref first, "scope", () => String(ScopeText(key.Scope)));
            if (key.Reduction != BlackboardReductionKind.None)
                Property(ref first, "reduction", () => WriteReduction(key.Reduction));
            if (!_semanticOnly && key.Description != null)
                Property(ref first, "description", () => String(key.Description));
            if (key.HasDefault)
                Property(ref first, "default", () => WriteDefault(key.DefaultValue));
            EndObject(first);
        }

        private void WriteNodes(IReadOnlyList<NodeDocument> nodes)
        {
            var ordered = new List<NodeDocument>(nodes);
            ordered.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Id.Value, right.Id.Value));
            BeginObject();
            var first = true;
            foreach (var node in ordered)
                Property(ref first, node.Id.Value, () => WriteNode(node));
            EndObject(first);
        }

        private void WriteNode(NodeDocument node)
        {
            BeginObject();
            var first = true;
            Property(ref first, "type", () => String(node.TypeId));
            Property(ref first, "typeVersion", () => Integer(node.TypeVersion));
            if (!_semanticOnly && node.DisplayName != null)
                Property(ref first, "displayName", () => String(node.DisplayName));
            if (!_semanticOnly && node.Description != null)
                Property(ref first, "description", () => String(node.Description));
            if (node.Parameters.Properties.Count > 0)
                Property(ref first, "parameters", () => WriteSemanticObject(node.Parameters));
            if (node.Bindings != null)
                Property(ref first, "bindings", () => WriteBindings(node.Bindings));
            if (node.Observer != null)
                Property(ref first, "observer", () => WriteObserver(node.Observer));
            if (node.Children.Count > 0)
                Property(ref first, "children", () => WriteIds(node.Children));
            if (!_semanticOnly && node.Tags.Values.Count > 0)
                Property(ref first, "tags", () => WriteSortedStrings(node.Tags.Values));
            EndObject(first);
        }

        private void WriteObserver(NodeObserver observer)
        {
            BeginObject();
            var first = true;
            Property(ref first, "mode", () => String(observer.Mode));
            Property(ref first, "watchedKeys", () => WriteStrings(observer.WatchedKeys));
            EndObject(first);
        }

        private void WriteScopeContracts(TreeDocument document)
        {
            BeginObject();
            var first = true;
            if (document.AgentContract != null)
                Property(ref first, "agent", () => WriteScopeContract(document.AgentContract));
            if (document.SharedContract != null)
                Property(ref first, "shared", () => WriteScopeContract(document.SharedContract));
            EndObject(first);
        }

        private void WriteScopeContract(BlackboardScopeContract contract)
        {
            BeginObject();
            var first = true;
            Property(ref first, "contractId", () => String(contract.ContractId));
            Property(ref first, "contractVersion", () => Integer(contract.ContractVersion));
            EndObject(first);
        }

        private void WriteBindings(NodeBindingMap bindings)
        {
            var ordered = new List<KeyValuePair<string, string>>(bindings.Values);
            ordered.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Key, right.Key));
            BeginObject();
            var first = true;
            for (var index = 0; index < ordered.Count; index++)
            {
                var pair = ordered[index];
                Property(ref first, pair.Key, () => String(pair.Value));
            }
            EndObject(first);
        }

        private void WriteReduction(BlackboardReductionKind reduction)
        {
            BeginObject();
            var first = true;
            Property(ref first, "kind", () => String(ReductionText(reduction)));
            EndObject(first);
        }

        private void WriteDefault(BlackboardDefaultValue value)
        {
            if (value.ValueType == BlackboardValueType.Registered && value.TryGetRegisteredValue(out var registered))
            {
                WriteSemanticObject(registered);
                return;
            }
            if (!value.TryGetRuntimeValue(out var runtime))
                throw new InvalidOperationException("A default value is not representable by the built-in canonical writer.");

            switch (runtime.Type)
            {
                case BlackboardValueType.Bool:
                    runtime.TryGetBool(out var boolean); Boolean(boolean); return;
                case BlackboardValueType.Int32:
                    runtime.TryGetInt32(out var int32); Integer(int32); return;
                case BlackboardValueType.Int64:
                    runtime.TryGetInt64(out var int64); Integer(int64); return;
                case BlackboardValueType.Float32:
                    runtime.TryGetFloat32(out var float32); Number(float32); return;
                case BlackboardValueType.Float64:
                    runtime.TryGetFloat64(out var float64); Number(float64); return;
                case BlackboardValueType.Float2:
                    runtime.TryGetFloat2(out var float2); WriteVector(float2.X, float2.Y); return;
                case BlackboardValueType.Float3:
                    runtime.TryGetFloat3(out var float3); WriteVector(float3.X, float3.Y, float3.Z); return;
                case BlackboardValueType.Quaternion:
                    runtime.TryGetQuaternion(out var quaternion); WriteVector(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W); return;
                case BlackboardValueType.Enum32:
                    runtime.TryGetEnum32(out var enum32); WriteEnum(value.EnumContract, enum32.Value); return;
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512:
                    runtime.TryGetFixedString(out var text); String(text); return;
                case BlackboardValueType.AgentId:
                    runtime.TryGetAgentId(out var agentId); String(agentId.ToString()); return;
                case BlackboardValueType.EntityId:
                    runtime.TryGetEntityId(out var entityId); String(entityId.ToString()); return;
                case BlackboardValueType.OperationId:
                    runtime.TryGetOperationId(out var operationId); String(operationId.ToString()); return;
                case BlackboardValueType.AssetId:
                    runtime.TryGetAssetId(out var assetId); WriteAssetId(assetId); return;
                default:
                    throw new InvalidOperationException("Unsupported canonical blackboard value type.");
            }
        }

        private void WriteVector(params float[] components)
        {
            BeginObject();
            var first = true;
            var names = new[] { "x", "y", "z", "w" };
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                Property(ref first, names[index], () => Number(component));
            }
            EndObject(first);
        }

        private void WriteEnum(string contract, int value)
        {
            BeginObject();
            var first = true;
            Property(ref first, "contract", () => String(contract));
            Property(ref first, "value", () => Integer(value));
            EndObject(first);
        }

        private void WriteAssetId(AssetId value)
        {
            BeginObject();
            var first = true;
            Property(ref first, "guid", () => String(value.ToGuidString()));
            if (value.HasLocalFileId)
                Property(ref first, "localFileId", () => Integer(value.LocalFileId));
            EndObject(first);
        }

        private void WriteSemanticObject(SemanticObject value)
        {
            var ordered = new List<SemanticProperty>(value.Properties);
            ordered.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.Name, right.Name));
            BeginObject();
            var first = true;
            foreach (var property in ordered)
                Property(ref first, property.Name, () => WriteSemanticValue(property.Value));
            EndObject(first);
        }

        private void WriteSemanticValue(SemanticValue value)
        {
            switch (value.Kind)
            {
                case SemanticValueKind.Null: _builder.Append("null"); return;
                case SemanticValueKind.Boolean:
                    value.TryGetBoolean(out var boolean); Boolean(boolean); return;
                case SemanticValueKind.SignedInteger:
                    value.TryGetInt64(out var signed); Integer(signed); return;
                case SemanticValueKind.UnsignedInteger:
                    value.TryGetUInt64(out var unsigned); Integer(unsigned); return;
                case SemanticValueKind.Number:
                    value.TryGetNumber(out var number); Number(number); return;
                case SemanticValueKind.String:
                    value.TryGetString(out var text); String(text); return;
                case SemanticValueKind.Array:
                    value.TryGetArray(out var array); WriteSemanticArray(array); return;
                case SemanticValueKind.Object:
                    value.TryGetObject(out var @object); WriteSemanticObject(@object); return;
                default: throw new InvalidOperationException("Unknown semantic value kind.");
            }
        }

        private void WriteSemanticArray(IReadOnlyList<SemanticValue> values)
        {
            BeginArray();
            for (var index = 0; index < values.Count; index++)
            {
                Element(index == 0, () => WriteSemanticValue(values[index]));
            }
            EndArray(values.Count == 0);
        }

        private void WriteIds(IReadOnlyList<NodeId> values)
        {
            BeginArray();
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index].Value;
                Element(index == 0, () => String(value));
            }
            EndArray(values.Count == 0);
        }

        private void WriteStrings(IReadOnlyList<string> values)
        {
            BeginArray();
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                Element(index == 0, () => String(value));
            }
            EndArray(values.Count == 0);
        }

        private void WriteSortedStrings(IReadOnlyList<string> values)
        {
            var ordered = new List<string>(values);
            ordered.Sort(Utf8OrdinalComparer.Instance);
            WriteStrings(ordered);
        }

        private void Property(ref bool first, string name, Action writeValue)
        {
            if (!first) _builder.Append(',');
            _builder.Append('\n');
            Indent();
            String(name);
            _builder.Append(": ");
            writeValue();
            first = false;
        }

        private void Element(bool first, Action writeValue)
        {
            if (!first) _builder.Append(',');
            _builder.Append('\n');
            Indent();
            writeValue();
        }

        private void BeginObject() { _builder.Append('{'); _depth++; }
        private void EndObject(bool empty)
        {
            _depth--;
            if (!empty) { _builder.Append('\n'); Indent(); }
            _builder.Append('}');
        }
        private void BeginArray() { _builder.Append('['); _depth++; }
        private void EndArray(bool empty)
        {
            _depth--;
            if (!empty) { _builder.Append('\n'); Indent(); }
            _builder.Append(']');
        }
        private void Indent() => _builder.Append(' ', _depth * 2);
        private void Boolean(bool value) => _builder.Append(value ? "true" : "false");
        private void Integer(long value) => _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        private void Integer(ulong value) => _builder.Append(value.ToString(CultureInfo.InvariantCulture));
        private void Number(float value) => RawNumber(value == 0f ? "0" : value.ToString("R", CultureInfo.InvariantCulture));
        private void Number(double value) => RawNumber(value == 0d ? "0" : value.ToString("R", CultureInfo.InvariantCulture));

        private void RawNumber(string value)
        {
            var exponent = value.IndexOfAny(new[] { 'E', 'e' });
            if (exponent < 0) { _builder.Append(value); return; }
            _builder.Append(value, 0, exponent);
            _builder.Append('e');
            var index = exponent + 1;
            if (index < value.Length && value[index] == '+') index++;
            if (index < value.Length && value[index] == '-') { _builder.Append('-'); index++; }
            while (index + 1 < value.Length && value[index] == '0') index++;
            _builder.Append(value, index, value.Length - index);
        }

        private void String(string value)
        {
            _builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"': _builder.Append("\\\""); break;
                    case '\\': _builder.Append("\\\\"); break;
                    case '\b': _builder.Append("\\b"); break;
                    case '\f': _builder.Append("\\f"); break;
                    case '\n': _builder.Append("\\n"); break;
                    case '\r': _builder.Append("\\r"); break;
                    case '\t': _builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                            _builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _builder.Append(character);
                        break;
                }
            }
            _builder.Append('"');
        }

        private static string ScopeText(BlackboardScope scope)
        {
            switch (scope)
            {
                case BlackboardScope.Tree: return "tree";
                case BlackboardScope.Agent: return "agent";
                case BlackboardScope.Shared: return "shared";
                default: throw new InvalidOperationException("Node-local and unknown scopes are not persisted at tree level.");
            }
        }

        private static string ReductionText(BlackboardReductionKind reduction)
        {
            switch (reduction)
            {
                case BlackboardReductionKind.Min: return "min";
                case BlackboardReductionKind.Max: return "max";
                case BlackboardReductionKind.Sum: return "sum";
                case BlackboardReductionKind.Any: return "any";
                case BlackboardReductionKind.All: return "all";
                case BlackboardReductionKind.First: return "first";
                case BlackboardReductionKind.Last: return "last";
                default: throw new InvalidOperationException("Unknown Shared reduction kind.");
            }
        }
    }
}
