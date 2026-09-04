using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// JSON &lt;-&gt; authoring-model conversion for MCP authoring tool arguments and responses.
    /// Mirrors <c>CanonicalTreeJson</c>'s own JSON-value mapping (that type's own conversion
    /// methods are private to <c>AIBT.Authoring</c>), scoped to what authoring tool payloads need:
    /// node definitions, parameters, and blackboard-key declarations. Never used for the on-disk
    /// canonical format itself -- <see cref="TreeDocumentPersistence"/> always goes through the
    /// real <c>CanonicalTreeJson</c> for that.
    /// </summary>
    internal static class McpAuthoringJson
    {
        internal static SemanticObject ReadParameters(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return SemanticObject.Empty;
            }

            return ReadSemanticObject((JObject)token);
        }

        /// <summary>Reads one arbitrary JSON value (e.g. a <c>configure_node</c> tool's <c>value</c> argument) as a <see cref="SemanticValue"/>.</summary>
        internal static SemanticValue ReadValue(JToken token)
        {
            if (token == null)
            {
                throw new FormatException("A value is required.");
            }

            return ReadSemanticValue(token);
        }

        private static SemanticObject ReadSemanticObject(JObject value)
        {
            var properties = new List<SemanticProperty>();
            foreach (var property in value.Properties())
            {
                properties.Add(new SemanticProperty(property.Name, ReadSemanticValue(property.Value)));
            }

            return new SemanticObject(properties);
        }

        private static SemanticValue ReadSemanticValue(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Null:
                    return SemanticValue.Null;
                case JTokenType.Boolean:
                    return SemanticValue.FromBoolean(token.Value<bool>());
                case JTokenType.Integer:
                    return SemanticValue.FromInt64(token.Value<long>());
                case JTokenType.Float:
                    return SemanticValue.FromNumber(token.Value<double>());
                case JTokenType.String:
                    return SemanticValue.FromString(token.Value<string>());
                case JTokenType.Array:
                    return SemanticValue.FromArray(((JArray)token).Select(ReadSemanticValue));
                case JTokenType.Object:
                    return SemanticValue.FromObject(ReadSemanticObject((JObject)token));
                default:
                    throw new FormatException("Unsupported parameter JSON value type: " + token.Type);
            }
        }

        internal static JObject WriteParameters(SemanticObject value)
        {
            var result = new JObject();
            foreach (var property in value.Properties)
            {
                result[property.Name] = WriteSemanticValue(property.Value);
            }

            return result;
        }

        private static JToken WriteSemanticValue(SemanticValue value)
        {
            switch (value.Kind)
            {
                case SemanticValueKind.Null:
                    return JValue.CreateNull();
                case SemanticValueKind.Boolean:
                    value.TryGetBoolean(out var boolean);
                    return boolean;
                case SemanticValueKind.SignedInteger:
                    value.TryGetInt64(out var signed);
                    return signed;
                case SemanticValueKind.UnsignedInteger:
                    value.TryGetUInt64(out var unsigned);
                    return unsigned;
                case SemanticValueKind.Number:
                    value.TryGetNumber(out var number);
                    return number;
                case SemanticValueKind.String:
                    value.TryGetString(out var text);
                    return text;
                case SemanticValueKind.Array:
                    value.TryGetArray(out var array);
                    return new JArray(array.Select(WriteSemanticValue));
                case SemanticValueKind.Object:
                    value.TryGetObject(out var obj);
                    return WriteParameters(obj);
                default:
                    throw new InvalidOperationException("Unknown semantic value kind: " + value.Kind);
            }
        }

        /// <summary>
        /// Reads one authoring-payload node definition: <c>{id, typeId, typeVersion, parameters?,
        /// displayName?, description?, tags?, children?}</c>. Used both for a freshly authored
        /// node (no children) and for round-tripping an extract/inline subtree payload (children
        /// preserved verbatim as authored NodeIds -- fidelity for the round-trip proof).
        /// </summary>
        internal static NodeDocument ReadNode(JObject json)
        {
            var id = new NodeId(RequireString(json, "id"));
            var typeId = RequireString(json, "typeId");
            var typeVersion = json["typeVersion"]?.Value<int>() ?? 1;
            var parameters = ReadParameters(json["parameters"]);
            var displayName = json["displayName"]?.Value<string>();
            var description = json["description"]?.Value<string>();
            var tags = json["tags"] != null
                ? new TagSet(((JArray)json["tags"]).Select(t => t.Value<string>()))
                : TagSet.Empty;
            var children = json["children"] != null
                ? ((JArray)json["children"]).Select(t => new NodeId(t.Value<string>()))
                : Enumerable.Empty<NodeId>();
            var observer = json["observer"] != null && json["observer"].Type != JTokenType.Null
                ? ReadObserver((JObject)json["observer"])
                : null;
            var bindings = json["bindings"] != null && json["bindings"].Type != JTokenType.Null
                ? ReadBindings((JObject)json["bindings"])
                : null;

            return new NodeDocument(id, typeId, typeVersion, children, parameters, observer, displayName, description, tags, bindings);
        }

        internal static JObject WriteNode(NodeDocument node)
        {
            var json = new JObject
            {
                ["id"] = node.Id.Value,
                ["typeId"] = node.TypeId,
                ["typeVersion"] = node.TypeVersion,
                ["parameters"] = WriteParameters(node.Parameters ?? SemanticObject.Empty),
                ["displayName"] = node.DisplayName,
                ["description"] = node.Description,
                ["tags"] = new JArray((node.Tags ?? TagSet.Empty).Values),
                ["children"] = new JArray(node.Children.Select(id => id.Value)),
            };

            if (node.Observer != null)
            {
                json["observer"] = WriteObserver(node.Observer);
            }

            if (node.Bindings != null)
            {
                json["bindings"] = WriteBindings(node.Bindings);
            }

            return json;
        }

        /// <summary>
        /// Mirrors <c>CanonicalTreeJson</c>'s own (private) observer JSON shape:
        /// <c>{mode, watchedKeys}</c>. Round-tripped verbatim -- authoring payloads never invent a
        /// different shape than the canonical on-disk format uses for the same concept.
        /// </summary>
        private static NodeObserver ReadObserver(JObject json)
        {
            var mode = RequireString(json, "mode");
            if (mode != "self" && mode != "lower-priority" && mode != "both")
            {
                throw new FormatException("Observer mode must be self, lower-priority, or both.");
            }

            if (!(json["watchedKeys"] is JArray watchedKeys) || watchedKeys.Count == 0)
            {
                throw new FormatException("Observer watchedKeys must be a non-empty array.");
            }

            return new NodeObserver(mode, watchedKeys.Select(t => t.Value<string>()));
        }

        private static JObject WriteObserver(NodeObserver observer)
        {
            return new JObject
            {
                ["mode"] = observer.Mode,
                ["watchedKeys"] = new JArray(observer.WatchedKeys),
            };
        }

        /// <summary>
        /// Mirrors <c>CanonicalTreeJson</c>'s own (private) generated-bindings JSON shape: a flat
        /// <c>{memberId: blackboardKeyId, ...}</c> map.
        /// </summary>
        private static NodeBindingMap ReadBindings(JObject json)
        {
            var pairs = json.Properties().Select(p => new KeyValuePair<string, string>(p.Name, p.Value.Value<string>()));
            try
            {
                return new NodeBindingMap(pairs);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Invalid generated binding map: " + exception.Message);
            }
        }

        private static JObject WriteBindings(NodeBindingMap bindings)
        {
            var result = new JObject();
            foreach (var pair in bindings.Values)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        internal static List<NodeDocument> ReadNodes(JArray json)
        {
            return json.Select(token => ReadNode((JObject)token)).ToList();
        }

        internal static JArray WriteNodes(IEnumerable<NodeDocument> nodes)
        {
            return new JArray(nodes.Select(n => (JToken)WriteNode(n)));
        }

        /// <summary>
        /// Reads one blackboard-key declaration: <c>{id, valueType, scope?, default?, reduction?,
        /// description?}</c>. <c>scope</c> defaults to <c>"tree"</c>; Agent/Shared scope requires a
        /// real, canonical <c>default</c> (P7-018 -- previously rejected outright since this MCP
        /// surface had no default-value reader). Scoped to built-in scalar types only (not Enum32
        /// or Registered, which need a project-supplied type catalog) -- unrelated, pre-existing,
        /// still-disclosed limitation.
        /// </summary>
        internal static BlackboardKeyDefinition ReadBlackboardKey(JObject json)
        {
            var id = RequireString(json, "id");
            var valueTypeText = RequireString(json, "valueType");
            if (!Enum.TryParse<BlackboardValueType>(valueTypeText, out var valueType)
                || valueType == BlackboardValueType.Invalid
                || valueType == BlackboardValueType.Registered
                || valueType == BlackboardValueType.Enum32)
            {
                throw new FormatException("Unsupported or unknown blackboard valueType: " + valueTypeText + ". Supported: built-in scalar types only (not Enum32 or Registered).");
            }

            var scope = ReadBlackboardScope(json);
            BlackboardDefaultValue defaultValue = null;
            if (json["default"] != null && json["default"].Type != JTokenType.Null)
            {
                defaultValue = ReadDefaultValue(json["default"], valueType);
            }
            else if (scope != BlackboardScope.Tree)
            {
                throw new FormatException("Agent/Shared blackboard keys require a canonical 'default' value.");
            }

            var reduction = ReadReduction(json["reduction"], scope);
            var description = json["description"]?.Value<string>();
            return new BlackboardKeyDefinition(id, id, BlackboardTypeReference.BuiltIn(valueType), scope, defaultValue, description, reduction);
        }

        internal static List<BlackboardKeyDefinition> ReadBlackboardKeys(JArray json)
        {
            return json.Select(token => ReadBlackboardKey((JObject)token)).ToList();
        }

        internal static JObject WriteBlackboardKey(BlackboardKeyDefinition key)
        {
            var json = new JObject
            {
                ["id"] = key.Id,
                ["valueType"] = key.Type.ValueType.ToString(),
                ["scope"] = key.Scope.ToString().ToLowerInvariant(),
                ["description"] = key.Description,
            };

            if (key.DefaultValue != null)
            {
                json["default"] = WriteDefaultValue(key.DefaultValue);
            }

            if (key.Reduction != BlackboardReductionKind.None)
            {
                json["reduction"] = new JObject { ["kind"] = key.Reduction.ToString().ToLowerInvariant() };
            }

            return json;
        }

        /// <summary>Maps <c>scope</c> ("tree"/"agent"/"shared", defaulting to "tree") to <see cref="BlackboardScope"/>.</summary>
        private static BlackboardScope ReadBlackboardScope(JObject json)
        {
            var text = json["scope"]?.Value<string>() ?? "tree";
            switch (text)
            {
                case "tree": return BlackboardScope.Tree;
                case "agent": return BlackboardScope.Agent;
                case "shared": return BlackboardScope.Shared;
                default: throw new FormatException("Unknown blackboard scope: " + text + ". Supported: tree, agent, shared.");
            }
        }

        /// <summary>Mirrors <c>CanonicalTreeJson.ReadReduction</c>'s own (private) shape: <c>{kind}</c>, only for Shared scope.</summary>
        private static BlackboardReductionKind ReadReduction(JToken token, BlackboardScope scope)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return BlackboardReductionKind.None;
            }

            if (scope != BlackboardScope.Shared)
            {
                throw new FormatException("Only Shared-scope blackboard keys may declare a reduction.");
            }

            var kindText = RequireString((JObject)token, "kind");
            if (!Enum.TryParse<BlackboardReductionKind>(kindText, true, out var kind))
            {
                throw new FormatException("Unknown blackboard reduction kind: " + kindText);
            }

            return kind;
        }

        /// <summary>
        /// Mirrors <c>CanonicalTreeJson.ReadDefault</c>'s own (private) shape for the built-in
        /// scalar types this MCP surface accepts (not Enum32/Registered -- unrelated, disclosed
        /// limitation). A caller-supplied default is required for Agent/Shared keys.
        /// </summary>
        private static BlackboardDefaultValue ReadDefaultValue(JToken token, BlackboardValueType type)
        {
            switch (type)
            {
                case BlackboardValueType.Bool: return BlackboardDefaultValue.Bool(token.Value<bool>());
                case BlackboardValueType.Int32: return BlackboardDefaultValue.Int32(token.Value<int>());
                case BlackboardValueType.Int64: return BlackboardDefaultValue.Int64(token.Value<long>());
                case BlackboardValueType.Float32: return BlackboardDefaultValue.Float32(token.Value<float>());
                case BlackboardValueType.Float64: return BlackboardDefaultValue.Float64(token.Value<double>());
                case BlackboardValueType.Float2:
                    var f2 = RequireVector((JObject)token, "x", "y");
                    return BlackboardDefaultValue.Float2(f2[0], f2[1]);
                case BlackboardValueType.Float3:
                    var f3 = RequireVector((JObject)token, "x", "y", "z");
                    return BlackboardDefaultValue.Float3(f3[0], f3[1], f3[2]);
                case BlackboardValueType.Quaternion:
                    var q = RequireVector((JObject)token, "x", "y", "z", "w");
                    return BlackboardDefaultValue.Quaternion(q[0], q[1], q[2], q[3]);
                case BlackboardValueType.FixedString32: return BlackboardDefaultValue.FixedString32(token.Value<string>());
                case BlackboardValueType.FixedString64: return BlackboardDefaultValue.FixedString64(token.Value<string>());
                case BlackboardValueType.FixedString128: return BlackboardDefaultValue.FixedString128(token.Value<string>());
                case BlackboardValueType.FixedString512: return BlackboardDefaultValue.FixedString512(token.Value<string>());
                case BlackboardValueType.AgentId:
                    if (!AIBT.AgentId.TryParse(token.Value<string>(), out var agent)) throw new FormatException("Invalid AgentId default.");
                    return BlackboardDefaultValue.AgentId(agent);
                case BlackboardValueType.EntityId:
                    if (!AIBT.EntityId.TryParse(token.Value<string>(), out var entity)) throw new FormatException("Invalid EntityId default.");
                    return BlackboardDefaultValue.EntityId(entity);
                case BlackboardValueType.OperationId:
                    if (!AIBT.OperationId.TryParse(token.Value<string>(), out var operation)) throw new FormatException("Invalid OperationId default.");
                    return BlackboardDefaultValue.OperationId(operation);
                case BlackboardValueType.AssetId:
                    var assetJson = (JObject)token;
                    var localFileId = assetJson["localFileId"] == null ? (long?)null : assetJson["localFileId"].Value<long>();
                    if (!AIBT.AssetId.TryParse(RequireString(assetJson, "guid"), localFileId, out var asset)) throw new FormatException("Invalid AssetId default.");
                    return BlackboardDefaultValue.AssetId(asset);
                default:
                    throw new FormatException("Unsupported default value type: " + type);
            }
        }

        private static JToken WriteDefaultValue(BlackboardDefaultValue value)
        {
            if (!value.TryGetRuntimeValue(out var runtime))
            {
                throw new InvalidOperationException("A default value is not representable by the MCP authoring writer.");
            }

            switch (runtime.Type)
            {
                case BlackboardValueType.Bool: runtime.TryGetBool(out var b); return b;
                case BlackboardValueType.Int32: runtime.TryGetInt32(out var i32); return i32;
                case BlackboardValueType.Int64: runtime.TryGetInt64(out var i64); return i64;
                case BlackboardValueType.Float32: runtime.TryGetFloat32(out var f32); return f32;
                case BlackboardValueType.Float64: runtime.TryGetFloat64(out var f64); return f64;
                case BlackboardValueType.Float2: runtime.TryGetFloat2(out var v2); return WriteVector(v2.X, v2.Y);
                case BlackboardValueType.Float3: runtime.TryGetFloat3(out var v3); return WriteVector(v3.X, v3.Y, v3.Z);
                case BlackboardValueType.Quaternion: runtime.TryGetQuaternion(out var quat); return WriteVector(quat.X, quat.Y, quat.Z, quat.W);
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512:
                    runtime.TryGetFixedString(out var text); return text;
                case BlackboardValueType.AgentId: runtime.TryGetAgentId(out var agent); return agent.ToString();
                case BlackboardValueType.EntityId: runtime.TryGetEntityId(out var entity); return entity.ToString();
                case BlackboardValueType.OperationId: runtime.TryGetOperationId(out var operation); return operation.ToString();
                case BlackboardValueType.AssetId:
                    runtime.TryGetAssetId(out var asset);
                    var assetJson = new JObject { ["guid"] = asset.ToGuidString() };
                    if (asset.HasLocalFileId) assetJson["localFileId"] = asset.LocalFileId;
                    return assetJson;
                default:
                    throw new InvalidOperationException("Unsupported runtime value type: " + runtime.Type);
            }
        }

        /// <summary>Mirrors <c>CanonicalTreeJson</c>'s own vector shape: a <c>{x, y, z?, w?}</c> object.</summary>
        private static float[] RequireVector(JObject json, params string[] components)
        {
            var result = new float[components.Length];
            for (var index = 0; index < components.Length; index++)
            {
                var component = json[components[index]];
                if (component == null)
                {
                    throw new FormatException("Missing required vector component '" + components[index] + "'.");
                }

                result[index] = component.Value<float>();
            }

            return result;
        }

        private static JObject WriteVector(params float[] components)
        {
            var names = new[] { "x", "y", "z", "w" };
            var json = new JObject();
            for (var index = 0; index < components.Length; index++)
            {
                json[names[index]] = components[index];
            }

            return json;
        }

        /// <summary>Reads a scope contract declaration: <c>{contractId, contractVersion}</c>.</summary>
        internal static BlackboardScopeContract ReadScopeContract(JObject json)
        {
            var contractId = RequireString(json, "contractId");
            var contractVersion = json["contractVersion"]?.Value<uint>()
                ?? throw new FormatException("Missing required property 'contractVersion'.");
            return new BlackboardScopeContract(contractId, contractVersion);
        }

        internal static JObject WriteScopeContract(BlackboardScopeContract contract)
        {
            return new JObject
            {
                ["contractId"] = contract.ContractId,
                ["contractVersion"] = contract.ContractVersion,
            };
        }

        private static string RequireString(JObject json, string property)
        {
            var value = json[property]?.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new FormatException("Missing required string property '" + property + "'.");
            }

            return value;
        }
    }
}
