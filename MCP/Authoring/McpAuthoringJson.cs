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

            return new NodeDocument(id, typeId, typeVersion, children, parameters, observer: null, displayName, description, tags);
        }

        internal static JObject WriteNode(NodeDocument node)
        {
            return new JObject
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
        /// Reads one blackboard-key declaration: <c>{id, valueType, description?}</c>, always
        /// tree-scoped. Scoped to built-in scalar types with no default value -- Enum32 and
        /// Registered types need a project-supplied type catalog, and Agent/Shared scope
        /// requires a scope contract plus a canonical default, neither of which this card's MCP
        /// surface has any way to accept yet (disclosed limitation, rejected explicitly here
        /// rather than accepted and left to fail confusingly at write time).
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

            if (json["scope"] != null && json["scope"].Value<string>() != "tree")
            {
                throw new FormatException("Only tree-scoped blackboard keys are supported by this tool (Agent/Shared require a scope contract and canonical default this MCP surface cannot accept yet).");
            }

            var description = json["description"]?.Value<string>();
            return new BlackboardKeyDefinition(id, id, BlackboardTypeReference.BuiltIn(valueType), BlackboardScope.Tree, defaultValue: null, description);
        }

        internal static List<BlackboardKeyDefinition> ReadBlackboardKeys(JArray json)
        {
            return json.Select(token => ReadBlackboardKey((JObject)token)).ToList();
        }

        internal static JObject WriteBlackboardKey(BlackboardKeyDefinition key)
        {
            return new JObject
            {
                ["id"] = key.Id,
                ["valueType"] = key.Type.ValueType.ToString(),
                ["scope"] = key.Scope.ToString().ToLowerInvariant(),
                ["description"] = key.Description,
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
