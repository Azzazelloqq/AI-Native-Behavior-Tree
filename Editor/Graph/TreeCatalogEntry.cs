using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using UnityEditor;

namespace AIBT.Editor.Graph
{
    /// <summary>A read-only source summary, shared by browser and imported-asset inspector.</summary>
    public sealed class TreeCatalogEntry
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public string TreeId { get; private set; }
        public string[] Tags { get; private set; } = Array.Empty<string>();
        public string Status { get; private set; }
        public string Details { get; private set; }
        public int NodeCount { get; private set; }

        /// <summary>
        /// The subtree expansion of a tree with references or an interface (ADR-P11-001); null for
        /// every other tree. On success the status and node count describe the expanded tree.
        /// </summary>
        internal SubtreeExpansion Expansion { get; private set; }

        /// <summary>
        /// The tree list's entries for <paramref name="paths"/>, in order. The project's tree files are
        /// read at most once for all of them, and only if one of them uses subtrees (P11-007, review F4).
        /// </summary>
        internal static List<TreeCatalogEntry> ReadAll(IEnumerable<string> paths, NodeRegistry registry, NativeFacts facts)
            => ReadAll(paths, registry, facts, null);

        /// <summary>
        /// As <see cref="ReadAll(IEnumerable{string}, NodeRegistry, NativeFacts)"/>, reading keys of the
        /// project's registered blackboard types with <paramref name="registeredTypes"/> (P11-014 U9: without
        /// them a tree with such a key was listed as "Errors" although it imports and compiles).
        /// </summary>
        internal static List<TreeCatalogEntry> ReadAll(IEnumerable<string> paths, NodeRegistry registry, NativeFacts facts,
            RegisteredBlackboardTypeCatalog registeredTypes)
            => ReadAll(paths, registry, facts, registeredTypes, null);

        /// <summary>
        /// As <see cref="ReadAll(IEnumerable{string}, NodeRegistry, NativeFacts, RegisteredBlackboardTypeCatalog)"/>,
        /// resolving subtrees among the tree files under <paramref name="roots"/> (null: the project's).
        /// The project's tree files are read with the same <paramref name="registeredTypes"/>.
        /// </summary>
        internal static List<TreeCatalogEntry> ReadAll(IEnumerable<string> paths, NodeRegistry registry, NativeFacts facts,
            RegisteredBlackboardTypeCatalog registeredTypes, IEnumerable<ProjectTreeIndex.ScanRoot> roots)
        {
            ProjectTreeIndex.Snapshot snapshot = null;
            Func<ProjectTreeIndex.Snapshot> scan = () => snapshot ?? (snapshot = registeredTypes == null
                ? (roots == null ? ProjectTreeIndex.Scan() : ProjectTreeIndex.Scan(roots))
                : ProjectTreeIndex.Scan(roots ?? ProjectTreeIndex.Roots(), registeredTypes));
            var entries = new List<TreeCatalogEntry>();
            foreach (var path in paths) entries.Add(Read(path, registry, null, facts, scan, registeredTypes));
            return entries;
        }

        public static TreeCatalogEntry Read(string path)
            => Read(path, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry);

        internal static TreeCatalogEntry Read(string path, NodeRegistry registry)
            => Read(path, registry, null);

        /// <param name="expansion">
        /// The expansion to describe for a tree that uses subtrees; when null it is expanded here
        /// against the project's trees with <paramref name="registry"/>.
        /// </param>
        internal static TreeCatalogEntry Read(string path, NodeRegistry registry, SubtreeExpansion expansion)
            => Read(path, registry, expansion, (NativeFacts)null);

        /// <param name="facts">
        /// The project's generated node code and prepared runtime assets (P11-005), used to describe
        /// a native tree; when null they are not checked and the entry says so.
        /// </param>
        internal static TreeCatalogEntry Read(string path, NodeRegistry registry, SubtreeExpansion expansion, NativeFacts facts)
            => Read(path, registry, expansion, facts, ProjectTreeIndex.Scan, null);

        /// <summary>
        /// The importer's entry (P11-014 review item 3): keys of registered blackboard types are read with
        /// <paramref name="registeredTypes"/>, so a valid tree with such a key is not "Errors" at import.
        /// </summary>
        internal static TreeCatalogEntry Read(string path, NodeRegistry registry, SubtreeExpansion expansion,
            RegisteredBlackboardTypeCatalog registeredTypes)
            => Read(path, registry, expansion, null,
                () => registeredTypes == null ? ProjectTreeIndex.Scan() : ProjectTreeIndex.Scan(registeredTypes), registeredTypes);

