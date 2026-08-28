using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using AIBT.Editor.Patching;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// Implements the 11 Authoring tools ai-and-mcp.md's Authoring section lists (create tree;
    /// add/remove/move/replace/configure nodes; declare/change blackboard keys; extract/inline
    /// subtrees; apply a domain-patch transaction; request layout of the affected region). Every
    /// mutation goes through P6-004's <see cref="SemanticPatchTransaction"/>/
    /// <see cref="LayoutPatchTransaction"/> -- this class only resolves tree files, builds the
    /// pure edit function(s) to hand to those transactions, and serializes the result. Called
    /// from <see cref="McpToolDispatcher"/> only after permission enforcement.
    /// </summary>
    internal static class McpAuthoringToolDispatcher
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 0);

        // ---- create_tree -----------------------------------------------------------------

        internal static JObject CreateTree(string projectRoot, JObject args)
        {
            var treeId = new TreeId(RequireString(args, "treeId"));
            var name = RequireString(args, "name");
            var relativePath = RequireString(args, "path");
            var rootNode = McpAuthoringJson.ReadNode(RequireObject(args, "rootNode"));
            var blackboard = args["blackboard"] != null
                ? McpAuthoringJson.ReadBlackboardKeys((JArray)args["blackboard"])
                : new List<BlackboardKeyDefinition>();
            var description = args["description"]?.Value<string>();
            var dryRun = args["dryRun"]?.Value<bool>() ?? false;

            var path = ResolveNewTreePath(projectRoot, relativePath);

            var document = new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                treeId,
                name,
                rootNode.Id,
                new[] { rootNode },
                blackboard,
                description,
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);

            var (registry, options) = BuildRegistryAndOptions(projectRoot, path);
            var compilation = ReferenceCompiler.Compile(document, registry, options);

            var response = new JObject
            {
                ["accepted"] = compilation.Success,
                ["contentHash"] = ComputeSemanticHash(document),
                ["path"] = path,
                ["diagnostics"] = WriteDiagnostics(compilation.Diagnostics),
            };

            if (compilation.Success && !dryRun)
            {
                var writeDiagnostics = TreeDocumentPersistence.Save(path, document);
                if (writeDiagnostics.Count > 0)
                {
                    response["accepted"] = false;
                    response["diagnostics"] = WriteDiagnostics(writeDiagnostics);
                }
            }

            return response;
        }

        // ---- single-node authoring tools --------------------------------------------------

        internal static JObject AddNode(string projectRoot, JObject args)
        {
            var node = McpAuthoringJson.ReadNode(RequireObject(args, "node"));
            var parentId = new NodeId(RequireString(args, "parentId"));
            var insertIndex = args["insertIndex"]?.Value<int>();

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => SemanticEditOperations.AddNode(document, node, parentId, insertIndex),
            });
        }

        internal static JObject RemoveNode(string projectRoot, JObject args)
        {
            var nodeId = new NodeId(RequireString(args, "nodeId"));

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => SemanticEditOperations.RemoveNode(document, nodeId),
            });
        }

        internal static JObject MoveNode(string projectRoot, JObject args)
        {
            var nodeId = new NodeId(RequireString(args, "nodeId"));
            var newParentId = new NodeId(RequireString(args, "newParentId"));
            var insertIndex = args["insertIndex"]?.Value<int>();

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => McpAuthoringOperations.Move(document, nodeId, newParentId, insertIndex),
            });
        }

        internal static JObject ReplaceNode(string projectRoot, JObject args)
        {
            var nodeId = new NodeId(RequireString(args, "nodeId"));
            var newTypeId = RequireString(args, "newTypeId");
            var newTypeVersion = args["newTypeVersion"]?.Value<int>() ?? 1;
            var newParameters = McpAuthoringJson.ReadParameters(args["newParameters"]);

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => McpAuthoringOperations.Replace(document, nodeId, newTypeId, newTypeVersion, newParameters),
            });
        }

        internal static JObject ConfigureNode(string projectRoot, JObject args)
        {
            var nodeId = new NodeId(RequireString(args, "nodeId"));
            var parameterName = RequireString(args, "parameterName");
            var value = McpAuthoringJson.ReadValue(RequireToken(args, "value"));

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => SemanticEditOperations.SetParameter(document, nodeId, parameterName, value),
            });
        }

        internal static JObject SetBlackboardKeys(string projectRoot, JObject args)
        {
            var keys = McpAuthoringJson.ReadBlackboardKeys((JArray)RequireToken(args, "keys"));

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => McpAuthoringOperations.SetBlackboard(document, keys),
            });
        }

        // ---- extract / inline subtree ------------------------------------------------------

        internal static JObject ExtractSubtree(string projectRoot, JObject args)
        {
            var subtreeRootId = new NodeId(RequireString(args, "nodeId"));
            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            var capture = McpAuthoringOperations.CaptureSubtree(document, subtreeRootId);

            var patchResponse = ApplySemanticPatchToDocument(projectRoot, document, path, args, new Func<TreeDocument, TreeDocument>[]
            {
                d => SemanticEditOperations.RemoveNode(d, subtreeRootId),
            });

            patchResponse["extractedNodes"] = McpAuthoringJson.WriteNodes(capture.Nodes);
            patchResponse["attachment"] = new JObject
            {
                ["rootNodeId"] = subtreeRootId.Value,
                ["parentId"] = capture.ParentId.Value,
                ["insertIndex"] = capture.InsertIndex,
            };
            return patchResponse;
        }

        internal static JObject InlineSubtree(string projectRoot, JObject args)
        {
            var nodes = McpAuthoringJson.ReadNodes((JArray)RequireToken(args, "nodes"));
            var subtreeRootId = new NodeId(RequireString(args, "subtreeRootId"));
            var parentId = new NodeId(RequireString(args, "parentId"));
            var insertIndex = args["insertIndex"]?.Value<int>();

            return ApplySemanticPatch(projectRoot, args, new Func<TreeDocument, TreeDocument>[]
            {
                document => McpAuthoringOperations.AttachSubtree(document, nodes, subtreeRootId, parentId, insertIndex),
            });
        }

        // ---- generic multi-operation composer -----------------------------------------------

        internal static JObject ApplyDomainPatch(string projectRoot, JObject args)
        {
            var operationsJson = (JArray)RequireToken(args, "operations");
            var operations = operationsJson.Select(token => BuildOperation((JObject)token)).ToArray();

            return ApplySemanticPatch(projectRoot, args, operations);
        }

        private static Func<TreeDocument, TreeDocument> BuildOperation(JObject opJson)
        {
            var op = RequireString(opJson, "op");
            switch (op)
            {
                case "add":
                {
                    var node = McpAuthoringJson.ReadNode(RequireObject(opJson, "node"));
                    var parentId = new NodeId(RequireString(opJson, "parentId"));
                    var insertIndex = opJson["insertIndex"]?.Value<int>();
                    return document => SemanticEditOperations.AddNode(document, node, parentId, insertIndex);
                }
                case "remove":
                {
                    var nodeId = new NodeId(RequireString(opJson, "nodeId"));
                    return document => SemanticEditOperations.RemoveNode(document, nodeId);
                }
                case "move":
                {
                    var nodeId = new NodeId(RequireString(opJson, "nodeId"));
                    var newParentId = new NodeId(RequireString(opJson, "newParentId"));
                    var insertIndex = opJson["insertIndex"]?.Value<int>();
                    return document => McpAuthoringOperations.Move(document, nodeId, newParentId, insertIndex);
                }
                case "replace":
                {
                    var nodeId = new NodeId(RequireString(opJson, "nodeId"));
                    var newTypeId = RequireString(opJson, "newTypeId");
                    var newTypeVersion = opJson["newTypeVersion"]?.Value<int>() ?? 1;
                    var newParameters = McpAuthoringJson.ReadParameters(opJson["newParameters"]);
                    return document => McpAuthoringOperations.Replace(document, nodeId, newTypeId, newTypeVersion, newParameters);
                }
                case "configure":
                {
                    var nodeId = new NodeId(RequireString(opJson, "nodeId"));
                    var parameterName = RequireString(opJson, "parameterName");
                    var value = McpAuthoringJson.ReadValue(RequireToken(opJson, "value"));
                    return document => SemanticEditOperations.SetParameter(document, nodeId, parameterName, value);
                }
                case "setBlackboard":
                {
                    var keys = McpAuthoringJson.ReadBlackboardKeys((JArray)RequireToken(opJson, "keys"));
                    return document => McpAuthoringOperations.SetBlackboard(document, keys);
                }
                default:
                    throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Unknown domain-patch operation: " + op);
            }
        }

        // ---- request_layout ------------------------------------------------------------------

        internal static JObject RequestLayout(string projectRoot, JObject args)
        {
            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            var dryRun = args["dryRun"]?.Value<bool>() ?? false;

            var existing = LayoutPersistenceController.Load(path, document);
            if (!existing.Success)
            {
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "The existing layout file is invalid: " + string.Join("; ", existing.Diagnostics.Select(d => d.Message)));
            }

            // No *.aibt.layout.json exists yet: LayoutPersistenceController.Load already computed
            // a complete fresh default (P3-004's fallback). There is nothing prior to protect via
            // a hash precondition -- same "no precondition for a brand-new resource" reasoning as
            // create_tree -- so this bootstraps the file directly instead of requiring an
            // expectedHash the caller could never have obtained in advance.
            if (existing.UsedDefault)
            {
                var freshHash = StableHash.Sha256Hex(CanonicalLayoutJsonWriter.Write(existing.Document));
                if (!dryRun)
                {
                    LayoutPersistenceController.Save(path, existing.Document);
                }

                return new JObject
                {
                    ["accepted"] = true,
                    ["hash"] = freshHash,
                    ["diff"] = WriteLayoutDiff(LayoutDiff.Between(
                        new LayoutDocument(document.TreeId, LayoutDirection.TopToBottom, new Dictionary<NodeId, LayoutNodePlacement>()),
                        existing.Document)),
                    ["diagnostics"] = WriteDiagnostics(DiagnosticCollection.Empty),
                };
            }

            var expectedHash = RequireString(args, "expectedHash");
            var result = LayoutPatchTransaction.Apply(existing.Document, expectedHash, new Func<LayoutDocument, LayoutDocument>[]
            {
                current => DeterministicAutoLayoutService.Layout(document, current),
            });

            if (result.Accepted && !dryRun)
            {
                LayoutPersistenceController.Save(path, result.Document);
            }

            return new JObject
            {
                ["accepted"] = result.Accepted,
                ["hash"] = result.ResultHash,
                ["diff"] = WriteLayoutDiff(result.Diff),
                ["diagnostics"] = WriteDiagnostics(result.Diagnostics),
            };
        }

        // ---- shared plumbing ------------------------------------------------------------------

        private static JObject ApplySemanticPatch(string projectRoot, JObject args, IReadOnlyList<Func<TreeDocument, TreeDocument>> operations)
        {
            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            return ApplySemanticPatchToDocument(projectRoot, document, path, args, operations);
        }

        // TreeDocument.Revision is real (SemanticEditOperations.Rebuild increments it) but is a
        // pure in-memory session concept -- CanonicalTreeJsonWriter never writes it to
        // *.aibt.json, and CanonicalTreeJson.ReadDocument hard-codes `default` (-> 1) on every
        // parse (confirmed by reading both directly; a real save-then-reload round trip proved
        // it live). Every MCP call here reloads fresh from disk with no live session, so
        // Revision is always 1 immediately after LoadTreeOrThrow and cannot detect a concurrent
        // change between two separate calls -- SemanticPatchTransaction.Apply's own revision
        // precondition (Editor/Patching/, outside this card's allowed changes) would always
        // trivially pass. Fixed the same way ADR-P6-002 already fixed the identical problem for
        // LayoutDocument (which also has no persisted revision field): a computed canonical
        // content-hash precondition, checked here before ever calling into the transaction, with
        // SemanticPatchTransaction.Apply given the just-loaded document's own actual (trivially
        // matching) revision so its own precondition never rejects a hash-verified call.
        private static JObject ApplySemanticPatchToDocument(string projectRoot, TreeDocument document, string path, JObject args, IReadOnlyList<Func<TreeDocument, TreeDocument>> operations)
        {
            var expectedHash = args["expectedHash"]?.Value<string>()
                ?? throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Missing required property 'expectedHash'.");
            var dryRun = args["dryRun"]?.Value<bool>() ?? false;

            var actualHash = ComputeSemanticHash(document);
            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
            {
                var mismatch = new Diagnostic(
                    McpAuthoringDiagnostics.ContentHashMismatch,
                    DiagnosticSeverity.Error,
                    "Expected content hash " + expectedHash + " but the document hash is " + actualHash + ".",
                    new DiagnosticLocation(treeId: document.TreeId));
                return new JObject
                {
                    ["accepted"] = false,
                    ["contentHash"] = actualHash,
                    ["diff"] = WriteSemanticDiff(SemanticDiff.Empty),
                    ["diagnostics"] = WriteDiagnostics(new DiagnosticCollection(new[] { mismatch })),
                };
            }

            var (registry, options) = BuildRegistryAndOptions(projectRoot, path);
            var result = SemanticPatchTransaction.Apply(document, document.Revision.Value, operations, registry, options);

            if (result.Accepted && !dryRun)
            {
                var writeDiagnostics = TreeDocumentPersistence.Save(path, result.Document);
                if (writeDiagnostics.Count > 0)
                {
                    return new JObject
                    {
                        ["accepted"] = false,
                        ["contentHash"] = actualHash,
                        ["diff"] = WriteSemanticDiff(SemanticDiff.Empty),
                        ["diagnostics"] = WriteDiagnostics(writeDiagnostics),
                    };
                }
            }

            return new JObject
            {
                ["accepted"] = result.Accepted,
                ["contentHash"] = result.Accepted ? ComputeSemanticHash(result.Document) : actualHash,
                ["diff"] = WriteSemanticDiff(result.Diff),
                ["diagnostics"] = WriteDiagnostics(result.Diagnostics),
            };
        }

        /// <summary>
        /// Canonical semantic-content hash of a TreeDocument -- reuses CanonicalTreeJson.Serialize's
        /// own already-computed SemanticHash (semantic-only canonical bytes, excluding
        /// name/description/tags/metadata, the same "cosmetic fields don't count" rule the tree's
        /// own semantic-hash concept in canonical-json-v1.md already establishes) rather than
        /// hashing a second time.
        /// </summary>
        private static string ComputeSemanticHash(TreeDocument document)
        {
            var serialized = CanonicalTreeJson.Serialize(document);
            if (!serialized.Success)
            {
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments,
                    "Document is not representable: " + string.Join("; ", serialized.Diagnostics.Select(d => d.Message)));
            }

            return ToLowercaseHex(serialized.SemanticHash);
        }

        private static string ToLowercaseHex(byte[] bytes)
        {
            var characters = new char[bytes.Length * 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                var value = bytes[index];
                characters[index * 2] = ToHexChar(value >> 4);
                characters[(index * 2) + 1] = ToHexChar(value & 0x0f);
            }

            return new string(characters);
        }

        private static char ToHexChar(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

        private static (TreeDocument Document, string Path) LoadTreeOrThrow(string projectRoot, JObject args)
        {
            var treeId = new TreeId(RequireString(args, "treeId"));
            var scan = AibtTreeDiscovery.Scan(projectRoot);
            if (!scan.TryFindPath(treeId, out var path))
            {
                throw new McpToolException(McpAuthoringDiagnostics.TreeNotFound, "No tree with id '" + treeId.Value + "' was found under the project.");
            }

            var loaded = TreeDocumentPersistence.Load(path);
            if (!loaded.Success)
            {
                throw new McpToolException(McpAuthoringDiagnostics.TreeNotFound, "Tree '" + treeId.Value + "' could not be parsed: " + string.Join("; ", loaded.Diagnostics.Select(d => d.Message)));
            }

            return (loaded.Document, path);
        }

        /// <summary>
        /// Resolves a caller-supplied relative create-tree path to an absolute one, rejecting
        /// anything that would escape <paramref name="projectRoot"/> (Application.dataPath) --
        /// the same path-traversal reasoning already applied to the static-resource allowlist in
        /// <see cref="McpToolDispatcher.Dispatch"/>. Rejects an existing file rather than
        /// silently overwriting it.
        /// </summary>
        private static string ResolveNewTreePath(string projectRoot, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || !relativePath.EndsWith(".aibt.json", StringComparison.Ordinal))
            {
                throw new McpToolException(McpAuthoringDiagnostics.InvalidCreatePath, "Path must be a non-empty relative path ending in '.aibt.json'.");
            }

            var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectRoot, relativePath));
            var rootFull = System.IO.Path.GetFullPath(projectRoot) + System.IO.Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootFull, StringComparison.Ordinal))
            {
                throw new McpToolException(McpAuthoringDiagnostics.InvalidCreatePath, "Path must stay under the project's Assets directory.");
            }

            if (System.IO.File.Exists(combined))
            {
                throw new McpToolException(McpAuthoringDiagnostics.TreeAlreadyExists, "A file already exists at '" + relativePath + "'.");
            }

            return combined;
        }

        private static (NodeRegistry Registry, ReferenceCompilerOptions Options) BuildRegistryAndOptions(string projectRoot, string absolutePath)
        {
            var buildResult = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            var sourceId = ToLogicalSourceId(projectRoot, absolutePath);
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, CompilerVersion);
            return (buildResult.Registry, options);
        }

        /// <summary>
        /// ReferenceCompilerOptions.SourceId must be a relative, forward-slash, ".."-free
        /// logical path (AIBT3010) -- never the absolute, backslash, drive-lettered path
        /// TreeDocumentPersistence/AibtTreeDiscovery use for real file I/O.
        /// </summary>
        private static string ToLogicalSourceId(string projectRoot, string absolutePath)
        {
            return System.IO.Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        }

        private static JObject WriteSemanticDiff(SemanticDiff diff)
        {
            var entries = new JArray();
            foreach (var entry in diff.Entries)
            {
                entries.Add(new JObject { ["nodeId"] = entry.NodeId.Value, ["kind"] = entry.Kind.ToString() });
            }

            return new JObject { ["entries"] = entries };
        }

        private static JObject WriteLayoutDiff(LayoutDiff diff)
        {
            var entries = new JArray();
            foreach (var entry in diff.Entries)
            {
                entries.Add(new JObject { ["target"] = entry.Target.ToString(), ["key"] = entry.Key, ["kind"] = entry.Kind.ToString() });
            }

            return new JObject { ["entries"] = entries };
        }

        private static JArray WriteDiagnostics(DiagnosticCollection diagnostics)
        {
            var array = new JArray();
            foreach (var diagnostic in diagnostics)
            {
                array.Add(new JObject
                {
                    ["code"] = diagnostic.Code.Value,
                    ["severity"] = diagnostic.Severity.ToString(),
                    ["message"] = diagnostic.Message,
                    ["treeId"] = diagnostic.Location.TreeId.IsValid ? diagnostic.Location.TreeId.Value : null,
                    ["nodeId"] = diagnostic.Location.NodeId.IsValid ? diagnostic.Location.NodeId.Value : null,
                    ["jsonPointer"] = diagnostic.Location.JsonPointer,
                });
            }

            return array;
        }

        private static string RequireString(JObject json, string property)
        {
            var value = json[property]?.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Missing required string property '" + property + "'.");
            }

            return value;
        }

        private static JObject RequireObject(JObject json, string property)
        {
            if (!(json[property] is JObject value))
            {
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Missing required object property '" + property + "'.");
            }

            return value;
        }

        private static JToken RequireToken(JObject json, string property)
        {
            var value = json[property];
            if (value == null)
            {
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Missing required property '" + property + "'.");
            }

            return value;
        }
    }
}
