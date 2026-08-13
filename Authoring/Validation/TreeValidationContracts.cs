using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public enum UnreachableNodePolicy : byte
    {
        Error,
        Warning,
        Allow,
    }

    public enum BlackboardNamingPolicy : byte
    {
        Any,
        SnakeCase,
        CamelCase,
        PascalCase,
    }

    public sealed class TreeValidationPolicy
    {
        private readonly ReadOnlyCollection<string> _forbiddenNodeTypes;
        private readonly ReadOnlyCollection<DiagnosticCode> _warningsAsErrors;

        public TreeValidationPolicy(
            int? maxTreeDepth = null,
            int? maxNodesPerTree = null,
            bool allowManagedNodes = false,
            bool allowMainThreadNodes = false,
            bool requireTreeDescription = false,
            bool requireNodeDescriptions = false,
            BlackboardNamingPolicy blackboardNaming = BlackboardNamingPolicy.Any,
            IEnumerable<string> forbiddenNodeTypes = null,
            bool requireDeterministicNodes = true,
            bool allowSideEffects = true,
            IEnumerable<DiagnosticCode> warningsAsErrors = null,
            double? maxEstimatedCost = null,
            bool forbidUnboundedRepeaters = false,
            bool requireEventDrivenServices = false)
        {
            MaxTreeDepth = maxTreeDepth;
            MaxNodesPerTree = maxNodesPerTree;
            AllowManagedNodes = allowManagedNodes;
            AllowMainThreadNodes = allowMainThreadNodes;
            RequireTreeDescription = requireTreeDescription;
            RequireNodeDescriptions = requireNodeDescriptions;
            BlackboardNaming = blackboardNaming;
            RequireDeterministicNodes = requireDeterministicNodes;
            AllowSideEffects = allowSideEffects;
            MaxEstimatedCost = maxEstimatedCost;
            ForbidUnboundedRepeaters = forbidUnboundedRepeaters;
            RequireEventDrivenServices = requireEventDrivenServices;

            var values = new List<string>(forbiddenNodeTypes ?? Array.Empty<string>());
            values.Sort(StringComparer.Ordinal);
            _forbiddenNodeTypes = values.AsReadOnly();

            var warningCodes = new List<DiagnosticCode>(warningsAsErrors ?? Array.Empty<DiagnosticCode>());
            warningCodes.Sort();
            _warningsAsErrors = warningCodes.AsReadOnly();
        }

        public int? MaxTreeDepth { get; }

        public int? MaxNodesPerTree { get; }

        public bool AllowManagedNodes { get; }

        public bool AllowMainThreadNodes { get; }

        public bool RequireTreeDescription { get; }

        public bool RequireNodeDescriptions { get; }

        public BlackboardNamingPolicy BlackboardNaming { get; }

        public IReadOnlyList<string> ForbiddenNodeTypes => _forbiddenNodeTypes;

        public bool RequireDeterministicNodes { get; }

        public bool AllowSideEffects { get; }

        public IReadOnlyList<DiagnosticCode> WarningsAsErrors => _warningsAsErrors;

        public double? MaxEstimatedCost { get; }

        public bool ForbidUnboundedRepeaters { get; }

        public bool RequireEventDrivenServices { get; }
    }

    public sealed class ValidationOptions
    {
        public ValidationOptions(
            string documentId = null,
            UnreachableNodePolicy unreachableNodes = UnreachableNodePolicy.Error,
            bool supportsAgentScope = false,
            bool supportsSharedScope = false,
            TreeValidationPolicy policy = null)
        {
            if (documentId != null && documentId.Length == 0)
            {
                throw new ArgumentException("Document IDs cannot be empty when present.", nameof(documentId));
            }

            if (!Enum.IsDefined(typeof(UnreachableNodePolicy), unreachableNodes))
            {
                throw new ArgumentOutOfRangeException(nameof(unreachableNodes));
            }

            DocumentId = documentId;
            UnreachableNodes = unreachableNodes;
            SupportsAgentScope = supportsAgentScope;
            SupportsSharedScope = supportsSharedScope;
            Policy = policy ?? new TreeValidationPolicy();
        }

        public string DocumentId { get; }

        public UnreachableNodePolicy UnreachableNodes { get; }

        public bool SupportsAgentScope { get; }

        public bool SupportsSharedScope { get; }

        public TreeValidationPolicy Policy { get; }

        public static ValidationOptions Phase1 { get; } = new ValidationOptions();
    }
}
