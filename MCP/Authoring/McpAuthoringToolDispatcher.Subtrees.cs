using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Graph;
using AIBT.Editor.Patching;
using AIBT.Editor.Registry;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// The subtree-reference tools of ADR-P11-001 §4.7 (stage 4): a reference node through
    /// <c>add_node</c>, <c>set_subtree_keys</c>, <c>extract_to_subtree</c>, <c>unpack_subtree</c> and
    /// <c>find_subtree_usages</c>. The edits themselves are the graph editor's
    /// (<see cref="SubtreeGraphEdits"/>); like every other authoring tool they go through
    /// <see cref="SemanticPatchTransaction"/> with the content-hash precondition, and an edit is
    /// accepted only when the resulting tree, expanded through the project's trees, compiles.
    /// Not to be confused with <c>extract_subtree</c>/<c>inline_subtree</c> (P6-006), which move a
    /// node payload and create no reference.
    /// </summary>
    internal static partial class McpAuthoringToolDispatcher
    {
        // ---- add_node with a `subtree` object ------------------------------------------------

        /// <summary>
        /// The edit that adds a reference node described by <paramref name="nodeJson"/>
        /// (<c>{id?, subtree: {treeId, interfaceVersion?, keys?}, displayName?, description?, tags?}</c>).
        /// A missing <c>id</c> becomes a short ID made from the subtree's tree ID; a missing
        /// <c>interfaceVersion</c> is the subtree's current one; unless
        /// <paramref name="connectSameNamedKeys"/> is false, every interface key the caller did not
        /// mention is connected to the parent's Tree key of the same ID and type (as the graph
        /// editor does); a key given as null stays not connected. The tree is raised to format 3.
        /// </summary>
        private static Func<TreeDocument, TreeDocument> AddReferenceOperation(string projectRoot, JObject nodeJson, NodeId parentId,
            int? insertIndex, bool connectSameNamedKeys)
        {
            var generateId = nodeJson["id"] == null || nodeJson["id"].Type == JTokenType.Null;
            var payload = (JObject)nodeJson.DeepClone();
            if (generateId) payload["id"] = "pending";
            NodeDocument parsed;
            List<string> disconnected;
            try
            {
                parsed = McpAuthoringJson.ReadReferenceNode(payload);
                McpAuthoringJson.ReadSubtreeReference((JObject)payload["subtree"], out disconnected);
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentException || exception is InvalidCastException)
            {
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, exception.Message);
            }

            var requested = parsed.Subtree;
            var resolution = McpSubtrees.Resolver(projectRoot).Resolve(requested.TreeId);
            var subtree = resolution.Status == SubtreeResolutionStatus.Found ? resolution.Document : null;
            return document =>
            {
                var version = requested.InterfaceVersion != 0 ? requested.InterfaceVersion
                    : subtree?.Interface != null && subtree.Interface.Version != 0 ? subtree.Interface.Version : 1u;
                var keys = requested.Keys.ToList();
                if (connectSameNamedKeys && subtree != null)
                {
                    keys = SubtreeGraphEdits.SameNamedBindings(document, subtree, keys)
                        .Where(binding => requested.Keys.Contains(binding) || !disconnected.Contains(binding.InterfaceKey)).ToList();
                }

                var id = generateId ? SubtreeGraphEdits.NewReferenceId(document, SubtreeGraphEdits.SeedFor(requested.TreeId)) : parsed.Id;
                var node = NodeDocument.CreateSubtreeReference(id, new SubtreeReference(requested.TreeId, version, keys),
                    parsed.DisplayName, parsed.Description, parsed.Tags);
                return SemanticEditOperations.AddNode(SubtreeGraphEdits.WithSubtreeFormat(document), node, parentId, insertIndex);
            };
        }

        // ---- set_subtree_keys -----------------------------------------------------------------

        internal static JObject SetSubtreeKeys(string projectRoot, JObject args)
        {
            var nodeId = new NodeId(RequireString(args, "nodeId"));
            var keysJson = RequireObject(args, "keys");
            List<SubtreeKeyBinding> bindings;
            List<string> disconnected;
            try { bindings = McpAuthoringJson.ReadKeyBindings(keysJson, out disconnected); }
            catch (FormatException exception) { throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, exception.Message); }
            uint? version = null;
            if (args["interfaceVersion"] != null && args["interfaceVersion"].Type != JTokenType.Null)
            {
                var value = args["interfaceVersion"].Value<long>();
                if (value < 1 || value > uint.MaxValue) throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "interfaceVersion must be at least 1.");
                version = (uint)value;
            }
            var connect = args["connectSameNamedKeys"]?.Value<bool>() ?? false;

            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            var reference = RequireReference(document, nodeId);
            TreeDocument subtree = null;
            if (connect)
            {
                var resolution = McpSubtrees.Resolver(projectRoot).Resolve(reference.Subtree.TreeId);
                if (resolution.Status == SubtreeResolutionStatus.Found) subtree = resolution.Document;
            }

            return ApplySemanticPatchToDocument(projectRoot, document, path, args, new Func<TreeDocument, TreeDocument>[]
            {
                current =>
                {
                    foreach (var binding in bindings) current = SubtreeGraphEdits.SetKeyBinding(current, nodeId, binding.InterfaceKey, binding);
                    foreach (var key in disconnected) current = SubtreeGraphEdits.SetKeyBinding(current, nodeId, key, null);
                    if (version.HasValue) current = SubtreeGraphEdits.SetInterfaceVersion(current, nodeId, version.Value);
                    if (subtree != null) current = SubtreeGraphEdits.ConnectSameNamedKeys(current, nodeId, subtree);
                    return current;
                },
            });
        }

        // ---- unpack_subtree -------------------------------------------------------------------

        internal static JObject UnpackSubtree(string projectRoot, JObject args)
        {
            var nodeId = new NodeId(RequireString(args, "nodeId"));
            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            var actualHash = ComputeSemanticHash(document);
            if (!string.Equals(RequireString(args, "expectedHash"), actualHash, StringComparison.Ordinal)) return HashMismatch(document, (string)args["expectedHash"], actualHash);
            RequireReference(document, nodeId);

            var catalog = ProjectNodeCatalogCache.Current;
            var sourceId = ToLogicalSourceId(projectRoot, path);
            var unpacked = SubtreeGraphEdits.Unpack(document, nodeId, McpSubtrees.Resolver(projectRoot), catalog.Registry, catalog.RegisteredTypes,
                out var diagnostics, out var innerRoot, sourceId);
            if (unpacked == null)
            {
                return new JObject
                {
                    ["accepted"] = false,
                    ["contentHash"] = actualHash,
                    ["diff"] = WriteSemanticDiff(SemanticDiff.Empty),
                    ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
                };
            }

            var response = ApplySemanticPatchToDocument(projectRoot, document, path, args, new Func<TreeDocument, TreeDocument>[] { _ => unpacked });
            if ((bool)response["accepted"]) response["innerRootId"] = innerRoot.Value;
            return response;
        }

        // ---- extract_to_subtree ---------------------------------------------------------------

        /// <summary>
        /// Moves a node and everything below it into a new tree file and puts one reference node in
        /// its place (<see cref="SubtreeGraphEdits.Extract"/>). The new tree and the edited parent are
        /// validated together (the parent through its expansion with the new tree) before anything is
        /// written; then the new file is written first and the parent second, and if writing the
        /// parent fails the new file is deleted again, so no orphan file is left.
        /// </summary>
        internal static JObject ExtractToSubtree(string projectRoot, JObject args)
        {
            var top = new NodeId(RequireString(args, "nodeId"));
            var newTreeId = new TreeId(RequireString(args, "newTreeId"));
            if (!newTreeId.IsValid) throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "newTreeId is not a valid tree ID.");
            var relativePath = RequireString(args, "path");
            var name = args["name"]?.Value<string>();
            var description = args["description"]?.Value<string>();
            var dryRun = args["dryRun"]?.Value<bool>() ?? false;

            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            var actualHash = ComputeSemanticHash(document);
            if (!string.Equals(RequireString(args, "expectedHash"), actualHash, StringComparison.Ordinal)) return HashMismatch(document, (string)args["expectedHash"], actualHash);
            var newPath = ResolveNewTreePath(projectRoot, relativePath);
            var scan = AibtTreeDiscovery.Scan(projectRoot);
            if (scan.Trees.Any(tree => tree.TreeId == newTreeId))
                throw new McpToolException(McpAuthoringDiagnostics.TreeAlreadyExists, "A tree with id '" + newTreeId.Value + "' already exists; a second one would make every reference to it ambiguous (AIBT3021).");

            var catalog = ProjectNodeCatalogCache.Current;
            SubtreeGraphEdits.Extraction extraction;
            try { extraction = SubtreeGraphEdits.Extract(document, top, newTreeId, name, catalog.Registry); }
            catch (ArgumentException exception) { throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, exception.Message); }
            extraction = WithDescriptions(extraction, document, top, description);

            var (_, parentOptions) = BuildRegistryAndOptions(projectRoot, path);
            var (_, subtreeOptions) = BuildRegistryAndOptions(projectRoot, newPath);
            var resolver = new McpSubtreeResolver(scan, file => ToLogicalSourceId(projectRoot, file))
                .Override(extraction.Subtree, subtreeOptions.SourceId);
            var subtreeDiagnostics = catalog.ValidateAuthoring(extraction.Subtree, subtreeOptions, resolver);
            var result = SemanticPatchTransaction.Apply(document, document.Revision.Value,
                new Func<TreeDocument, TreeDocument>[] { _ => extraction.Parent },
                candidate => catalog.ValidateAuthoring(candidate, parentOptions, resolver.Override(candidate, parentOptions.SourceId)));
            var subtreeValid = !subtreeDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var accepted = result.Accepted && subtreeValid;
            var diagnostics = new DiagnosticCollection(subtreeDiagnostics.Concat(result.Diagnostics));

            var response = new JObject
            {
                ["accepted"] = accepted,
                ["contentHash"] = accepted ? ComputeSemanticHash(result.Document) : actualHash,
                ["diff"] = WriteSemanticDiff(accepted ? result.Diff : SemanticDiff.Empty),
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
                ["referenceNodeId"] = extraction.ReferenceId.Value,
                ["subtree"] = new JObject
                {
                    ["treeId"] = newTreeId.Value,
                    ["path"] = subtreeOptions.SourceId,
                    ["contentHash"] = ComputeSemanticHash(extraction.Subtree),
                    ["interface"] = McpSubtrees.WriteInterface(extraction.Subtree.Interface),
                },
            };
            if (!accepted || dryRun) return response;

            var written = TreeDocumentPersistence.Save(newPath, extraction.Subtree);
            if (written.Count > 0) return WriteFailure(response, actualHash, written);
            DiagnosticCollection parentWritten;
            try { parentWritten = TreeDocumentPersistence.Save(path, result.Document); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                File.Delete(newPath);
                throw new McpToolException(McpAuthoringDiagnostics.WriteFailed, "The parent tree could not be written (" + exception.Message + "); the new subtree file was removed again.");
            }
            if (parentWritten.Count > 0)
            {
                File.Delete(newPath);
                return WriteFailure(response, actualHash, parentWritten);
            }

            return response;
        }

        // ---- find_subtree_usages ----------------------------------------------------------------

        /// <summary>
        /// Every reference node in the project's trees that names <c>treeId</c>, with its tree, file,
        /// node ID and key map, and the files that declare the tree ID (none: references to it report
        /// <c>AIBT3020</c>; several: <c>AIBT3021</c>). Direct references only.
        /// </summary>
        internal static JObject FindSubtreeUsages(string projectRoot, JObject args)
        {
            var treeId = new TreeId(RequireString(args, "treeId"));
            var scan = AibtTreeDiscovery.Scan(projectRoot);
            var declared = new JArray();
            var usages = new List<JObject>();
            for (var index = 0; index < scan.Trees.Count; index++)
            {
                var tree = scan.Trees[index];
                var source = ToLogicalSourceId(projectRoot, scan.TreePaths[index]);
                if (tree.TreeId == treeId)
                    declared.Add(new JObject { ["path"] = source, ["name"] = tree.Name, ["interface"] = McpSubtrees.WriteInterface(tree.Interface) });
                foreach (var node in tree.Nodes.Where(node => node.Subtree != null && node.Subtree.TreeId == treeId))
                {
                    usages.Add(new JObject
                    {
                        ["treeId"] = tree.TreeId.Value,
                        ["path"] = source,
                        ["nodeId"] = node.Id.Value,
                        ["displayName"] = node.DisplayName,
                        ["subtree"] = McpAuthoringJson.WriteSubtreeReference(node.Subtree),
                    });
                }
            }

            var ordered = usages.OrderBy(item => (string)item["treeId"], StringComparer.Ordinal)
                .ThenBy(item => (string)item["path"], StringComparer.Ordinal)
                .ThenBy(item => (string)item["nodeId"], StringComparer.Ordinal);
            return new JObject
            {
                ["treeId"] = treeId.Value,
                ["status"] = declared.Count == 0 ? "NotFound" : declared.Count == 1 ? "Found" : "Ambiguous",
                ["declaredIn"] = declared,
                ["usageCount"] = usages.Count,
                ["usages"] = new JArray(ordered),
                ["skippedTreeFiles"] = new JArray(scan.SkippedFiles.Select(file => ToLogicalSourceId(projectRoot, file))),
            };
        }

        // ---- helpers ----------------------------------------------------------------------------

        /// <summary>
        /// The new tree gets <paramref name="description"/> (when given) and the reference node the
        /// moved top node's description, so a project policy that requires descriptions accepts both.
        /// </summary>
        private static SubtreeGraphEdits.Extraction WithDescriptions(SubtreeGraphEdits.Extraction extraction, TreeDocument document, NodeId top,
            string description)
        {
            var subtree = extraction.Subtree;
            if (!string.IsNullOrWhiteSpace(description))
            {
                subtree = new TreeDocument(subtree.Format, subtree.FormatVersion, subtree.TreeId, subtree.Name, subtree.Root, subtree.Nodes,
                    subtree.Blackboard, description.Trim(), subtree.Tags, subtree.Metadata, default, subtree.AgentContract, subtree.SharedContract,
                    subtree.Interface);
            }

            var topDescription = document.Nodes.First(node => node.Id == top).Description;
            var parent = extraction.Parent;
            if (!string.IsNullOrWhiteSpace(topDescription))
            {
                parent = GraphDocumentEdits.Rebuild(parent, parent.Nodes.Select(node => node.Id == extraction.ReferenceId
                    ? NodeDocument.CreateSubtreeReference(node.Id, node.Subtree, node.DisplayName, topDescription, node.Tags)
                    : node));
            }

            return new SubtreeGraphEdits.Extraction { Parent = parent, Subtree = subtree, ReferenceId = extraction.ReferenceId };
        }

        private static NodeDocument RequireReference(TreeDocument document, NodeId nodeId)
        {
            var node = document.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId)
                ?? throw new McpToolException(McpAuthoringDiagnostics.NodeNotFound, "Node '" + nodeId.Value + "' does not exist in tree '" + document.TreeId.Value + "'.");
            if (node.Subtree == null)
                throw new McpToolException(McpAuthoringDiagnostics.MalformedArguments, "Node '" + nodeId.Value + "' is not a subtree reference.");
            return node;
        }

        private static JObject HashMismatch(TreeDocument document, string expectedHash, string actualHash)
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
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(new DiagnosticCollection(new[] { mismatch })),
            };
        }

        private static JObject WriteFailure(JObject response, string actualHash, DiagnosticCollection diagnostics)
        {
            response["accepted"] = false;
            response["contentHash"] = actualHash;
            response["diff"] = WriteSemanticDiff(SemanticDiff.Empty);
            response["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics);
            return response;
        }
    }
}
