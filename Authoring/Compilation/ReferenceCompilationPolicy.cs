using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace AIBT.Authoring
{
    public sealed class ReferenceCompilationPolicy
    {
        private readonly ReadOnlyCollection<string> _forbiddenNodeTypes;
        private readonly ReadOnlyCollection<DiagnosticCode> _warningsAsErrors;

        public ReferenceCompilationPolicy(
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
            UnreachableNodePolicy unreachableNodes = UnreachableNodePolicy.Error,
            bool supportsAgentScope = false,
            bool supportsSharedScope = false,
            IEnumerable<DiagnosticCode> warningsAsErrors = null,
            double? maxEstimatedCost = null,
            bool forbidUnboundedRepeaters = false,
            bool requireEventDrivenServices = false)
        {
            if (!Enum.IsDefined(typeof(BlackboardNamingPolicy), blackboardNaming))
                throw new ArgumentOutOfRangeException(nameof(blackboardNaming));
            if (!Enum.IsDefined(typeof(UnreachableNodePolicy), unreachableNodes))
                throw new ArgumentOutOfRangeException(nameof(unreachableNodes));
            if (maxTreeDepth.HasValue && maxTreeDepth.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(maxTreeDepth));
            if (maxNodesPerTree.HasValue && maxNodesPerTree.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(maxNodesPerTree));
            if (maxEstimatedCost.HasValue && (maxEstimatedCost.Value < 0d
                || double.IsNaN(maxEstimatedCost.Value) || double.IsInfinity(maxEstimatedCost.Value)))
                throw new ArgumentOutOfRangeException(nameof(maxEstimatedCost));

            MaxTreeDepth = maxTreeDepth;
            MaxNodesPerTree = maxNodesPerTree;
            AllowManagedNodes = allowManagedNodes;
            AllowMainThreadNodes = allowMainThreadNodes;
            RequireTreeDescription = requireTreeDescription;
            RequireNodeDescriptions = requireNodeDescriptions;
            BlackboardNaming = blackboardNaming;
            RequireDeterministicNodes = requireDeterministicNodes;
            AllowSideEffects = allowSideEffects;
            UnreachableNodes = unreachableNodes;
            SupportsAgentScope = supportsAgentScope;
            SupportsSharedScope = supportsSharedScope;
            MaxEstimatedCost = maxEstimatedCost;
            ForbidUnboundedRepeaters = forbidUnboundedRepeaters;
            RequireEventDrivenServices = requireEventDrivenServices;

            var forbidden = new List<string>(forbiddenNodeTypes ?? Array.Empty<string>());
            forbidden.Sort(StringComparer.Ordinal);
            RequireUniqueNonempty(forbidden, nameof(forbiddenNodeTypes));
            _forbiddenNodeTypes = forbidden.AsReadOnly();

            var warnings = new List<DiagnosticCode>(warningsAsErrors ?? Array.Empty<DiagnosticCode>());
            warnings.Sort();
            for (var index = 0; index < warnings.Count; index++)
            {
                if (!warnings[index].IsValid || index > 0 && warnings[index] == warnings[index - 1])
                    throw new ArgumentException("Warning codes must be valid and unique.", nameof(warningsAsErrors));
            }
            _warningsAsErrors = warnings.AsReadOnly();
        }

        public static ReferenceCompilationPolicy Phase1 { get; } = new ReferenceCompilationPolicy();

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
        public UnreachableNodePolicy UnreachableNodes { get; }
        public bool SupportsAgentScope { get; }
        public bool SupportsSharedScope { get; }
        public IReadOnlyList<DiagnosticCode> WarningsAsErrors => _warningsAsErrors;
        public double? MaxEstimatedCost { get; }
        public bool ForbidUnboundedRepeaters { get; }
        public bool RequireEventDrivenServices { get; }

        public byte[] ToCanonicalUtf8() => ReferenceCompilationPolicyCodec.Serialize(this);

        public CompiledHash ComputeHash() => new CompiledHash(StableHash.Sha256Hex(ToCanonicalUtf8()));

        internal ValidationOptions CreateValidationOptions(string documentId)
        {
            return new ValidationOptions(
                documentId,
                UnreachableNodes,
                SupportsAgentScope,
                SupportsSharedScope,
                new TreeValidationPolicy(
                    MaxTreeDepth,
                    MaxNodesPerTree,
                    AllowManagedNodes,
                    AllowMainThreadNodes,
                    RequireTreeDescription,
                    RequireNodeDescriptions,
                    BlackboardNaming,
                    _forbiddenNodeTypes,
                    RequireDeterministicNodes,
                    AllowSideEffects,
                    _warningsAsErrors,
                    MaxEstimatedCost,
                    ForbidUnboundedRepeaters,
                    RequireEventDrivenServices));
        }

        private static void RequireUniqueNonempty(IReadOnlyList<string> values, string parameterName)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(values[index])
                    || index > 0 && values[index] == values[index - 1])
                    throw new ArgumentException("Policy sets must contain unique non-empty values.", parameterName);
            }
        }
    }

    public static class ReferenceCompilationPolicyCodec
    {
        public const string Format = "aibt.policy";
        public const uint FormatVersion = 1;

        public static byte[] Serialize(ReferenceCompilationPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            var builder = new StringBuilder(1024);
            builder.Append("{\n  \"format\": \"aibt.policy\",\n  \"formatVersion\": 1,\n");
            OptionalInteger(builder, "maxTreeDepth", policy.MaxTreeDepth);
            OptionalInteger(builder, "maxNodesPerTree", policy.MaxNodesPerTree);
            Boolean(builder, "allowManagedNodes", policy.AllowManagedNodes);
            Boolean(builder, "allowMainThreadNodes", policy.AllowMainThreadNodes);
            Boolean(builder, "requireTreeDescription", policy.RequireTreeDescription);
            Boolean(builder, "requireNodeDescriptions", policy.RequireNodeDescriptions);
            String(builder, "blackboardNaming", Naming(policy.BlackboardNaming));
            Boolean(builder, "requireDeterministicNodes", policy.RequireDeterministicNodes);
            Boolean(builder, "allowSideEffects", policy.AllowSideEffects);
            String(builder, "unreachableNodes", Unreachable(policy.UnreachableNodes));
            Boolean(builder, "supportsAgentScope", policy.SupportsAgentScope);
            Boolean(builder, "supportsSharedScope", policy.SupportsSharedScope);
            StringArray(builder, "forbiddenNodeTypes", policy.ForbiddenNodeTypes);
            DiagnosticArray(builder, "warningsAsErrors", policy.WarningsAsErrors);
            builder.Append("  \"performance\": {\n");
            if (policy.MaxEstimatedCost.HasValue)
            {
                builder.Append("    \"maxEstimatedCost\": ");
                builder.Append(CanonicalJsonNumber.Format(policy.MaxEstimatedCost.Value));
                builder.Append(",\n");
            }
            builder.Append("    \"forbidUnboundedRepeaters\": ");
            builder.Append(policy.ForbidUnboundedRepeaters ? "true" : "false");
            builder.Append(",\n    \"requireEventDrivenServices\": ");
            builder.Append(policy.RequireEventDrivenServices ? "true" : "false");
            builder.Append("\n  }\n}\n");
            return new UTF8Encoding(false, true).GetBytes(builder.ToString());
        }

        public static bool IsExactCanonicalEncoding(ReferenceCompilationPolicy policy, byte[] utf8)
        {
            if (policy == null || utf8 == null) return false;
            var expected = Serialize(policy);
            if (expected.Length != utf8.Length) return false;
            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != utf8[index]) return false;
            }
            return true;
        }

        private static void OptionalInteger(StringBuilder builder, string name, int? value)
        {
            if (!value.HasValue) return;
            builder.Append("  \"").Append(name).Append("\": ").Append(value.Value).Append(",\n");
        }

        private static void Boolean(StringBuilder builder, string name, bool value)
            => builder.Append("  \"").Append(name).Append("\": ")
                .Append(value ? "true" : "false").Append(",\n");

        private static void String(StringBuilder builder, string name, string value)
        {
            builder.Append("  \"").Append(name).Append("\": ");
            CanonicalJsonText.WriteString(builder, value);
            builder.Append(",\n");
        }

        private static void StringArray(StringBuilder builder, string name, IReadOnlyList<string> values)
        {
            builder.Append("  \"").Append(name).Append("\": [");
            for (var index = 0; index < values.Count; index++)
            {
                if (index != 0) builder.Append(", ");
                CanonicalJsonText.WriteString(builder, values[index]);
            }
            builder.Append("],\n");
        }

        private static void DiagnosticArray(
            StringBuilder builder,
            string name,
            IReadOnlyList<DiagnosticCode> values)
        {
            builder.Append("  \"").Append(name).Append("\": [");
            for (var index = 0; index < values.Count; index++)
            {
                if (index != 0) builder.Append(", ");
                CanonicalJsonText.WriteString(builder, values[index].Value);
            }
            builder.Append("],\n");
        }

        private static string Naming(BlackboardNamingPolicy value)
        {
            switch (value)
            {
                case BlackboardNamingPolicy.SnakeCase: return "snake_case";
                case BlackboardNamingPolicy.CamelCase: return "camelCase";
                case BlackboardNamingPolicy.PascalCase: return "PascalCase";
                case BlackboardNamingPolicy.Any: return "any";
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static string Unreachable(UnreachableNodePolicy value)
        {
            switch (value)
            {
                case UnreachableNodePolicy.Error: return "error";
                case UnreachableNodePolicy.Warning: return "warning";
                case UnreachableNodePolicy.Allow: return "allow";
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }
}
