using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    // A plain reporting snapshot of .aibt/policy.json (Schemas~/policy.schema.json). This is not
    // TreeValidator's TreeValidationPolicy -- Discovery reports policy facts for the project
    // manifest, it does not run validation and must not duplicate that type's behavior.
    public sealed class ProjectPolicySnapshot
    {
        private ProjectPolicySnapshot(
            int? maxTreeDepth,
            int? maxNodesPerTree,
            bool allowManagedNodes,
            bool allowMainThreadNodes,
            bool requireTreeDescription,
            bool requireNodeDescriptions,
            string blackboardNaming,
            bool requireDeterministicNodes,
            bool allowSideEffects,
            string unreachableNodes,
            bool supportsAgentScope,
            bool supportsSharedScope,
            IReadOnlyList<string> forbiddenNodeTypes,
            IReadOnlyList<string> warningsAsErrors,
            bool forbidUnboundedRepeaters,
            bool requireEventDrivenServices,
            double? maxEstimatedCost)
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
            UnreachableNodes = unreachableNodes;
            SupportsAgentScope = supportsAgentScope;
            SupportsSharedScope = supportsSharedScope;
            ForbiddenNodeTypes = forbiddenNodeTypes;
            WarningsAsErrors = warningsAsErrors;
            ForbidUnboundedRepeaters = forbidUnboundedRepeaters;
            RequireEventDrivenServices = requireEventDrivenServices;
            MaxEstimatedCost = maxEstimatedCost;
        }

        public int? MaxTreeDepth { get; }
        public int? MaxNodesPerTree { get; }
        public bool AllowManagedNodes { get; }
        public bool AllowMainThreadNodes { get; }
        public bool RequireTreeDescription { get; }
        public bool RequireNodeDescriptions { get; }
        public string BlackboardNaming { get; }
        public bool RequireDeterministicNodes { get; }
        public bool AllowSideEffects { get; }
        public string UnreachableNodes { get; }
        public bool SupportsAgentScope { get; }
        public bool SupportsSharedScope { get; }
        public IReadOnlyList<string> ForbiddenNodeTypes { get; }
        public IReadOnlyList<string> WarningsAsErrors { get; }
        public bool ForbidUnboundedRepeaters { get; }
        public bool RequireEventDrivenServices { get; }
        public double? MaxEstimatedCost { get; }

        public static bool TryReadFile(string filePath, out ProjectPolicySnapshot snapshot, out Diagnostic error)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                snapshot = null;
                error = new Diagnostic(
                    DiscoveryDiagnosticCodes.MalformedPolicyDocument,
                    DiagnosticSeverity.Error,
                    "Project policy document could not be read: " + ex.Message);
                return false;
            }

            return TryParse(json, out snapshot, out error);
        }

        public static bool TryParse(string json, out ProjectPolicySnapshot snapshot, out Diagnostic error)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json ?? throw new ArgumentNullException(nameof(json)));
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is ArgumentNullException)
            {
                snapshot = null;
                error = new Diagnostic(
                    DiscoveryDiagnosticCodes.MalformedPolicyDocument,
                    DiagnosticSeverity.Error,
                    "Project policy document is not valid JSON: " + ex.Message);
                return false;
            }

            if (!TryRequiredString(root, "format", out var format, out error) || format != "aibt.policy"
                || !TryRequiredBool(root, "allowManagedNodes", out var allowManagedNodes, out error)
                || !TryRequiredBool(root, "allowMainThreadNodes", out var allowMainThreadNodes, out error)
                || !TryRequiredBool(root, "requireTreeDescription", out var requireTreeDescription, out error)
                || !TryRequiredBool(root, "requireNodeDescriptions", out var requireNodeDescriptions, out error)
                || !TryRequiredString(root, "blackboardNaming", out var blackboardNaming, out error)
                || !TryRequiredBool(root, "requireDeterministicNodes", out var requireDeterministicNodes, out error)
                || !TryRequiredBool(root, "allowSideEffects", out var allowSideEffects, out error)
                || !TryRequiredString(root, "unreachableNodes", out var unreachableNodes, out error)
                || !TryRequiredBool(root, "supportsAgentScope", out var supportsAgentScope, out error)
                || !TryRequiredBool(root, "supportsSharedScope", out var supportsSharedScope, out error))
            {
                snapshot = null;
                error ??= MalformedError("Project policy document is missing a required field.");
                return false;
            }

            if (format != "aibt.policy")
            {
                snapshot = null;
                error = MalformedError("Project policy document 'format' must be 'aibt.policy'.");
                return false;
            }

            var performance = root["performance"] as JObject;
            if (performance == null
                || !TryRequiredBool(performance, "forbidUnboundedRepeaters", out var forbidUnboundedRepeaters, out error)
                || !TryRequiredBool(performance, "requireEventDrivenServices", out var requireEventDrivenServices, out error))
            {
                snapshot = null;
                error ??= MalformedError("Project policy document is missing 'performance' or its required fields.");
                return false;
            }

            snapshot = new ProjectPolicySnapshot(
                (int?)root["maxTreeDepth"],
                (int?)root["maxNodesPerTree"],
                allowManagedNodes,
                allowMainThreadNodes,
                requireTreeDescription,
                requireNodeDescriptions,
                blackboardNaming,
                requireDeterministicNodes,
                allowSideEffects,
                unreachableNodes,
                supportsAgentScope,
                supportsSharedScope,
                ReadStringArray(root, "forbiddenNodeTypes"),
                ReadStringArray(root, "warningsAsErrors"),
                forbidUnboundedRepeaters,
                requireEventDrivenServices,
                (double?)performance["maxEstimatedCost"]);
            error = null;
            return true;
        }

        private static bool TryRequiredString(JObject obj, string name, out string value, out Diagnostic error)
        {
            var token = obj[name];
            if (token == null || token.Type != JTokenType.String)
            {
                value = null;
                error = MalformedError("Project policy document is missing required string field '" + name + "'.");
                return false;
            }

            value = (string)token;
            error = null;
            return true;
        }

        private static bool TryRequiredBool(JObject obj, string name, out bool value, out Diagnostic error)
        {
            var token = obj[name];
            if (token == null || token.Type != JTokenType.Boolean)
            {
                value = false;
                error = MalformedError("Project policy document is missing required boolean field '" + name + "'.");
                return false;
            }

            value = (bool)token;
            error = null;
            return true;
        }

        private static IReadOnlyList<string> ReadStringArray(JObject obj, string name)
        {
            var token = obj[name] as JArray;
            if (token == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>(token.Count);
            for (var index = 0; index < token.Count; index++)
            {
                result.Add((string)token[index]);
            }

            return new ReadOnlyCollection<string>(result);
        }

        private static Diagnostic MalformedError(string message)
        {
            return new Diagnostic(DiscoveryDiagnosticCodes.MalformedPolicyDocument, DiagnosticSeverity.Error, message);
        }
    }
}
