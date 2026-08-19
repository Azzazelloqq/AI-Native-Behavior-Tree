using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AIBT.Authoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBT.Editor.Layout
{
    /// <summary>
    /// Strict reader for *.aibt.layout.json, per editor-layout-v1.md. Mirrors
    /// AIBT.Authoring.CanonicalTreeJson's approach (Newtonsoft JsonTextReader with
    /// DuplicatePropertyNameHandling.Error, fail-closed on unknown fields) at a scale
    /// appropriate to the smaller layout schema.
    /// </summary>
    public static class CanonicalLayoutJson
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static LayoutJsonReadResult Parse(byte[] utf8, string documentId = null, TreeDocument semanticTree = null)
        {
            if (utf8 == null)
            {
                throw new ArgumentNullException(nameof(utf8));
            }

            string source;
            try
            {
                source = StrictUtf8.GetString(utf8);
            }
            catch (DecoderFallbackException exception)
            {
                return Failure(null, utf8, LayoutJsonDiagnostics.Create(
                    LayoutJsonDiagnosticCodes.InvalidUtf8, "Input is not valid UTF-8: " + exception.Message, documentId));
            }

            return ParseCore(source, (byte[])utf8.Clone(), documentId, semanticTree);
        }

        public static LayoutJsonReadResult Parse(string json, string documentId = null, TreeDocument semanticTree = null)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            return ParseCore(json, null, documentId, semanticTree);
        }

        private static LayoutJsonReadResult ParseCore(string source, byte[] sourceUtf8, string documentId, TreeDocument semanticTree)
        {
            JObject root;
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
                    var token = JToken.Load(reader, new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load,
                    });

                    if (reader.Read())
                    {
                        throw new JsonReaderException("Only one JSON value is permitted.");
                    }

                    root = token as JObject;
                    if (root == null)
                    {
                        return Failure(source, sourceUtf8, LayoutJsonDiagnostics.Create(
                            LayoutJsonDiagnosticCodes.SchemaViolation, "The document root must be a JSON object.", documentId, "/"));
                    }
                }
            }
            catch (JsonReaderException exception)
            {
                var duplicate = exception.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0;
                return Failure(source, sourceUtf8, LayoutJsonDiagnostics.Create(
                    duplicate ? LayoutJsonDiagnosticCodes.DuplicateProperty : LayoutJsonDiagnosticCodes.InvalidSyntax,
                    duplicate ? "Duplicate object properties are not permitted." : "Invalid JSON syntax: " + exception.Message,
                    documentId));
            }

            try
            {
                var document = ReadDocument(root, documentId, semanticTree);
                return new LayoutJsonReadResult(document, DiagnosticCollection.Empty, source, sourceUtf8);
            }
            catch (LayoutJsonReadException exception)
            {
                return Failure(source, sourceUtf8, exception.Diagnostic);
            }
        }

        private static LayoutDocument ReadDocument(JObject root, string documentId, TreeDocument semanticTree)
        {
            RequireNoUnknownProperties(root, RootProperties, documentId, "/");

            var format = RequireString(root, "format", documentId, "/format");
            if (!string.Equals(format, LayoutDocument.CurrentFormat, StringComparison.Ordinal))
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Unexpected 'format' value: '" + format + "'.", documentId, "/format");
            }

            var formatVersion = RequireInt(root, "formatVersion", documentId, "/formatVersion");
            if (formatVersion != LayoutDocument.CurrentFormatVersion)
            {
                throw Error(LayoutJsonDiagnosticCodes.UnsupportedVersion, "Unsupported formatVersion: " + formatVersion + ".", documentId, "/formatVersion");
            }

            var treeIdText = RequireString(root, "treeId", documentId, "/treeId");
            TreeId treeId;
            try
            {
                treeId = new TreeId(treeIdText);
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Invalid treeId: " + exception.Message, documentId, "/treeId");
            }

            if (semanticTree != null && semanticTree.TreeId != treeId)
            {
                throw Error(LayoutJsonDiagnosticCodes.TreeIdMismatch, "treeId does not match the open semantic document.", documentId, "/treeId");
            }

            var direction = LayoutDirection.TopToBottom;
            if (root.ContainsKey("direction"))
            {
                var directionText = RequireString(root, "direction", documentId, "/direction");
                if (string.Equals(directionText, "topToBottom", StringComparison.Ordinal))
                {
                    direction = LayoutDirection.TopToBottom;
                }
                else if (string.Equals(directionText, "leftToRight", StringComparison.Ordinal))
                {
                    direction = LayoutDirection.LeftToRight;
                }
                else
                {
                    throw Error(LayoutJsonDiagnosticCodes.InvalidDirection, "Invalid direction value: '" + directionText + "'.", documentId, "/direction");
                }
            }

            HashSet<NodeId> validNodeIds = null;
            if (semanticTree != null)
            {
                validNodeIds = new HashSet<NodeId>();
                foreach (var node in semanticTree.Nodes)
                {
                    validNodeIds.Add(node.Id);
                }
            }

            var nodes = ReadNodes(root, documentId, validNodeIds);
            var groups = ReadGroups(root, documentId, validNodeIds);
            var notes = ReadNotes(root, documentId);
            var reroutes = ReadReroutes(root, documentId, semanticTree);

            return new LayoutDocument(treeId, direction, nodes, groups, notes, reroutes);
        }

        private static readonly HashSet<string> RootProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "format", "formatVersion", "treeId", "direction", "nodes", "groups", "notes", "reroutes",
        };

        private static readonly HashSet<string> NodeProperties = new HashSet<string>(StringComparer.Ordinal) { "position", "pinned" };
        private static readonly HashSet<string> GroupProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "title", "description", "color", "locked", "memberNodeIds",
        };
        private static readonly HashSet<string> NoteProperties = new HashSet<string>(StringComparer.Ordinal) { "text", "position", "size", "color" };
        private static readonly HashSet<string> RerouteProperties = new HashSet<string>(StringComparer.Ordinal) { "waypoints" };
        private static readonly HashSet<string> PointProperties = new HashSet<string>(StringComparer.Ordinal) { "x", "y" };

        private static Dictionary<NodeId, LayoutNodePlacement> ReadNodes(JObject root, string documentId, HashSet<NodeId> validNodeIds)
        {
            var result = new Dictionary<NodeId, LayoutNodePlacement>();
            if (!root.TryGetValue("nodes", out var token))
            {
                return result;
            }

            var nodesObject = RequireObjectToken(token, documentId, "/nodes");
            foreach (var property in nodesObject.Properties())
            {
                var pointer = "/nodes/" + property.Name;
                if (!NodeId.TryParse(property.Name, out var nodeId))
                {
                    throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Invalid NodeId key: '" + property.Name + "'.", documentId, pointer);
                }

                if (validNodeIds != null && !validNodeIds.Contains(nodeId))
                {
                    throw Error(LayoutJsonDiagnosticCodes.UnknownNodeReference, "'" + property.Name + "' does not exist in the semantic document.", documentId, pointer);
                }

                var entryObject = RequireObjectToken(property.Value, documentId, pointer);
                RequireNoUnknownProperties(entryObject, NodeProperties, documentId, pointer);
                var position = RequirePoint(entryObject, "position", documentId, pointer);
                var pinned = false;
                if (entryObject.TryGetValue("pinned", out var pinnedToken))
                {
                    pinned = RequireBool(pinnedToken, documentId, pointer + "/pinned");
                }

                result[nodeId] = new LayoutNodePlacement(position, pinned);
            }

            return result;
        }

        private static Dictionary<string, LayoutGroup> ReadGroups(JObject root, string documentId, HashSet<NodeId> validNodeIds)
        {
            var result = new Dictionary<string, LayoutGroup>(StringComparer.Ordinal);
            if (!root.TryGetValue("groups", out var token))
            {
                return result;
            }

            var groupedNodes = new HashSet<NodeId>();
            var groupsObject = RequireObjectToken(token, documentId, "/groups");
            foreach (var property in groupsObject.Properties())
            {
                var pointer = "/groups/" + property.Name;
                if (!LayoutIdentity.IsValid(property.Name))
                {
                    throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Invalid GroupId key: '" + property.Name + "'.", documentId, pointer);
                }

                var entryObject = RequireObjectToken(property.Value, documentId, pointer);
                RequireNoUnknownProperties(entryObject, GroupProperties, documentId, pointer);
                var title = RequireString(entryObject, "title", documentId, pointer + "/title");
                var description = OptionalString(entryObject, "description");
                var color = OptionalString(entryObject, "color");
                var locked = entryObject.TryGetValue("locked", out var lockedToken) && RequireBool(lockedToken, documentId, pointer + "/locked");
                var memberIds = new List<NodeId>();
                var membersArray = RequireArrayToken(entryObject, "memberNodeIds", documentId, pointer + "/memberNodeIds");
                foreach (var member in membersArray)
                {
                    var memberText = RequireStringToken(member, documentId, pointer + "/memberNodeIds");
                    if (!NodeId.TryParse(memberText, out var memberId))
                    {
                        throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Invalid NodeId: '" + memberText + "'.", documentId, pointer + "/memberNodeIds");
                    }

                    if (validNodeIds != null && !validNodeIds.Contains(memberId))
                    {
                        throw Error(LayoutJsonDiagnosticCodes.UnknownNodeReference, "'" + memberText + "' does not exist in the semantic document.", documentId, pointer + "/memberNodeIds");
                    }

                    if (!groupedNodes.Add(memberId))
                    {
                        throw Error(LayoutJsonDiagnosticCodes.NodeInMultipleGroups, "'" + memberText + "' belongs to more than one group.", documentId, pointer + "/memberNodeIds");
                    }

                    memberIds.Add(memberId);
                }

                result[property.Name] = new LayoutGroup(title, memberIds, description, color, locked);
            }

            return result;
        }

        private static Dictionary<string, LayoutNote> ReadNotes(JObject root, string documentId)
        {
            var result = new Dictionary<string, LayoutNote>(StringComparer.Ordinal);
            if (!root.TryGetValue("notes", out var token))
            {
                return result;
            }

            var notesObject = RequireObjectToken(token, documentId, "/notes");
            foreach (var property in notesObject.Properties())
            {
                var pointer = "/notes/" + property.Name;
                if (!LayoutIdentity.IsValid(property.Name))
                {
                    throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Invalid NoteId key: '" + property.Name + "'.", documentId, pointer);
                }

                var entryObject = RequireObjectToken(property.Value, documentId, pointer);
                RequireNoUnknownProperties(entryObject, NoteProperties, documentId, pointer);
                var text = RequireString(entryObject, "text", documentId, pointer + "/text");
                var position = RequirePoint(entryObject, "position", documentId, pointer);
                var size = RequirePoint(entryObject, "size", documentId, pointer);
                var color = OptionalString(entryObject, "color");
                result[property.Name] = new LayoutNote(text, position, size, color);
            }

            return result;
        }

        private static Dictionary<LayoutEdgeKey, LayoutReroute> ReadReroutes(JObject root, string documentId, TreeDocument semanticTree)
        {
            var result = new Dictionary<LayoutEdgeKey, LayoutReroute>();
            if (!root.TryGetValue("reroutes", out var token))
            {
                return result;
            }

            var reroutesObject = RequireObjectToken(token, documentId, "/reroutes");
            foreach (var property in reroutesObject.Properties())
            {
                var pointer = "/reroutes/" + property.Name;
                var parts = property.Name.Split('|');
                if (parts.Length != 2 || !NodeId.TryParse(parts[0], out var fromId) || !NodeId.TryParse(parts[1], out var toId))
                {
                    throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Invalid reroute key (expected 'fromNodeId|toNodeId'): '" + property.Name + "'.", documentId, pointer);
                }

                if (semanticTree != null && !EdgeExists(semanticTree, fromId, toId))
                {
                    throw Error(LayoutJsonDiagnosticCodes.OrphanedReroute, "No edge '" + fromId + "' -> '" + toId + "' exists in the semantic document.", documentId, pointer);
                }

                var entryObject = RequireObjectToken(property.Value, documentId, pointer);
                RequireNoUnknownProperties(entryObject, RerouteProperties, documentId, pointer);
                var waypointsArray = RequireArrayToken(entryObject, "waypoints", documentId, pointer + "/waypoints");
                var waypoints = new List<LayoutPoint>();
                foreach (var waypoint in waypointsArray)
                {
                    var waypointObject = RequireObjectToken(waypoint, documentId, pointer + "/waypoints");
                    RequireNoUnknownProperties(waypointObject, PointProperties, documentId, pointer + "/waypoints");
                    waypoints.Add(new LayoutPoint(
                        (float)RequireDouble(waypointObject, "x", documentId, pointer + "/waypoints"),
                        (float)RequireDouble(waypointObject, "y", documentId, pointer + "/waypoints")));
                }

                if (waypoints.Count == 0)
                {
                    throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "A reroute requires at least one waypoint.", documentId, pointer + "/waypoints");
                }

                result[new LayoutEdgeKey(fromId, toId)] = new LayoutReroute(waypoints);
            }

            return result;
        }

        private static bool EdgeExists(TreeDocument semanticTree, NodeId fromId, NodeId toId)
        {
            foreach (var node in semanticTree.Nodes)
            {
                if (node.Id != fromId)
                {
                    continue;
                }

                for (var index = 0; index < node.Children.Count; index++)
                {
                    if (node.Children[index] == toId)
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }

        private static LayoutPoint RequirePoint(JObject parent, string propertyName, string documentId, string parentPointer)
        {
            var pointer = parentPointer + "/" + propertyName;
            var pointObject = RequireObjectToken(RequireToken(parent, propertyName, documentId, pointer), documentId, pointer);
            RequireNoUnknownProperties(pointObject, PointProperties, documentId, pointer);
            var x = RequireDouble(pointObject, "x", documentId, pointer);
            var y = RequireDouble(pointObject, "y", documentId, pointer);
            return new LayoutPoint((float)x, (float)y);
        }

        private static void RequireNoUnknownProperties(JObject obj, HashSet<string> allowed, string documentId, string pointer)
        {
            foreach (var property in obj.Properties())
            {
                if (!allowed.Contains(property.Name))
                {
                    throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Unknown property: '" + property.Name + "'.", documentId, pointer + "/" + property.Name);
                }
            }
        }

        private static JToken RequireToken(JObject parent, string propertyName, string documentId, string pointer)
        {
            if (!parent.TryGetValue(propertyName, out var token))
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Missing required property: '" + propertyName + "'.", documentId, pointer);
            }

            return token;
        }

        private static string RequireString(JObject parent, string propertyName, string documentId, string pointer)
        {
            return RequireStringToken(RequireToken(parent, propertyName, documentId, pointer), documentId, pointer);
        }

        private static string RequireStringToken(JToken token, string documentId, string pointer)
        {
            if (token.Type != JTokenType.String)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Expected a string.", documentId, pointer);
            }

            return token.Value<string>();
        }

        private static string OptionalString(JObject parent, string propertyName)
        {
            return parent.TryGetValue(propertyName, out var token) && token.Type == JTokenType.String ? token.Value<string>() : null;
        }

        private static int RequireInt(JObject parent, string propertyName, string documentId, string pointer)
        {
            var token = RequireToken(parent, propertyName, documentId, pointer);
            if (token.Type != JTokenType.Integer)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Expected an integer.", documentId, pointer);
            }

            return token.Value<int>();
        }

        private static double RequireDouble(JObject parent, string propertyName, string documentId, string pointer)
        {
            var token = RequireToken(parent, propertyName, documentId, pointer);
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Expected a number.", documentId, pointer + "/" + propertyName);
            }

            return token.Value<double>();
        }

        private static bool RequireBool(JToken token, string documentId, string pointer)
        {
            if (token.Type != JTokenType.Boolean)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Expected a boolean.", documentId, pointer);
            }

            return token.Value<bool>();
        }

        private static JObject RequireObjectToken(JToken token, string documentId, string pointer)
        {
            var obj = token as JObject;
            if (obj == null)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Expected a JSON object.", documentId, pointer);
            }

            return obj;
        }

        private static JArray RequireArrayToken(JObject parent, string propertyName, string documentId, string pointer)
        {
            var token = RequireToken(parent, propertyName, documentId, pointer);
            var array = token as JArray;
            if (array == null)
            {
                throw Error(LayoutJsonDiagnosticCodes.SchemaViolation, "Expected a JSON array.", documentId, pointer);
            }

            return array;
        }

        private static LayoutJsonReadException Error(DiagnosticCode code, string message, string documentId, string pointer)
        {
            return new LayoutJsonReadException(LayoutJsonDiagnostics.Create(code, message, documentId, pointer));
        }

        private static LayoutJsonReadResult Failure(string source, byte[] sourceUtf8, Diagnostic diagnostic)
        {
            return new LayoutJsonReadResult(null, new DiagnosticCollection(new[] { diagnostic }), source, sourceUtf8);
        }

        private sealed class LayoutJsonReadException : Exception
        {
            public LayoutJsonReadException(Diagnostic diagnostic)
            {
                Diagnostic = diagnostic;
            }

            public Diagnostic Diagnostic { get; }
        }
    }
}
