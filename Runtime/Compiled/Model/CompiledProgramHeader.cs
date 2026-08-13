using System;

namespace AIBT
{
    public readonly struct CompiledProgramHeader
    {
        public const uint ExpectedMagic = 0x54424941;

        public CompiledProgramHeader(
            uint compiledFormatVersion,
            uint executionSemanticsVersion,
            CompiledCompilerVersion compilerVersion,
            CompiledHash canonicalSemanticHash,
            CompiledHash nodeRegistryHash,
            CompiledHash canonicalPolicyHash,
            uint policyFormatVersion,
            CompiledHash compiledContentHash,
            uint rootNodeIndex,
            uint nodeCount,
            uint childIndexCount,
            uint blackboardSlotCount,
            uint debugMapCount,
            uint configBlobSize,
            uint instanceNodeMemorySize,
            uint requiredMaximumAlignment,
            uint capabilityFlags,
            bool deterministicModeCompatible)
        {
            if (compiledFormatVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(compiledFormatVersion));
            }

            if (executionSemanticsVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(executionSemanticsVersion));
            }

            if (!compilerVersion.IsValid)
            {
                throw new ArgumentException("A valid compiler version is required.", nameof(compilerVersion));
            }

            RequireHash(canonicalSemanticHash, nameof(canonicalSemanticHash));
            RequireHash(nodeRegistryHash, nameof(nodeRegistryHash));
            RequireHash(canonicalPolicyHash, nameof(canonicalPolicyHash));
            RequireHash(compiledContentHash, nameof(compiledContentHash));

            if (policyFormatVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(policyFormatVersion));
            }

            if (nodeCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeCount));
            }

            if (rootNodeIndex == CompiledIndex.Invalid || rootNodeIndex >= nodeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(rootNodeIndex));
            }

            if (!IsPowerOfTwo(requiredMaximumAlignment))
            {
                throw new ArgumentOutOfRangeException(nameof(requiredMaximumAlignment), "Alignment must be a positive power of two.");
            }

            Magic = ExpectedMagic;
            CompiledFormatVersion = compiledFormatVersion;
            ExecutionSemanticsVersion = executionSemanticsVersion;
            CompilerVersion = compilerVersion;
            CanonicalSemanticHash = canonicalSemanticHash;
            NodeRegistryHash = nodeRegistryHash;
            CanonicalPolicyHash = canonicalPolicyHash;
            PolicyFormatVersion = policyFormatVersion;
            CompiledContentHash = compiledContentHash;
            RootNodeIndex = rootNodeIndex;
            NodeCount = nodeCount;
            ChildIndexCount = childIndexCount;
            BlackboardSlotCount = blackboardSlotCount;
            DebugMapCount = debugMapCount;
            ConfigBlobSize = configBlobSize;
            InstanceNodeMemorySize = instanceNodeMemorySize;
            RequiredMaximumAlignment = requiredMaximumAlignment;
            CapabilityFlags = capabilityFlags;
            DeterministicModeCompatible = deterministicModeCompatible;
        }

        public uint Magic { get; }

        public uint CompiledFormatVersion { get; }

        public uint ExecutionSemanticsVersion { get; }

        public CompiledCompilerVersion CompilerVersion { get; }

        public CompiledHash CanonicalSemanticHash { get; }

        public CompiledHash NodeRegistryHash { get; }

        public CompiledHash CanonicalPolicyHash { get; }

        public uint PolicyFormatVersion { get; }

        public CompiledHash CompiledContentHash { get; }

        public uint RootNodeIndex { get; }

        public uint NodeCount { get; }

        public uint ChildIndexCount { get; }

        public uint BlackboardSlotCount { get; }

        public uint DebugMapCount { get; }

        public uint ConfigBlobSize { get; }

        public uint InstanceNodeMemorySize { get; }

        public uint RequiredMaximumAlignment { get; }

        public uint CapabilityFlags { get; }

        public bool DeterministicModeCompatible { get; }

        private static void RequireHash(CompiledHash hash, string parameterName)
        {
            if (!hash.IsValid)
            {
                throw new ArgumentException("A valid canonical SHA-256 hash is required.", parameterName);
            }
        }

        private static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    }
}
