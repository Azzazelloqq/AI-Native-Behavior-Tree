using System;
using System.IO;
using System.Linq;
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
        /// automatically after the source is saved or the catalog changes (P9-004).
        /// </summary>
        public static ProductionTreeAsset Create(TreeDocument document, string sourceId, Type catalogType,
            out DiagnosticCollection diagnostics)
        {
            var catalog = ProjectNodeCatalog.ForCatalog(catalogType);
            if (catalog.Problems.Count > 0)
                throw new InvalidOperationException("Resolve project node catalog diagnostics before baking: " + string.Join("\n", catalog.Problems));
            var missing = document.Nodes.Where(node => !node.TypeId.StartsWith("aibt.core.", StringComparison.Ordinal) && !catalog.Generated.ContainsKey(node.TypeId)).ToArray();
            if (missing.Length != 0)
            {
                diagnostics = new DiagnosticCollection(missing.Select(node => new Diagnostic(ReferenceCompilerDiagnosticCodes.UnsupportedCapability,
                    DiagnosticSeverity.Error, "Selected native catalog does not declare node '" + node.TypeId + "'.",
                    new DiagnosticLocation(treeId: document.TreeId, nodeId: node.Id))));
                return null;
            }
            var compiled = GeneratedCompiledProgramV2Compiler.Compile(document, catalog.Generated.Values, catalog.RegisteredTypes,
                new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            diagnostics = compiled.Diagnostics;
            if (!compiled.Success) return null;
            if (!GeneratedNodeReflectionHarness.TryReflectHandshake(catalogType, out var handshake, out var explanation))
                throw new InvalidOperationException(explanation);
            var asset = ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(catalog.RegisteredTypes), handshake.Catalog);
            var guid = string.IsNullOrEmpty(sourceId) ? string.Empty : AssetDatabase.AssetPathToGUID(sourceId);
            var hash = !string.IsNullOrEmpty(guid) && File.Exists(sourceId) ? StableHash.Sha256Hex(File.ReadAllBytes(sourceId)) : string.Empty;
            asset.RecordPreparation(guid, hash, catalogType.FullName + ", " + catalogType.Assembly.GetName().Name);
            return asset;
        }
    }
}
