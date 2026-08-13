using System;

namespace AIBT.Authoring
{
    public sealed class ReferenceCompilerOptions
    {
        private readonly Func<string, ulong> _blackboardKeyHash;
        private readonly Func<BlackboardTypeDescriptor, BlackboardTypeDescriptor> _blackboardLayout;

        public ReferenceCompilerOptions(
            string sourceId,
            ReferenceCompilationPolicy policy,
            CompiledCompilerVersion compilerVersion,
            bool stripDisplayMetadata = false)
            : this(sourceId, policy, compilerVersion, stripDisplayMetadata, StableHash.Fnv1A64, null, uint.MaxValue)
        {
        }

        internal ReferenceCompilerOptions(
            string sourceId,
            ReferenceCompilationPolicy policy,
            CompiledCompilerVersion compilerVersion,
            bool stripDisplayMetadata,
            Func<string, ulong> blackboardKeyHash,
            Func<BlackboardTypeDescriptor, BlackboardTypeDescriptor> blackboardLayout,
            uint tableElementLimit)
        {
            SourceId = sourceId;
            Policy = policy;
            CompilerVersion = compilerVersion;
            StripDisplayMetadata = stripDisplayMetadata;
            _blackboardKeyHash = blackboardKeyHash;
            _blackboardLayout = blackboardLayout;
            TableElementLimit = tableElementLimit;
        }

        public string SourceId { get; }

        public ReferenceCompilationPolicy Policy { get; }

        public CompiledCompilerVersion CompilerVersion { get; }

        public bool StripDisplayMetadata { get; }

        internal Func<string, ulong> BlackboardKeyHash => _blackboardKeyHash;

        internal BlackboardTypeDescriptor ResolveBlackboardLayout(BlackboardTypeDescriptor descriptor)
            => _blackboardLayout == null ? descriptor : _blackboardLayout(descriptor);

        internal uint TableElementLimit { get; }
    }

    public sealed class ReferenceCompilationResult
    {
        internal ReferenceCompilationResult(CompiledProgram program, DiagnosticCollection diagnostics)
        {
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            if (program != null && ContainsError(diagnostics))
            {
                throw new ArgumentException("A compiled program cannot be returned with error diagnostics.", nameof(program));
            }

            Program = program;
        }

        public CompiledProgram Program { get; }

        public DiagnosticCollection Diagnostics { get; }

        public bool Success => Program != null && !ContainsError(Diagnostics);

        private static bool ContainsError(DiagnosticCollection diagnostics)
        {
            for (var index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
