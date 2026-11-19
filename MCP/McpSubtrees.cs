using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp
{
    /// <summary>
    /// Resolves subtree references (ADR-P11-001) against the trees <see cref="AibtTreeDiscovery"/>
    /// found under the project's <c>Assets</c> folder, as every MCP tool addresses trees. A tree ID
    /// declared by several files is ambiguous (<c>AIBT3021</c>). Documents not yet on disk (a tree
    /// being edited, a subtree being extracted) can replace the scanned file of their tree ID.
    /// </summary>
    internal sealed class McpSubtreeResolver : ISubtreeResolver
    {
        private readonly Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>> _entries
            = new Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>>();
        private readonly Dictionary<TreeId, KeyValuePair<string, TreeDocument>> _overrides
            = new Dictionary<TreeId, KeyValuePair<string, TreeDocument>>();
        private readonly IReadOnlyList<string> _skipped;

        /// <param name="sourceIdForPath">Logical source ID of a scanned file (an absolute path).</param>
        internal McpSubtreeResolver(AibtTreeDiscovery.ScanResult scan, Func<string, string> sourceIdForPath)
        {
            for (var index = 0; index < scan.Trees.Count; index++)
            {
                var document = scan.Trees[index];
                if (!_entries.TryGetValue(document.TreeId, out var list)) _entries.Add(document.TreeId, list = new List<KeyValuePair<string, TreeDocument>>());
                list.Add(new KeyValuePair<string, TreeDocument>(sourceIdForPath(scan.TreePaths[index]), document));
            }
            _skipped = scan.SkippedFiles.Select(sourceIdForPath).ToList();
        }

        /// <summary>Resolves <paramref name="document"/>'s tree ID to it instead of the scanned files.</summary>
        internal McpSubtreeResolver Override(TreeDocument document, string sourceId)
        {
            _overrides[document.TreeId] = new KeyValuePair<string, TreeDocument>(sourceId, document);
            return this;
        }

        public SubtreeResolution Resolve(TreeId treeId)
        {
            if (_overrides.TryGetValue(treeId, out var replaced)) return SubtreeResolution.Found(replaced.Value, replaced.Key);
            if (!_entries.TryGetValue(treeId, out var list))
                return SubtreeResolution.NotFound(_skipped.Count == 0 ? null : "Tree files that could not be read: " + string.Join(", ", _skipped) + ".");
            return list.Count == 1 ? SubtreeResolution.Found(list[0].Value, list[0].Key) : SubtreeResolution.Ambiguous(list.Select(pair => pair.Key));
        }
    }

    /// <summary>Shared subtree helpers of the MCP tools (ADR-P11-001 §4.7, stage 4).</summary>
    internal static class McpSubtrees
    {
        /// <summary>
        /// Structural expansion as at import (<c>ProjectTreeIndex.Expand</c>): every node domain and
        /// scope admitted, so reading an expansion never fails on a policy that only compiling enforces.
        /// </summary>
        internal static readonly ReferenceCompilationPolicy StructuralPolicy = new ReferenceCompilationPolicy(
            allowManagedNodes: true, allowMainThreadNodes: true, requireDeterministicNodes: false,
            supportsAgentScope: true, supportsSharedScope: true);

        /// <summary>The source ID MCP compiles a project tree under: its path relative to <c>Assets</c>, with '/'.</summary>
        internal static string LogicalSourceId(string projectRoot, string absolutePath)
            => Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');

        /// <summary>A resolver over the project's trees with source IDs relative to <c>Assets</c> (validate, compile, edits).</summary>
        internal static McpSubtreeResolver Resolver(string projectRoot)
            => new McpSubtreeResolver(AibtTreeDiscovery.Scan(projectRoot), path => LogicalSourceId(projectRoot, path));

        /// <summary>The resolver for <paramref name="document"/>, or null when it uses no subtree feature (nothing is scanned).</summary>
        internal static McpSubtreeResolver ResolverFor(TreeDocument document, string projectRoot)
            => document != null && SubtreeExpander.RequiresExpansion(document) ? Resolver(projectRoot) : null;

        /// <summary>Expands <paramref name="document"/> structurally (see <see cref="StructuralPolicy"/>).</summary>
        internal static SubtreeExpansion ExpandStructurally(TreeDocument document, string sourceId, ISubtreeResolver resolver,
            NodeRegistry registry, RegisteredBlackboardTypeCatalog registeredTypes)
            => SubtreeExpander.Expand(document, resolver, registry, StructuralPolicy.CreateValidationOptions(sourceId), registeredTypes);

        /// <summary>The distinct tree IDs <paramref name="document"/>'s reference nodes name, in ordinal order.</summary>
        internal static IReadOnlyList<string> ReferencedTreeIds(TreeDocument document)
            => document.Nodes.Where(node => node.Subtree != null).Select(node => node.Subtree.TreeId.Value)
                .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();

        /// <summary><c>{version, keys}</c>, or null when the tree declares no interface.</summary>
        internal static JObject WriteInterface(SubtreeInterface value)
            => value == null ? null : new JObject { ["version"] = value.Version, ["keys"] = new JArray(value.Keys) };
    }
}
