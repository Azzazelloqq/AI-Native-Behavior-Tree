using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Editor.Registry;
using UnityEditor;

namespace AIBT.Editor.Graph
{
    public static class ProductionTreeAssetBaker
    {
        /// <summary>
        /// Compiles <paramref name="document"/> for <paramref name="catalogType"/> into a new runtime
        /// asset. When <paramref name="sourceId"/> is a project asset path, the asset also records
        /// the source GUID, the source bytes' hash and the catalog type, so the Editor re-prepares it
        /// automatically after the source is saved or the catalog changes (P9-004). Subtree
        /// references are resolved against the project's trees (ADR-P11-001).
        /// </summary>
        public static ProductionTreeAsset Create(TreeDocument document, string sourceId, Type catalogType,
            out DiagnosticCollection diagnostics)
            => Create(document, sourceId, catalogType,
                document != null && SubtreeExpander.RequiresExpansion(document) ? ProjectTreeIndex.Scan() : null, out diagnostics);

        /// <summary>
        /// <see cref="Create(TreeDocument, string, Type, out DiagnosticCollection)"/> with subtree
        /// references resolved through <paramref name="resolver"/> (ADR-P11-001). The asset then also
        /// records the GUID of every subtree file it was prepared from, and its source content hash
        /// covers the source and those files (ordered by tree ID, with their GUIDs), so editing any
        /// of them marks the asset stale.
        /// </summary>
        public static ProductionTreeAsset Create(TreeDocument document, string sourceId, Type catalogType,
            ISubtreeResolver resolver, out DiagnosticCollection diagnostics)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var catalog = ProjectNodeCatalog.ForCatalog(catalogType);
            if (catalog.Problems.Count > 0)
                throw new InvalidOperationException("Resolve project node catalog diagnostics before baking: " + string.Join("\n", catalog.Problems));
            RecordingSubtreeResolver recorder = null;
            var documents = new List<TreeDocument> { document };
            if (SubtreeExpander.RequiresExpansion(document) && resolver != null)
            {
                recorder = new RecordingSubtreeResolver(resolver);
                recorder.Walk(document);
                documents.AddRange(recorder.Documents);
            }
            var missing = documents.SelectMany(item => item.Nodes)
                .Where(node => !node.IsSubtreeReference && !node.TypeId.StartsWith("aibt.core.", StringComparison.Ordinal) && !catalog.Generated.ContainsKey(node.TypeId))
                .ToArray();
            if (missing.Length != 0)
            {
                diagnostics = new DiagnosticCollection(missing.Select(node => new Diagnostic(ReferenceCompilerDiagnosticCodes.UnsupportedCapability,
                    DiagnosticSeverity.Error, MissingNodeMessage(node.TypeId),
                    new DiagnosticLocation(treeId: document.TreeId, nodeId: node.Id))));
                return null;
            }
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(document, catalog.Generated.Values, catalog.RegisteredTypes,
                new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)),
                (ISubtreeResolver)recorder ?? resolver);
            // P11-009 (review F10, native half): the program is unchanged; observer blocks get the warning AIBT2054.
            var observerWarnings = ProjectNodeCatalog.NativeObserverWarnings(document, sourceId);
            diagnostics = observerWarnings.Count == 0 ? compiled.Diagnostics : new DiagnosticCollection(compiled.Diagnostics.Concat(observerWarnings));
            if (!compiled.Success) return null;
            if (!GeneratedNodeReflectionHarness.TryReflectHandshake(catalogType, out var handshake, out var explanation))
                throw new InvalidOperationException(explanation);
            var asset = ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(catalog.RegisteredTypes), handshake.Catalog);
            var guid = string.IsNullOrEmpty(sourceId) ? string.Empty : AssetDatabase.AssetPathToGUID(sourceId);
            var subtrees = recorder == null ? new List<KeyValuePair<string, string>>() : recorder.Found.ToList();
            var hash = string.IsNullOrEmpty(guid) || !File.Exists(sourceId) ? string.Empty : SourceHash(sourceId, subtrees);
            asset.RecordPreparation(guid, hash, catalogType.FullName + ", " + catalogType.Assembly.GetName().Name);
            asset.RecordSubtreeSources(subtrees.Select(pair => AssetDatabase.AssetPathToGUID(pair.Value)).ToArray());
            return asset;
        }

        private const string StandardNativeShardTypeName = "AIBT.StandardNodes.StandardNativeShard, AIBT.StandardNodes";
        private static HashSet<string> _standardNativeTypeIds;

        // ADR-P11-002 §4.2: the package's native library is opt-in, so a tree that uses one of its
        // nodes with a catalog set that does not list the shard gets the fix in the message.
        internal static string MissingNodeMessage(string typeId)
        {
            var message = "Selected native catalog does not declare node '" + typeId + "'.";
            return StandardNativeTypeIds().Contains(typeId)
                ? message + " It is a package node of shard aibt.native.standard: add typeof(AIBT.StandardNodes.StandardNativeShard) to your catalog set ([AibtCatalogSet])."
                : message;
        }

        private static HashSet<string> StandardNativeTypeIds()
        {
            if (_standardNativeTypeIds != null) return _standardNativeTypeIds;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var shard = Type.GetType(StandardNativeShardTypeName, false);
            if (shard != null
                && GeneratedNodeReflectionHarness.TryReflectMetadata(shard, out var metadata, out _)
                && GeneratedNodeReflectionHarness.TryMaterializeArtifact(metadata, out var artifact, out _))
                foreach (var node in artifact.Nodes) ids.Add(node.Manifest.TypeId);
            return _standardNativeTypeIds = ids;
        }

        /// <summary>
        /// The staleness hash of a tree at <paramref name="path"/> as the project resolves its subtrees
        /// now, or null when a subtree cannot be resolved (missing or ambiguous tree ID).
        /// </summary>
        internal static string CurrentSourceHash(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var parsed = CanonicalTreeJson.Parse(bytes, path);
            if (!parsed.Success || !SubtreeExpander.RequiresExpansion(parsed.Document)) return StableHash.Sha256Hex(bytes);
            var recorder = new RecordingSubtreeResolver(ProjectTreeIndex.Scan());
            recorder.Walk(parsed.Document);
            return recorder.Complete ? SourceHash(path, recorder.Found.ToList()) : null;
        }

        // ADR-P11-001 §4.5: the source bytes' hash alone for a tree without subtrees (as before);
        // otherwise a hash over the source and every resolved subtree file, ordered by tree ID, with
        // their GUIDs.
        private static string SourceHash(string path, IReadOnlyList<KeyValuePair<string, string>> subtrees)
        {
            var own = StableHash.Sha256Hex(File.ReadAllBytes(path));
            if (subtrees.Count == 0) return own;
            var text = new StringBuilder("aibt.production-tree-sources.v1\n");
            text.Append(AssetDatabase.AssetPathToGUID(path)).Append(' ').Append(own).Append('\n');
            foreach (var pair in subtrees)
            {
                text.Append(pair.Key).Append(' ').Append(AssetDatabase.AssetPathToGUID(pair.Value)).Append(' ')
                    .Append(File.Exists(pair.Value) ? StableHash.Sha256Hex(File.ReadAllBytes(pair.Value)) : "missing").Append('\n');
            }
            return StableHash.Sha256Hex(Encoding.UTF8.GetBytes(text.ToString()));
        }
    }
}
