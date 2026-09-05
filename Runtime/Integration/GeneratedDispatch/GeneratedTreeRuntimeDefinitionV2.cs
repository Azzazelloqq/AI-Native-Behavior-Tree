using System;
using AIBT.Burst;

namespace AIBT
{
    /// <summary>
    /// Immutable, shareable bootstrap definition for a compiled-v2 tree and one exact
    /// source-generated dispatch catalog.
    /// </summary>
    public sealed class GeneratedTreeRuntimeDefinitionV2
    {
        private GeneratedTreeRuntimeDefinitionV2(
            NativeProgramBlackboardBindingV2 binding,
            in BurstCatalogHandshake expectedCatalog)
        {
            Binding = binding;
            ExpectedCatalog = expectedCatalog;
        }

        public CompiledHash ContentHash => Binding.OuterContentHash;
        public BurstCatalogFingerprint CatalogFingerprint => ExpectedCatalog.Catalog;

        internal NativeProgramBlackboardBindingV2 Binding { get; }
        internal BurstCatalogHandshake ExpectedCatalog { get; }

        internal static bool TryCreate(
            NativeProgramBlackboardBindingV2 binding,
            GeneratedBurstCatalogV2 catalog,
            out GeneratedTreeRuntimeDefinitionV2 definition,
            out BurstContextResult failure)
        {
            definition = null;
            if (binding == null || catalog == null || !catalog.IsCreated)
            {
                failure = BurstContextResult.InvalidHandle;
                return false;
            }

            var program = binding.SemanticProgram;
            var handshake = catalog.Handshake;
            if (program.Header.ExecutionSemanticsVersion != handshake.ExecutionSemanticsVersion)
            {
                failure = BurstContextResult.TypeMismatch;
                return false;
            }

            // handshake.CompiledFormatVersion/NodeRegistry are the generated catalog's own
            // self-identity fields (layout-blob format and the frozen aibt.core.* authority plus
            // the catalog's selected generated shards, per GeneratedBurstDispatchPrebindingV2) --
            // not a mirror of program.Header's own, differently-scoped fields (which tree compiler
            // produced this program, and its full live node registry). The two pairs cover
            // different things and must never be compared directly; the per-node case lookup below
            // is the real compatibility gate for generated dispatch.
            for (var index = 0; index < program.Nodes.Count; index++)
            {
                var node = program.Nodes[index];
                if (NativeHotReloadInstance.ClassifyKind(node.NodeTypeId)
                    != NativeLifecycleNodeKindV1.GeneratedLeaf)
                    continue;

                if (!catalog.TryFindCase(node.NodeTypeId, node.NodeTypeVersion, out var caseIndex))
                {
                    failure = BurstContextResult.TypeMismatch;
                    return false;
                }

                var dispatchCase = catalog.Cases[(int)caseIndex];
                if (node.ConfigSize != dispatchCase.ConfigurationSize
                    || node.InstanceMemorySize != dispatchCase.MemorySize)
                {
                    failure = BurstContextResult.InvalidEncoding;
                    return false;
                }
            }

            definition = new GeneratedTreeRuntimeDefinitionV2(binding, in handshake);
            failure = BurstContextResult.Success;
            return true;
        }

        internal bool Matches(GeneratedBurstCatalogV2 catalog)
        {
            if (catalog == null || !catalog.IsCreated) return false;
            var expected = ExpectedCatalog;
            var actual = catalog.Handshake;
            return HandshakeEquals(in expected, in actual);
        }

        private static bool HandshakeEquals(in BurstCatalogHandshake left, in BurstCatalogHandshake right)
            => left.AbiVersion == right.AbiVersion
                && left.CompiledFormatVersion == right.CompiledFormatVersion
                && left.ExecutionSemanticsVersion == right.ExecutionSemanticsVersion
                && HashEquals(left.Catalog.Value, right.Catalog.Value)
                && HashEquals(left.NodeRegistry, right.NodeRegistry)
                && HashEquals(left.ConfigurationLayout, right.ConfigurationLayout)
                && HashEquals(left.MemoryLayout, right.MemoryLayout)
                && HashEquals(left.AccessLayout, right.AccessLayout);

        private static bool HashEquals(BurstHash256 left, BurstHash256 right)
            => left.Word0 == right.Word0 && left.Word1 == right.Word1
                && left.Word2 == right.Word2 && left.Word3 == right.Word3
                && left.Word4 == right.Word4 && left.Word5 == right.Word5
                && left.Word6 == right.Word6 && left.Word7 == right.Word7;

    }
}