        private static TreeCatalogEntry Read(string path, NodeRegistry registry, SubtreeExpansion expansion, NativeFacts facts,
            Func<ProjectTreeIndex.Snapshot> scan, RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var entry = new TreeCatalogEntry { Path = path, Name = System.IO.Path.GetFileName(path), TreeId = "", Status = "Errors" };
            try
            {
                var parsed = registeredTypes == null
                    ? CanonicalTreeJson.Parse(File.ReadAllBytes(path), path)
                    : CanonicalTreeJson.Parse(File.ReadAllBytes(path), registeredTypes, path);
                entry.Details = string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                if (!parsed.Success) return entry;
                var document = parsed.Document;
                entry.Name = document.Name;
                entry.TreeId = document.TreeId.Value;
                entry.Tags = document.Tags.Values.ToArray();
                entry.NodeCount = document.Nodes.Count;
                if (SubtreeExpander.RequiresExpansion(document))
                {
                    entry.Expansion = expansion ?? ProjectTreeIndex.Expand(path, document, registry, registeredTypes, scan());
                    if (!entry.Expansion.Success)
                    {
                        entry.Details = string.Join("\n", entry.Expansion.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                        if (OnlyUnknownNodeTypes(entry.Expansion.Diagnostics))
                        {
                            // As for a tree without subtrees: node types the catalog does not know
                            // are not a source error; the tree is reported as needing a registry.
                            entry.Status = "Needs registry";
                            entry.Details = "Source parsed. Project node contracts are not validated here; the subtree expansion "
                                + "needs node types the project catalog does not know:\n" + entry.Details;
                        }
                        return entry;
                    }
                    document = entry.Expansion.Document;
                    entry.NodeCount = document.Nodes.Count;
                }
                var unresolved = document.Nodes.Where(n => !registry.TryGet(n.TypeId, out _))
                    .Select(n => n.TypeId).Distinct().ToArray();
                if (unresolved.Length != 0)
                {
                    entry.Status = "Needs registry";
                    entry.Details = "Source parsed. Project node contracts are not validated here: " + string.Join(", ", unresolved);
                    return entry;
                }
                // Trees with managed (C#) nodes run on BehaviorTreeAgent and are validated with the
                // managed policy, which accepts the main-thread nodes the native policy rejects; other
                // trees with the native default (Agent and Shared scope allowed). One helper, which MCP
                // validate/compile use too (ADR-P9-001 amendment 2026-11-24).
                var managed = ProjectNodeCatalog.UsesManagedNodes(document, registry);
                var options = ProjectNodeCatalog.DefaultPolicy(document, registry).CreateValidationOptions(path);
                var diagnostics = TreeValidator.Validate(document, registry, options, registeredTypes);
                // P11-008 (review F10): BehaviorTreeAgent refuses an observer block when it loads the tree.
                // P11-009 (review F10, native half): a native host does not re-evaluate observers (warning AIBT2054).
                diagnostics = new DiagnosticCollection(diagnostics.Concat(ProjectNodeCatalog.ObserverDiagnostics(document, registry, path)));
                // Project nodes without a reference handler run only from generated native code.
                var nativeTypes = managed ? Array.Empty<string>() : document.Nodes
                    .Where(node => registry.TryGet(node.TypeId, out var item) &&
                                   item.Source == NodeManifestSource.UserExtension && !item.HasReferenceHandlerBinding)
                    .Select(node => node.TypeId).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToArray();
                var withoutCode = facts == null ? Array.Empty<string>() : nativeTypes.Where(id => !facts.HasGeneratedCode(id)).ToArray();
                entry.Status = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? "Errors"
                    : diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning) ? "Warnings"
                    : withoutCode.Length != 0 ? "Needs native compile" : "Valid";
                entry.Details = string.Join("\n", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                if (entry.Status == "Valid" && managed)
                    entry.Details = "Node contracts validated; managed C# nodes run on a BehaviorTreeAgent.";
                else if (entry.Status == "Needs native compile")
                    entry.Details = "No generated code in the project for: " + string.Join(", ", withoutCode)
                        + ". It is written when scripts compile.";
                else if (entry.Status == "Valid" && nativeTypes.Length != 0)
                    entry.Details = facts == null
                        ? "Node contracts validated; generated code and prepared runtime assets are not checked here."
                        : facts.DescribePrepared(path);
                else if (entry.Status == "Valid")
                    entry.Details = "Node contracts validated; runtime compilation is a separate step.";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                entry.Details = exception.Message;
            }
            return entry;
        }

        /// <summary>
        /// True when every error of a failed expansion is <c>AIBT2021</c> (a node type the registry
        /// does not know). Any other error — a subtree that is missing, ambiguous or recursive, a key
        /// map error, any other validation error — keeps the status <c>Errors</c>.
        /// </summary>
        internal static bool OnlyUnknownNodeTypes(DiagnosticCollection diagnostics)
        {
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            return errors.Count != 0 && errors.All(d => d.Code == TreeValidationDiagnosticCodes.UnknownNodeType);
        }

        /// <summary>
        /// Facts the tree list checks for a native tree (P11-005), gathered once per list refresh:
        /// which node types have generated code in the project catalog, and which prepared runtime
        /// assets (<see cref="ProductionTreeAsset"/>) were compiled from which tree, as its source or
        /// as one of its subtrees. Whether a prepared asset is stale is not checked here.
        /// </summary>
        internal sealed class NativeFacts
        {
            private readonly IReadOnlyDictionary<string, GeneratedNodeDescriptor> _generated;
            private readonly IReadOnlyList<(string TreeGuid, string AssetGuid)> _prepared;

            /// <param name="prepared">Pairs of a tree source GUID and the GUID of a runtime asset prepared from it.</param>
            internal NativeFacts(IReadOnlyDictionary<string, GeneratedNodeDescriptor> generated,
                IReadOnlyList<(string TreeGuid, string AssetGuid)> prepared)
            {
                _generated = generated ?? throw new ArgumentNullException(nameof(generated));
                _prepared = prepared ?? Array.Empty<(string, string)>();
            }

            // FindAssets by type costs about 12 ms here, more than the whole list refresh, and the list
            // refreshes on every project change. The index is kept until an .asset file is imported,
            // deleted or moved (preparing a runtime asset writes one) or the domain reloads.
            private static IReadOnlyList<(string, string)> s_preparedIndex;

            /// <summary>The project's generated catalog and the recorded sources of every prepared runtime asset.</summary>
            internal static NativeFacts FromProject(ProjectNodeCatalog catalog)
                => new NativeFacts(catalog.Generated, s_preparedIndex ?? (s_preparedIndex = IndexPreparedAssets()));

            private static IReadOnlyList<(string, string)> IndexPreparedAssets()
            {
                var prepared = new List<(string, string)>();
                foreach (var assetGuid in AssetDatabase.FindAssets("t:" + nameof(ProductionTreeAsset)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetDatabase.GUIDToAssetPath(assetGuid));
                    if (asset == null) continue;
                    // Assets prepared before provenance was recorded name only their source path.
                    prepared.Add((string.IsNullOrEmpty(asset.SourceGuid) ? ProjectGuid(asset.SourceId) : asset.SourceGuid, assetGuid));
                    foreach (var subtree in asset.SubtreeSourceGuids) prepared.Add((subtree, assetGuid));
                }
                return prepared;
            }

            internal bool HasGeneratedCode(string typeId) => _generated.ContainsKey(typeId);

            internal string DescribePrepared(string treePath)
            {
                var treeGuid = ProjectGuid(treePath);
                var assets = string.IsNullOrEmpty(treeGuid) ? Array.Empty<string>() : _prepared
                    .Where(pair => pair.TreeGuid == treeGuid).Select(pair => AssetDatabase.GUIDToAssetPath(pair.AssetGuid))
                    .Where(path => !string.IsNullOrEmpty(path)).Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
                return assets.Length != 0
                    ? "Native nodes have generated code. Prepared runtime asset: " + string.Join(", ", assets) + "."
                    : "Native nodes have generated code. No runtime asset is prepared from this tree yet.";
            }

            // AssetPathToGUID logs a warning for a path outside the project (a test's temporary file).
            private static string ProjectGuid(string path)
                => path != null && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
                    ? AssetDatabase.AssetPathToGUID(path) : string.Empty;

            private sealed class PreparedAssetPostprocessor : AssetPostprocessor
            {
                private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
                {
                    if (s_preparedIndex == null) return;
                    if (imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                        .Any(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))) s_preparedIndex = null;
                }
            }
        }

        public bool Matches(string search, string tag, string status)
        {
            bool Contains(string value) => value.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
            return (Contains(Name) || Contains(TreeId) || Contains(Path))
                && (string.IsNullOrEmpty(tag) || tag == "All tags" || Tags.Contains(tag))
                && (string.IsNullOrEmpty(status) || status == "All states" || Status == status);
        }
    }
}
