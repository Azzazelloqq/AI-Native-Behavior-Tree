using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    public static class CanonicalTreeJson
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static TreeJsonReadResult Parse(byte[] utf8, string documentId = null)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            string source;
            try
            {
                source = StrictUtf8.GetString(utf8);
            }
            catch (DecoderFallbackException exception)
            {
                return ReadFailure(null, utf8, TreeJsonDiagnostics.Create(
                    TreeJsonDiagnosticCodes.InvalidUtf8,
                    "Input is not valid UTF-8: " + exception.Message,
                    documentId));
            }

            return ParseCore(source, (byte[])utf8.Clone(), documentId);
        }

        public static TreeJsonReadResult Parse(string json, string documentId = null)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return ParseCore(json, null, documentId);
        }

        public static TreeJsonWriteResult Serialize(TreeDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var diagnostics = ValidateRepresentable(document);
            if (diagnostics.Count != 0)
                return new TreeJsonWriteResult(null, null, diagnostics);

            try
            {
                var utf8 = CanonicalTreeJsonWriter.Write(document, false);
                var semanticBytes = CanonicalTreeJsonWriter.Write(document, true);
                using (var sha256 = SHA256.Create())
                {
                    return new TreeJsonWriteResult(utf8, sha256.ComputeHash(semanticBytes), DiagnosticCollection.Empty);
                }
            }
            catch (Exception exception) when (exception is EncoderFallbackException || exception is InvalidOperationException)
            {
                return new TreeJsonWriteResult(null, null, new DiagnosticCollection(new[]
                {
                    TreeJsonDiagnostics.Create(
                        TreeJsonDiagnosticCodes.UnrepresentableDocument,
                        "Tree document cannot be written canonically: " + exception.Message)
                }));
            }
        }

        private static TreeJsonReadResult ParseCore(string source, byte[] sourceUtf8, string documentId)
        {
            if (source.Length > 0 && source[0] == '\ufeff')
                return ReadFailure(source, sourceUtf8, TreeJsonDiagnostics.Create(
                    TreeJsonDiagnosticCodes.InvalidUtf8, "A UTF-8 BOM is not permitted.", documentId, line: 1, column: 1));

            if (TryFindInvalidEscapedUnicode(source, out var unicodeLine, out var unicodeColumn))
                return ReadFailure(source, sourceUtf8, TreeJsonDiagnostics.Create(
                    TreeJsonDiagnosticCodes.InvalidUnicode,
                    "JSON strings must contain only valid Unicode scalar sequences.",
                    documentId,
                    line: unicodeLine,
                    column: unicodeColumn));

            if (TryFindLexicalViolation(source, out var lexicalMessage, out var lexicalLine, out var lexicalColumn))
                return ReadFailure(source, sourceUtf8, TreeJsonDiagnostics.Create(
                    TreeJsonDiagnosticCodes.InvalidSyntax, lexicalMessage, documentId, line: lexicalLine, column: lexicalColumn));

            JToken token;
            try
            {
                using (var stringReader = new StringReader(source))
                using (var reader = new JsonTextReader(stringReader)
                {
                    Culture = CultureInfo.InvariantCulture,
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    SupportMultipleContent = false,
                })
                {
                    token = JToken.Load(reader, new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load,
                    });

                    if (reader.Read())
                        throw new JsonReaderException("Only one JSON value is permitted.");
                }
            }
            catch (JsonReaderException exception)
            {
                var duplicate = exception.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
                return ReadFailure(source, sourceUtf8, TreeJsonDiagnostics.Create(
                    duplicate ? TreeJsonDiagnosticCodes.DuplicateProperty : TreeJsonDiagnosticCodes.InvalidSyntax,
                    duplicate ? "Duplicate object properties are not permitted." : "Invalid JSON syntax: " + exception.Message,
                    documentId,
                    line: Positive(exception.LineNumber),
                    column: Positive(exception.LinePosition)));
            }

            if (!TryValidateUnicode(token, out var unicodeToken))
                return ReadFailure(source, sourceUtf8, ErrorAt(
                    TreeJsonDiagnosticCodes.InvalidUnicode,
                    "JSON strings must contain only valid Unicode scalar sequences.",
                    unicodeToken,
                    documentId,
                    Pointer(unicodeToken.Path)));

            try
            {
                var document = ReadDocument(RequireObject(token, "", documentId), documentId);
                return new TreeJsonReadResult(document, DiagnosticCollection.Empty, source, sourceUtf8);
            }
            catch (TreeJsonReadException exception)
            {
                return ReadFailure(source, sourceUtf8, exception.Diagnostic);
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is ArgumentException)
            {
                return ReadFailure(source, sourceUtf8, TreeJsonDiagnostics.Create(
                    TreeJsonDiagnosticCodes.SchemaViolation,
                    "JSON value cannot be represented by the Phase 1 authoring model: " + exception.Message,
                    documentId));
            }
        }

        private static TreeDocument ReadDocument(JObject root, string documentId)
        {
            RequireProperties(root, "", documentId,
                new[] { "format", "formatVersion", "treeId", "name", "root", "nodes" },
                new[] { "description", "blackboard", "tags", "metadata" });

            var format = StringValue(root["format"], "/format", documentId, false);
            if (!string.Equals(format, TreeDocument.CurrentFormat, StringComparison.Ordinal))
                throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "The format must be 'aibt.tree'.", root["format"], documentId, "/format");

            var version = Int32Value(root["formatVersion"], "/formatVersion", documentId);
            if (version != TreeDocument.CurrentFormatVersion)
                throw Error(TreeJsonDiagnosticCodes.UnsupportedVersion, "Only tree format version 1 is supported.", root["formatVersion"], documentId, "/formatVersion");

            var treeIdText = IdValue(root["treeId"], "/treeId", documentId);
            var rootIdText = IdValue(root["root"], "/root", documentId);
            var name = StringValue(root["name"], "/name", documentId, false);
            if (name.Length == 0)
                throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Tree name cannot be empty.", root["name"], documentId, "/name");

            var blackboard = ReadBlackboard(root["blackboard"], documentId);
            var nodes = ReadNodes(root["nodes"], documentId);
            var tags = ReadTags(root["tags"], "/tags", documentId);
            var metadata = root["metadata"] == null
                ? SemanticObject.Empty
                : ReadSemanticObject(RequireObject(root["metadata"], "/metadata", documentId), "/metadata", documentId);

            return new TreeDocument(
                format,
                version,
                new TreeId(treeIdText),
                name,
                new NodeId(rootIdText),
                nodes,
                blackboard,
                OptionalString(root["description"], "/description", documentId),
                tags,
                metadata);
        }

        private static List<BlackboardKeyDefinition> ReadBlackboard(JToken token, string documentId)
        {
            var result = new List<BlackboardKeyDefinition>();
            if (token == null) return result;
            var objectToken = RequireObject(token, "/blackboard", documentId);
            foreach (var property in objectToken.Properties())
            {
                if (!IsId(property.Name))
                    throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Blackboard map keys must be valid AIBT IDs.", property.Value, documentId, "/blackboard/" + Escape(property.Name));
                var pointer = "/blackboard/" + Escape(property.Name);
                var entry = RequireObject(property.Value, pointer, documentId);
                RequireProperties(entry, pointer, documentId, new[] { "type" }, new[] { "enumContract", "scope", "description", "default" });
                var typeName = StringValue(entry["type"], pointer + "/type", documentId, false);
                BlackboardTypeReference type;
                if (string.Equals(typeName, "Enum32", StringComparison.Ordinal))
                {
                    if (entry["enumContract"] == null)
                    {
                        throw Error(
                            TreeJsonDiagnosticCodes.SchemaViolation,
                            "Enum32 blackboard entries require enumContract.",
                            entry,
                            documentId,
                            pointer + "/enumContract");
                    }

                    type = BlackboardTypeReference.Enum32(
                        StringValue(entry["enumContract"], pointer + "/enumContract", documentId, false));
                }
                else if (entry["enumContract"] != null)
                {
                    throw Error(
                        TreeJsonDiagnosticCodes.SchemaViolation,
                        "enumContract is permitted only for Enum32 blackboard entries.",
                        entry["enumContract"],
                        documentId,
                        pointer + "/enumContract");
                }
                else if (!TryBuiltInType(typeName, out type))
                    throw Error(TreeJsonDiagnosticCodes.MissingRegisteredSchema,
                        "Registered blackboard type '" + typeName + "' cannot be loaded without a registered canonical schema implementation.",
                        entry["type"], documentId, pointer + "/type");
                var scope = ReadScope(entry["scope"], pointer + "/scope", documentId);
                BlackboardDefaultValue defaultValue = null;
                if (entry["default"] != null)
                    defaultValue = ReadDefault(entry["default"], type.ValueType, pointer + "/default", documentId);
                result.Add(new BlackboardKeyDefinition(
                    property.Name,
                    property.Name,
                    type,
                    scope,
                    defaultValue,
                    OptionalString(entry["description"], pointer + "/description", documentId)));
            }
            return result;
        }

        private static List<NodeDocument> ReadNodes(JToken token, string documentId)
        {
            var objectToken = RequireObject(token, "/nodes", documentId);
            if (!objectToken.HasValues)
                throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "At least one node is required.", objectToken, documentId, "/nodes");
            var result = new List<NodeDocument>();
            foreach (var property in objectToken.Properties())
            {
                var pointer = "/nodes/" + Escape(property.Name);
                if (!IsId(property.Name))
                    throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Node map keys must be valid AIBT IDs.", property.Value, documentId, pointer);
                var node = RequireObject(property.Value, pointer, documentId);
                RequireProperties(node, pointer, documentId,
                    new[] { "type", "typeVersion" },
                    new[] { "displayName", "description", "parameters", "observer", "children", "tags" });
                var type = StringValue(node["type"], pointer + "/type", documentId, false);
                if (type.Length == 0)
                    throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Node type cannot be empty.", node["type"], documentId, pointer + "/type");
                var typeVersion = Int32Value(node["typeVersion"], pointer + "/typeVersion", documentId);
                if (typeVersion < 1)
                    throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Node typeVersion must be at least 1.", node["typeVersion"], documentId, pointer + "/typeVersion");
                var parameters = node["parameters"] == null
                    ? SemanticObject.Empty
                    : ReadSemanticObject(RequireObject(node["parameters"], pointer + "/parameters", documentId), pointer + "/parameters", documentId);
                var observer = node["observer"] == null ? null : ReadObserver(node["observer"], pointer + "/observer", documentId);
                var children = ReadIds(node["children"], pointer + "/children", documentId, false);
                result.Add(new NodeDocument(
                    new NodeId(property.Name), type, typeVersion, children, parameters, observer,
                    OptionalString(node["displayName"], pointer + "/displayName", documentId),
                    OptionalString(node["description"], pointer + "/description", documentId),
                    ReadTags(node["tags"], pointer + "/tags", documentId)));
            }
            return result;
        }

        private static NodeObserver ReadObserver(JToken token, string pointer, string documentId)
        {
            var observer = RequireObject(token, pointer, documentId);
            RequireProperties(observer, pointer, documentId, new[] { "mode", "watchedKeys" }, Array.Empty<string>());
            var mode = StringValue(observer["mode"], pointer + "/mode", documentId, false);
            if (mode != "self" && mode != "lower-priority" && mode != "both")
                throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Observer mode must be self, lower-priority, or both.", observer["mode"], documentId, pointer + "/mode");
            var keys = ReadStringArray(observer["watchedKeys"], pointer + "/watchedKeys", documentId, true, true);
            return new NodeObserver(mode, keys);
        }

        private static SemanticObject ReadSemanticObject(JObject value, string pointer, string documentId)
        {
            var properties = new List<SemanticProperty>();
            foreach (var property in value.Properties())
                properties.Add(new SemanticProperty(property.Name, ReadSemanticValue(property.Value, pointer + "/" + Escape(property.Name), documentId)));
            return new SemanticObject(properties);
        }

        private static SemanticValue ReadSemanticValue(JToken token, string pointer, string documentId)
        {
            switch (token.Type)
            {
                case JTokenType.Null: return SemanticValue.Null;
                case JTokenType.Boolean: return SemanticValue.FromBoolean(token.Value<bool>());
                case JTokenType.Integer:
                    try { return SemanticValue.FromInt64(token.Value<long>()); }
                    catch (Exception exception) when (exception is OverflowException || exception is FormatException)
                    {
                        try { return SemanticValue.FromUInt64(token.Value<ulong>()); }
                        catch (Exception) { throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Integer is outside the supported Int64/UInt64 range.", token, documentId, pointer); }
                    }
                case JTokenType.Float:
                    var number = token.Value<double>();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                        throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Non-finite numbers are not permitted.", token, documentId, pointer);
                    return SemanticValue.FromNumber(number == 0d ? 0d : number);
                case JTokenType.String: return SemanticValue.FromString(token.Value<string>());
                case JTokenType.Array:
                    var array = new List<SemanticValue>();
                    var index = 0;
                    foreach (var item in (JArray)token)
                        array.Add(ReadSemanticValue(item, pointer + "/" + index++, documentId));
                    return SemanticValue.FromArray(array);
                case JTokenType.Object: return SemanticValue.FromObject(ReadSemanticObject((JObject)token, pointer, documentId));
                default: throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Only JSON semantic values are permitted.", token, documentId, pointer);
            }
        }

        private static BlackboardDefaultValue ReadDefault(JToken token, BlackboardValueType type, string pointer, string documentId)
        {
            try
            {
                switch (type)
                {
                    case BlackboardValueType.Bool: return BlackboardDefaultValue.Bool(Primitive<bool>(token, JTokenType.Boolean, pointer, documentId));
                    case BlackboardValueType.Int32: return BlackboardDefaultValue.Int32(Int32Value(token, pointer, documentId));
                    case BlackboardValueType.Int64: return BlackboardDefaultValue.Int64(Int64Value(token, pointer, documentId));
                    case BlackboardValueType.Float32: return BlackboardDefaultValue.Float32(FiniteFloat(token, pointer, documentId));
                    case BlackboardValueType.Float64: return BlackboardDefaultValue.Float64(FiniteDouble(token, pointer, documentId));
                    case BlackboardValueType.Float2:
                        var f2 = Vector(token, pointer, documentId, 2); return BlackboardDefaultValue.Float2(f2[0], f2[1]);
                    case BlackboardValueType.Float3:
                        var f3 = Vector(token, pointer, documentId, 3); return BlackboardDefaultValue.Float3(f3[0], f3[1], f3[2]);
                    case BlackboardValueType.Quaternion:
                        var q = Vector(token, pointer, documentId, 4); return BlackboardDefaultValue.Quaternion(q[0], q[1], q[2], q[3]);
                    case BlackboardValueType.Enum32:
                        var e = RequireObject(token, pointer, documentId);
                        RequireProperties(e, pointer, documentId, new[] { "contract", "value" }, Array.Empty<string>());
                        return BlackboardDefaultValue.Enum32(StringValue(e["contract"], pointer + "/contract", documentId, false), Int32Value(e["value"], pointer + "/value", documentId));
                    case BlackboardValueType.FixedString32: return BlackboardDefaultValue.FixedString32(StringValue(token, pointer, documentId, false));
                    case BlackboardValueType.FixedString64: return BlackboardDefaultValue.FixedString64(StringValue(token, pointer, documentId, false));
                    case BlackboardValueType.FixedString128: return BlackboardDefaultValue.FixedString128(StringValue(token, pointer, documentId, false));
                    case BlackboardValueType.FixedString512: return BlackboardDefaultValue.FixedString512(StringValue(token, pointer, documentId, false));
                    case BlackboardValueType.AgentId:
                        if (!AgentId.TryParse(StringValue(token, pointer, documentId, false), out var agent)) throw new FormatException("Invalid AgentId.");
                        return BlackboardDefaultValue.AgentId(agent);
                    case BlackboardValueType.EntityId:
                        if (!EntityId.TryParse(StringValue(token, pointer, documentId, false), out var entity)) throw new FormatException("Invalid EntityId.");
                        return BlackboardDefaultValue.EntityId(entity);
                    case BlackboardValueType.OperationId:
                        if (!OperationId.TryParse(StringValue(token, pointer, documentId, false), out var operation)) throw new FormatException("Invalid OperationId.");
                        return BlackboardDefaultValue.OperationId(operation);
                    case BlackboardValueType.AssetId:
                        var asset = RequireObject(token, pointer, documentId);
                        RequireProperties(asset, pointer, documentId, new[] { "guid" }, new[] { "localFileId" });
                        var localId = asset["localFileId"] == null ? (long?)null : Int64Value(asset["localFileId"], pointer + "/localFileId", documentId);
                        if (!AssetId.TryParse(StringValue(asset["guid"], pointer + "/guid", documentId, false), localId, out var assetId)) throw new FormatException("Invalid AssetId.");
                        return BlackboardDefaultValue.AssetId(assetId);
                    default: throw new FormatException("Unsupported default type.");
                }
            }
            catch (TreeJsonReadException) { throw; }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is ArgumentException)
            {
                throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Invalid " + type + " default: " + exception.Message, token, documentId, pointer);
            }
        }

        private static DiagnosticCollection ValidateRepresentable(TreeDocument document)
        {
            var diagnostics = new List<Diagnostic>();
            void Add(string message, string pointer = null, DiagnosticCode? code = null) => diagnostics.Add(TreeJsonDiagnostics.Create(code ?? TreeJsonDiagnosticCodes.UnrepresentableDocument, message, pointer: pointer));
            if (document.Format != TreeDocument.CurrentFormat) Add("Tree format must be 'aibt.tree'.", "/format");
            if (document.FormatVersion != TreeDocument.CurrentFormatVersion) Add("Only tree format version 1 can be serialized.", "/formatVersion", TreeJsonDiagnosticCodes.UnsupportedVersion);
            if (!document.TreeId.IsValid) Add("Tree ID is invalid.", "/treeId");
            if (string.IsNullOrEmpty(document.Name)) Add("Tree name is required.", "/name");
            if (!document.Root.IsValid) Add("Root node ID is invalid.", "/root");
            if (document.Nodes.Count == 0) Add("At least one node is required.", "/nodes");
            ValidateText(document.Name, "/name", Add);
            ValidateText(document.Description, "/description", Add);
            ValidateSemanticObject(document.Metadata, "/metadata", Add);
            ValidateTags(document.Tags, "/tags", Add);

            var blackboardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in document.Blackboard)
            {
                var pointer = "/blackboard/" + Escape(key?.Id ?? string.Empty);
                if (key == null) { Add("Blackboard entries cannot be null.", "/blackboard"); continue; }
                if (!IsId(key.Id) || !blackboardIds.Add(key.Id)) Add("Blackboard IDs must be valid and unique.", pointer);
                if (!string.Equals(key.Name, key.Id, StringComparison.Ordinal)) Add("The v1 JSON schema has no separate blackboard display-name field; Name must equal Id.", pointer);
                if (!key.Type.IsValid) Add("Blackboard type is invalid.", pointer + "/type");
                if (key.Type.IsRegistered) Add("Registered type requires a canonical schema implementation, which is not present in the Phase 1 descriptor.", pointer + "/type", TreeJsonDiagnosticCodes.MissingRegisteredSchema);
                if (key.Scope == BlackboardScope.NodeLocal || !Enum.IsDefined(typeof(BlackboardScope), key.Scope)) Add("Tree blackboard scope is invalid.", pointer + "/scope");
                ValidateText(key.Description, pointer + "/description", Add);
                if (key.HasDefault && (!key.DefaultValue.IsCanonical || !key.DefaultValue.HasRuntimeValue)) Add("Blackboard default is not canonical or representable.", pointer + "/default");
                if (key.HasDefault && key.DefaultValue.ValueType != key.Type.ValueType) Add("Blackboard default type does not match its declaration.", pointer + "/default");
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in document.Nodes)
            {
                var pointer = "/nodes/" + Escape(node?.Id.Value ?? string.Empty);
                if (node == null) { Add("Nodes cannot be null.", "/nodes"); continue; }
                if (!node.Id.IsValid || !nodeIds.Add(node.Id.Value)) Add("Node IDs must be valid and unique.", pointer);
                if (string.IsNullOrEmpty(node.TypeId)) Add("Node type is required.", pointer + "/type");
                if (node.TypeVersion < 1) Add("Node typeVersion must be at least 1.", pointer + "/typeVersion");
                ValidateText(node.TypeId, pointer + "/type", Add);
                ValidateText(node.DisplayName, pointer + "/displayName", Add);
                ValidateText(node.Description, pointer + "/description", Add);
                ValidateSemanticObject(node.Parameters, pointer + "/parameters", Add);
                ValidateTags(node.Tags, pointer + "/tags", Add);
                var children = new HashSet<NodeId>();
                foreach (var child in node.Children) if (!child.IsValid || !children.Add(child)) Add("Child IDs must be valid and unique.", pointer + "/children");
                if (node.Observer != null)
                {
                    if (node.Observer.Mode != "self" && node.Observer.Mode != "lower-priority" && node.Observer.Mode != "both") Add("Observer mode is invalid.", pointer + "/observer/mode");
                    if (node.Observer.WatchedKeys.Count == 0) Add("Observer watchedKeys cannot be empty.", pointer + "/observer/watchedKeys");
                    var watched = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var key in node.Observer.WatchedKeys) if (!IsId(key) || !watched.Add(key)) Add("Observer watchedKeys must contain unique valid IDs.", pointer + "/observer/watchedKeys");
                }
            }
            return new DiagnosticCollection(diagnostics);
        }

        private static void ValidateTags(TagSet tags, string pointer, Action<string, string, DiagnosticCode?> add)
        {
            if (tags == null || tags.HasDuplicateValues) { add("Tags must be a non-null set without duplicates.", pointer, null); return; }
            foreach (var tag in tags.Values)
            {
                if (string.IsNullOrEmpty(tag)) add("Tags cannot be empty.", pointer, null);
                ValidateText(tag, pointer, add);
            }
        }

        private static void ValidateSemanticObject(SemanticObject value, string pointer, Action<string, string, DiagnosticCode?> add)
        {
            if (value == null) { add("Semantic objects cannot be null.", pointer, null); return; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.Properties)
            {
                if (property == null || property.Name == null || property.Value == null) { add("Semantic object members require a name and value.", pointer, null); continue; }
                var childPointer = pointer + "/" + Escape(property.Name);
                if (!names.Add(property.Name)) add("Semantic object member names must be unique.", childPointer, null);
                ValidateText(property.Name, childPointer, add);
                ValidateSemanticValue(property.Value, childPointer, add);
            }
        }

        private static void ValidateSemanticValue(SemanticValue value, string pointer, Action<string, string, DiagnosticCode?> add)
        {
            if (value == null) { add("Semantic values cannot be null references.", pointer, null); return; }
            if (value.Kind == SemanticValueKind.String)
            {
                value.TryGetString(out var text);
                if (text == null) add("Semantic strings cannot be null.", pointer, null);
                else ValidateText(text, pointer, add);
            }
            else if (value.Kind == SemanticValueKind.Number)
            {
                value.TryGetNumber(out var number); if (double.IsNaN(number) || double.IsInfinity(number)) add("Non-finite semantic numbers are invalid.", pointer, null);
            }
            else if (value.Kind == SemanticValueKind.Array)
            {
                value.TryGetArray(out var values); for (var i = 0; i < values.Count; i++) ValidateSemanticValue(values[i], pointer + "/" + i, add);
            }
            else if (value.Kind == SemanticValueKind.Object)
            {
                value.TryGetObject(out var child); ValidateSemanticObject(child, pointer, add);
            }
        }

        private static void ValidateText(string value, string pointer, Action<string, string, DiagnosticCode?> add)
        {
            if (value != null && !IsValidUnicode(value)) add("Text contains an invalid Unicode scalar sequence.", pointer, TreeJsonDiagnosticCodes.InvalidUnicode);
        }

        private static TagSet ReadTags(JToken token, string pointer, string documentId)
            => token == null ? TagSet.Empty : new TagSet(ReadStringArray(token, pointer, documentId, false, true));

        private static List<NodeId> ReadIds(JToken token, string pointer, string documentId, bool nonEmpty)
        {
            if (token == null) return new List<NodeId>();
            var values = ReadStringArray(token, pointer, documentId, nonEmpty, true);
            var result = new List<NodeId>(values.Count);
            foreach (var value in values)
            {
                if (!NodeId.TryParse(value, out var id)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Array contains an invalid AIBT ID.", token, documentId, pointer);
                result.Add(id);
            }
            return result;
        }

        private static List<string> ReadStringArray(JToken token, string pointer, string documentId, bool nonEmpty, bool unique)
        {
            if (!(token is JArray array)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected an array.", token, documentId, pointer);
            if (nonEmpty && array.Count == 0) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Array cannot be empty.", token, documentId, pointer);
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < array.Count; index++)
            {
                var text = StringValue(array[index], pointer + "/" + index, documentId, false);
                if (text.Length == 0) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Array strings cannot be empty.", array[index], documentId, pointer + "/" + index);
                if (unique && !seen.Add(text)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Array values must be unique.", array[index], documentId, pointer + "/" + index);
                result.Add(text);
            }
            return result;
        }

        private static BlackboardScope ReadScope(JToken token, string pointer, string documentId)
        {
            if (token == null) return BlackboardScope.Tree;
            switch (StringValue(token, pointer, documentId, false))
            {
                case "tree": return BlackboardScope.Tree;
                case "agent": return BlackboardScope.Agent;
                case "shared": return BlackboardScope.Shared;
                default: throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Scope must be tree, agent, or shared.", token, documentId, pointer);
            }
        }

        private static float[] Vector(JToken token, string pointer, string documentId, int count)
        {
            var value = RequireObject(token, pointer, documentId);
            var names = count == 2 ? new[] { "x", "y" } : count == 3 ? new[] { "x", "y", "z" } : new[] { "x", "y", "z", "w" };
            RequireProperties(value, pointer, documentId, names, Array.Empty<string>());
            var result = new float[count];
            for (var index = 0; index < count; index++) result[index] = FiniteFloat(value[names[index]], pointer + "/" + names[index], documentId);
            return result;
        }

        private static T Primitive<T>(JToken token, JTokenType type, string pointer, string documentId)
        {
            if (token == null || token.Type != type) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Unexpected JSON value type.", token, documentId, pointer);
            return token.Value<T>();
        }

        private static int Int32Value(JToken token, string pointer, string documentId)
        {
            if (token == null || token.Type != JTokenType.Integer) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected an integer.", token, documentId, pointer);
            try { return token.Value<int>(); } catch (Exception) { throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Integer is outside Int32 range.", token, documentId, pointer); }
        }

        private static long Int64Value(JToken token, string pointer, string documentId)
        {
            if (token == null || token.Type != JTokenType.Integer) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected an integer.", token, documentId, pointer);
            try { return token.Value<long>(); } catch (Exception) { throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Integer is outside Int64 range.", token, documentId, pointer); }
        }

        private static float FiniteFloat(JToken token, string pointer, string documentId)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected a number.", token, documentId, pointer);
            var result = token.Value<float>();
            if (float.IsNaN(result) || float.IsInfinity(result)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Number is outside finite Float32 range.", token, documentId, pointer);
            return result == 0f ? 0f : result;
        }

        private static double FiniteDouble(JToken token, string pointer, string documentId)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected a number.", token, documentId, pointer);
            var result = token.Value<double>();
            if (double.IsNaN(result) || double.IsInfinity(result)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Number is not finite.", token, documentId, pointer);
            return result == 0d ? 0d : result;
        }

        private static string IdValue(JToken token, string pointer, string documentId)
        {
            var result = StringValue(token, pointer, documentId, false);
            if (!IsId(result)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected a valid AIBT ID.", token, documentId, pointer);
            return result;
        }

        private static string OptionalString(JToken token, string pointer, string documentId)
            => token == null ? null : StringValue(token, pointer, documentId, true);

        private static string StringValue(JToken token, string pointer, string documentId, bool allowEmpty)
        {
            if (token == null || token.Type != JTokenType.String) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected a string.", token, documentId, pointer);
            var result = token.Value<string>();
            if (!allowEmpty && result == null) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "String cannot be null.", token, documentId, pointer);
            return result;
        }

        private static JObject RequireObject(JToken token, string pointer, string documentId)
        {
            if (!(token is JObject result)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Expected an object.", token, documentId, pointer);
            return result;
        }

        private static void RequireProperties(JObject value, string pointer, string documentId, IReadOnlyList<string> required, IReadOnlyList<string> optional)
        {
            var allowed = new HashSet<string>(required, StringComparer.Ordinal);
            foreach (var name in optional) allowed.Add(name);
            foreach (var property in value.Properties())
                if (!allowed.Contains(property.Name)) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Unknown property '" + property.Name + "'.", property.Value, documentId, pointer + "/" + Escape(property.Name));
            foreach (var name in required)
                if (value.Property(name, StringComparison.Ordinal) == null) throw Error(TreeJsonDiagnosticCodes.SchemaViolation, "Missing required property '" + name + "'.", value, documentId, pointer);
        }

        private static bool TryBuiltInType(string value, out BlackboardTypeReference type)
        {
            foreach (BlackboardValueType candidate in Enum.GetValues(typeof(BlackboardValueType)))
            {
                if (candidate == BlackboardValueType.Invalid
                    || candidate == BlackboardValueType.Registered
                    || candidate == BlackboardValueType.Enum32) continue;
                var current = BlackboardTypeReference.BuiltIn(candidate);
                if (string.Equals(current.CanonicalTypeId, value, StringComparison.Ordinal)) { type = current; return true; }
            }
            type = default; return false;
        }

        private static bool TryValidateUnicode(JToken token, out JToken invalid)
        {
            if (token.Type == JTokenType.String && !IsValidUnicode(token.Value<string>())) { invalid = token; return false; }
            if (token is JObject obj)
                foreach (var property in obj.Properties())
                {
                    if (!IsValidUnicode(property.Name)) { invalid = property; return false; }
                    if (!TryValidateUnicode(property.Value, out invalid)) return false;
                }
            else if (token is JArray array)
                foreach (var child in array) if (!TryValidateUnicode(child, out invalid)) return false;
            invalid = null; return true;
        }

        private static bool IsValidUnicode(string value)
        {
            if (value == null) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (char.IsHighSurrogate(c))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
                    index++;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        private static bool TryFindLexicalViolation(string source, out string message, out int line, out int column)
        {
            var inString = false; var escaped = false; line = 1; column = 1;
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                if (!inString && c == '/' && index + 1 < source.Length && (source[index + 1] == '/' || source[index + 1] == '*'))
                { message = "JSON comments are not permitted."; return true; }
                if (!inString && c == ',')
                {
                    var next = index + 1;
                    while (next < source.Length && char.IsWhiteSpace(source[next])) next++;
                    if (next < source.Length && (source[next] == '}' || source[next] == ']'))
                    { message = "Trailing commas are not permitted."; return true; }
                }
                if (c == '"' && !escaped) inString = !inString;
                if (inString && c == '\\' && !escaped) escaped = true; else escaped = false;
                if (c == '\n') { line++; column = 0; }
                column++;
            }
            message = null; return false;
        }

        private static bool TryFindInvalidEscapedUnicode(string source, out int line, out int column)
        {
            var inString = false;
            line = 1;
            column = 1;
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                if (c == '"' && (index == 0 || !IsEscaped(source, index)))
                    inString = !inString;

                if (inString && c == '\\' && !IsEscaped(source, index)
                    && index + 1 < source.Length && source[index + 1] == 'u'
                    && TryReadHexCodeUnit(source, index + 2, out var codeUnit))
                {
                    if (char.IsHighSurrogate((char)codeUnit))
                    {
                        if (index + 11 >= source.Length || source[index + 6] != '\\' || source[index + 7] != 'u'
                            || !TryReadHexCodeUnit(source, index + 8, out var low)
                            || !char.IsLowSurrogate((char)low))
                            return true;
                        index += 11;
                        column += 11;
                    }
                    else if (char.IsLowSurrogate((char)codeUnit))
                    {
                        return true;
                    }
                    else
                    {
                        index += 5;
                        column += 5;
                    }
                }

                if (c == '\n') { line++; column = 0; }
                column++;
            }

            return false;
        }

        private static bool IsEscaped(string source, int quoteIndex)
        {
            var slashes = 0;
            for (var index = quoteIndex - 1; index >= 0 && source[index] == '\\'; index--) slashes++;
            return (slashes & 1) != 0;
        }

        private static bool TryReadHexCodeUnit(string source, int offset, out int value)
        {
            value = 0;
            if (offset + 4 > source.Length) return false;
            for (var index = 0; index < 4; index++)
            {
                var c = source[offset + index];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else return false;
                value = (value << 4) | digit;
            }
            return true;
        }

        private static bool IsId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !AlphaNumeric(value[0])) return false;
            for (var index = 1; index < value.Length; index++)
            {
                var c = value[index];
                if (!AlphaNumeric(c) && c != '.' && c != '_' && c != ':' && c != '-') return false;
            }
            return true;
        }

        private static bool AlphaNumeric(char c) => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9';
        private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
        private static string Pointer(string path) => string.IsNullOrEmpty(path) ? "" : "/" + path.Replace(".", "/").Replace("[", "/").Replace("]", "");
        private static int? Positive(int value) => value > 0 ? value : (int?)null;

        private static TreeJsonReadException Error(DiagnosticCode code, string message, JToken token, string documentId, string pointer)
            => new TreeJsonReadException(ErrorAt(code, message, token, documentId, pointer));

        private static Diagnostic ErrorAt(DiagnosticCode code, string message, JToken token, string documentId, string pointer)
        {
            var info = token as IJsonLineInfo;
            return TreeJsonDiagnostics.Create(code, message, documentId, pointer,
                info != null && info.HasLineInfo() ? info.LineNumber : (int?)null,
                info != null && info.HasLineInfo() ? info.LinePosition : (int?)null);
        }

        private static TreeJsonReadResult ReadFailure(string source, byte[] bytes, Diagnostic diagnostic)
            => new TreeJsonReadResult(null, new DiagnosticCollection(new[] { diagnostic }), source, bytes);

        private sealed class TreeJsonReadException : Exception
        {
            public TreeJsonReadException(Diagnostic diagnostic) { Diagnostic = diagnostic; }
            public Diagnostic Diagnostic { get; }
        }
    }
}
