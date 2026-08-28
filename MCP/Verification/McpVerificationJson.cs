using System;
using System.Collections.Generic;
using AIBT.Authoring;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Verification
{
    /// <summary>
    /// JSON plumbing for the Verification tool group: a ProjectPolicySnapshot -&gt;
    /// TreeValidationPolicy mapping (the two types are otherwise unrelated -- see
    /// McpVerificationToolDispatcher's Validate for why this exists), and a restricted reader for
    /// behavior-case-v1.md's plain "update" step shape (the only step kind ReferencePreviewDriver
    /// can actually drive). Diagnostic-collection JSON is <see cref="AIBT.Mcp.McpDiagnosticJson"/>
    /// now (shared with the Authoring tool group, not owned here).
    /// </summary>
    internal static class McpVerificationJson
    {
        /// <summary>
        /// Maps a per-project policy snapshot (already read from .aibt/policy.json by P6-003/P6-005)
        /// to the type TreeValidator actually consumes. The two types are otherwise unrelated --
        /// ProjectPolicySnapshot is explicitly documented as reporting-only. String enum values are
        /// mapped 1:1 against Schemas~/policy.schema.json's own declared enums.
        /// </summary>
        internal static TreeValidationPolicy ToValidationPolicy(ProjectPolicySnapshot snapshot)
        {
            var warningsAsErrors = new List<DiagnosticCode>();
            foreach (var code in snapshot.WarningsAsErrors)
            {
                if (DiagnosticCode.TryParse(code, out var parsed))
                {
                    warningsAsErrors.Add(parsed);
                }
            }

            return new TreeValidationPolicy(
                snapshot.MaxTreeDepth,
                snapshot.MaxNodesPerTree,
                snapshot.AllowManagedNodes,
                snapshot.AllowMainThreadNodes,
                snapshot.RequireTreeDescription,
                snapshot.RequireNodeDescriptions,
                ToBlackboardNaming(snapshot.BlackboardNaming),
                snapshot.ForbiddenNodeTypes,
                snapshot.RequireDeterministicNodes,
                snapshot.AllowSideEffects,
                warningsAsErrors,
                snapshot.MaxEstimatedCost,
                snapshot.ForbidUnboundedRepeaters,
                snapshot.RequireEventDrivenServices);
        }

        internal static UnreachableNodePolicy ToUnreachableNodePolicy(string value)
        {
            switch (value)
            {
                case "error": return UnreachableNodePolicy.Error;
                case "warning": return UnreachableNodePolicy.Warning;
                case "allow": return UnreachableNodePolicy.Allow;
                default: throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Unknown unreachableNodes policy value: " + value);
            }
        }

        private static BlackboardNamingPolicy ToBlackboardNaming(string value)
        {
            switch (value)
            {
                case "snake_case": return BlackboardNamingPolicy.SnakeCase;
                case "camelCase": return BlackboardNamingPolicy.CamelCase;
                case "PascalCase": return BlackboardNamingPolicy.PascalCase;
                case "any": return BlackboardNamingPolicy.Any;
                default: throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Unknown blackboardNaming policy value: " + value);
            }
        }

        internal readonly struct SimulateStep
        {
            internal SimulateStep(ulong updateId, ulong snapshotRevision, long timeMicroseconds)
            {
                UpdateId = updateId;
                SnapshotRevision = snapshotRevision;
                TimeMicroseconds = timeMicroseconds;
            }

            internal ulong UpdateId { get; }
            internal ulong SnapshotRevision { get; }
            internal long TimeMicroseconds { get; }
        }

        /// <summary>
        /// Reads one behavior-case-v1.md step, restricted to the plain "update" operation with no
        /// events/completions/stepBudget -- the only shape ReferencePreviewDriver.BeginTick/RunTick
        /// can actually drive. The driver assigns updateId/snapshotRevision itself (sequentially,
        /// starting at 1) and RunTick has no step-budget parameter at all, so those cannot be
        /// silently accepted-and-ignored; the caller (McpVerificationToolDispatcher.Simulate)
        /// validates the supplied updateId/snapshotRevision against the driver's own sequential
        /// assignment and rejects a mismatch, rather than pretending to honor an arbitrary value.
        /// </summary>
        internal static SimulateStep ReadUpdateStep(JObject stepJson)
        {
            var operation = (string)stepJson["operation"];
            if (operation != "update")
            {
                throw new McpToolException(
                    McpVerificationDiagnostics.UnsupportedSimulateStep,
                    "Only 'update' steps are supported by this MCP surface (ReferencePreviewDriver has no resume/abort/event/completion injection API); got '" + operation + "'.");
            }

            if (stepJson["events"] != null || stepJson["completions"] != null)
            {
                throw new McpToolException(
                    McpVerificationDiagnostics.UnsupportedSimulateStep,
                    "This MCP surface cannot inject external events or completions (ReferencePreviewDriver exposes no such API); the step must omit 'events' and 'completions'.");
            }

            if (stepJson["stepBudget"] != null)
            {
                throw new McpToolException(
                    McpVerificationDiagnostics.UnsupportedSimulateStep,
                    "This MCP surface cannot bound a step budget (ReferencePreviewDriver.RunTick has no such parameter); the step must omit 'stepBudget'.");
            }

            var updateId = (ulong?)stepJson["updateId"]
                ?? throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Missing required property 'updateId'.");
            var snapshotRevision = (ulong?)stepJson["snapshotRevision"]
                ?? throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Missing required property 'snapshotRevision'.");
            var timeMicroseconds = (long?)stepJson["timeMicroseconds"] ?? 0L;

            return new SimulateStep(updateId, snapshotRevision, timeMicroseconds);
        }
    }
}
