using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Editor.Registry;

namespace AIBT.Editor.Graph
{
    public static class ProductionTreeAssetBaker
    {
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
            return ProductionTreeAsset.Create(compiled.Program.CreateNativeBlackboardBindingV2(catalog.RegisteredTypes), handshake.Catalog);
        }
    }
}
