using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using AIBT.Editor.Layout;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Verification
{
    /// <summary>
    /// Implements the 4 Verification tools ai-and-mcp.md's "Core MCP surface > Verification"
    /// section lists that this card owns: validate, compile, simulate, explain-diagnostic. Every
    /// tool wraps exactly one already-accepted production entry point (TreeValidator,
    /// ReferenceCompiler, ReferencePreviewDriver) -- no second validator/compiler/executor exists
    /// here. Called from McpToolDispatcher only after permission enforcement.
    /// </summary>
    internal static class McpVerificationToolDispatcher
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 0);

        // ---- validate ------------------------------------------------------------------------

        internal static JObject Validate(string projectRoot, JObject args)
        {
            var (loadedDocument, path) = LoadTreeOrThrow(projectRoot, args);
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var document = ApplyMigrations(loadedDocument, registry, out var migrationDiagnostics);

            var policyPath = System.IO.Path.Combine(ProjectRootParent(projectRoot), ".aibt", "policy.json");
            var policyApplied = ProjectPolicySnapshot.TryReadFile(policyPath, out var snapshot, out _);
            var options = policyApplied
                ? new ValidationOptions(
                    ToLogicalSourceId(projectRoot, path),
                    McpVerificationJson.ToUnreachableNodePolicy(snapshot.UnreachableNodes),
                    snapshot.SupportsAgentScope,
                    snapshot.SupportsSharedScope,
                    McpVerificationJson.ToValidationPolicy(snapshot))
                : new ValidationOptions(ToLogicalSourceId(projectRoot, path));

            var diagnostics = new DiagnosticCollection(TreeValidator.Validate(document, registry, options).Concat(migrationDiagnostics));

            return new JObject
            {
                ["valid"] = !HasError(diagnostics),
                ["policyApplied"] = policyApplied,
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
            };
        }

        // ---- compile -------------------------------------------------------------------------

        internal static JObject Compile(string projectRoot, JObject args)
        {
            var (loadedDocument, path) = LoadTreeOrThrow(projectRoot, args);
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var document = ApplyMigrations(loadedDocument, registry, out var migrationDiagnostics);

            // P7-018: mirrors Validate's already-established project-policy read exactly -- never
            // touches ReferenceCompilationPolicy.Phase1's own shared default (still false/false).
            var policyPath = System.IO.Path.Combine(ProjectRootParent(projectRoot), ".aibt", "policy.json");
            var policy = ProjectPolicySnapshot.TryReadFile(policyPath, out var snapshot, out _)
                ? new ReferenceCompilationPolicy(supportsAgentScope: snapshot.SupportsAgentScope, supportsSharedScope: snapshot.SupportsSharedScope)
                : ReferenceCompilationPolicy.Phase1;
            var options = new ReferenceCompilerOptions(ToLogicalSourceId(projectRoot, path), policy, CompilerVersion);

            var result = ReferenceCompiler.Compile(document, registry, options);
            var diagnostics = new DiagnosticCollection(result.Diagnostics.Concat(migrationDiagnostics));

            return new JObject
            {
                ["success"] = result.Success,
                ["contentHash"] = result.Success ? result.Program.Header.CompiledContentHash.HexadecimalValue : null,
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
            };
        }

        // ---- migration hook (ADR-P7-005 / P7-006) --------------------------------------------

        /// <summary>
        /// Applies every registered migration rule to <paramref name="document"/> in memory only
        /// (per ADR-P7-005 -- the on-disk file is never touched here) and returns the migrated
        /// document plus one AIBT2042 Info diagnostic per migrated node. <paramref name="rules"/>
        /// defaults to <see cref="NodeMigrationRegistry.Empty"/> -- no real production migration
        /// rules exist yet (no node type has ever been version-bumped in this project); the
        /// parameter exists so a test can inject a populated registry and prove this exact hook is
        /// wired correctly, not only the standalone <see cref="DocumentMigrator"/> engine.
        /// </summary>
        internal static TreeDocument ApplyMigrations(TreeDocument document, NodeRegistry registry, out IReadOnlyList<Diagnostic> diagnostics, NodeMigrationRegistry rules = null)
        {
            var migrated = DocumentMigrator.TryMigrate(document, registry, rules ?? NodeMigrationRegistry.Empty, out var outcomes);
            if (outcomes.Count == 0)
            {
                diagnostics = Array.Empty<Diagnostic>();
                return migrated;
            }

            var list = new List<Diagnostic>(outcomes.Count);
            foreach (var outcome in outcomes)
            {
                var changeText = string.Join("; ", outcome.Changes.Select(c => c.Description));
                list.Add(TreeValidationDiagnosticCatalog.Create(
                    TreeValidationDiagnosticCodes.MigrationApplied,
                    "Node '" + outcome.NodeId.Value + "' (" + outcome.TypeId + ") migrated in memory from version "
                        + outcome.FromVersion + " to " + outcome.ToVersion + ": " + changeText,
                    new DiagnosticLocation(jsonPointer: "/nodes/" + outcome.NodeId.Value, treeId: outcome.TreeId, nodeId: outcome.NodeId),
                    severity: DiagnosticSeverity.Info));
            }

            diagnostics = list;
            return migrated;
        }

        // ---- simulate ------------------------------------------------------------------------

        internal static JObject Simulate(string projectRoot, JObject args)
        {
            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            var stepsJson = (JArray)RequireToken(args, "steps");

            if (!ReferencePreviewDriver.TryCreate(document, ToLogicalSourceId(projectRoot, path), out var driver, out var compileDiagnostics))
            {
                return new JObject
                {
                    ["accepted"] = false,
                    ["backend"] = "ReferencePreviewDriver (Phase 1 reference executor)",
                    ["nodeSet"] = "Phase 1 fixture/built-in registry (ReferencePreviewFixtureEnvironment) -- built-in composites/decorators plus aibt.test.success/failure/running only",
                    ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(compileDiagnostics),
                    ["steps"] = new JArray(),
                };
            }

            var stepResults = new JArray();
            var expectedUpdateId = 0uL;
            foreach (var stepToken in stepsJson)
            {
                expectedUpdateId++;
                var step = McpVerificationJson.ReadUpdateStep((JObject)stepToken);
                if (step.UpdateId != expectedUpdateId || step.SnapshotRevision != expectedUpdateId)
                {
                    throw new McpToolException(
                        McpVerificationDiagnostics.UnsupportedSimulateStep,
                        "ReferencePreviewDriver assigns updateId/snapshotRevision itself, sequentially starting at 1 -- step " + expectedUpdateId +
                        " must have updateId=" + expectedUpdateId + " and snapshotRevision=" + expectedUpdateId +
                        " (got updateId=" + step.UpdateId + ", snapshotRevision=" + step.SnapshotRevision + ").");
                }

                var envelope = driver.RunTick(timeMicroseconds: step.TimeMicroseconds);
                stepResults.Add(new JObject
                {
                    ["updateId"] = step.UpdateId,
                    ["progress"] = envelope.Progress.ToString(),
                    ["rootResult"] = envelope.RootResult?.ToString(),
                    ["executedSteps"] = envelope.Steps,
                    ["traceEvents"] = WriteTraceEvents(envelope.TraceEvents),
                });
            }

            return new JObject
            {
                ["accepted"] = true,
                ["backend"] = "ReferencePreviewDriver (Phase 1 reference executor)",
                ["nodeSet"] = "Phase 1 fixture/built-in registry (ReferencePreviewFixtureEnvironment) -- built-in composites/decorators plus aibt.test.success/failure/running only",
                ["terminalResult"] = driver.TerminalResult?.ToString(),
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(compileDiagnostics),
                ["steps"] = stepResults,
            };
        }

        private static JArray WriteTraceEvents(IReadOnlyList<ReferencePreviewTraceEvent> events)
        {
            var array = new JArray();
            foreach (var traceEvent in events)
            {
                array.Add(new JObject
                {
                    ["sequence"] = traceEvent.Sequence,
                    ["kind"] = traceEvent.Kind.ToString(),
                    ["node"] = traceEvent.Node?.Value,
                    ["status"] = traceEvent.Status?.ToString(),
                    ["sourceNode"] = traceEvent.SourceNode?.Value,
                });
            }

            return array;
        }

        // ---- explain-diagnostic ---------------------------------------------------------------

        internal static JObject ExplainDiagnostic(string projectRoot, JObject args)
        {
            var diagnosticJson = RequireObject(args, "diagnostic");
            var codeText = (string)diagnosticJson["code"]
                ?? throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "The supplied diagnostic is missing 'code'.");

            if (!DiagnosticCode.TryParse(codeText, out var code))
            {
                throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "'" + codeText + "' is not a well-formed AIBT diagnostic code.");
            }

            var response = new JObject { ["code"] = codeText };

            // AIBT.Mcp has an InternalsVisibleTo grant from AIBT.Authoring, AIBT.Runtime, and
            // AIBT.Editor (widened 2026-08-28), reaching every internal-or-public DiagnosticCatalog
            // holder in those three assemblies: TreeValidationDiagnosticCatalog/
            // BlackboardDiagnosticCatalog (already public before the grant), plus
            // TreeJsonDiagnostics/NodeRegistryDiagnostics/LayoutJsonDiagnostics (2026-08-28), and
            // ReferenceCompilerDiagnostics/ReferenceExecutionDiagnostics/CommandAsyncDiagnostics/
            // BlackboardStorageDiagnostics (P6-021 -- each of these four also had its own Catalog
            // field `private`, a stricter per-type restriction InternalsVisibleTo alone could not
            // bypass; widened to `internal` to match the earlier three). Nothing outside these nine
            // catalogs is reachable, and nothing here changed any catalog's own code/severity/field
            // contract -- only reachability.
            if (TreeValidationDiagnosticCatalog.Catalog.TryGet(code, out var treeDescriptor))
            {
                WriteDescriptor(response, treeDescriptor);
            }
            else if (BlackboardDiagnosticCatalog.Catalog.TryGet(code, out var blackboardDescriptor))
            {
                WriteDescriptor(response, blackboardDescriptor);
            }
            else if (TreeJsonDiagnostics.Catalog.TryGet(code, out var treeJsonDescriptor))
            {
                WriteDescriptor(response, treeJsonDescriptor);
            }
            else if (NodeRegistryDiagnostics.Catalog.TryGet(code, out var registryDescriptor))
            {
                WriteDescriptor(response, registryDescriptor);
            }
            else if (LayoutJsonDiagnostics.Catalog.TryGet(code, out var layoutJsonDescriptor))
            {
                WriteDescriptor(response, layoutJsonDescriptor);
            }
            else if (ReferenceCompilerDiagnostics.Catalog.TryGet(code, out var compilerDescriptor))
            {
                WriteDescriptor(response, compilerDescriptor);
            }
            else if (ReferenceExecutionDiagnostics.Catalog.TryGet(code, out var executionDescriptor))
            {
                WriteDescriptor(response, executionDescriptor);
            }
            else if (CommandAsyncDiagnostics.Catalog.TryGet(code, out var commandAsyncDescriptor))
            {
                WriteDescriptor(response, commandAsyncDescriptor);
            }
            else if (BlackboardStorageDiagnostics.Catalog.TryGet(code, out var blackboardStorageDescriptor))
            {
                WriteDescriptor(response, blackboardStorageDescriptor);
            }
            else
            {
                response["catalogReachable"] = false;
            }

            // Never fabricated -- exactly what the caller supplied, or absent.
            if (diagnosticJson["suggestedOperation"] != null)
            {
                response["suggestedOperation"] = diagnosticJson["suggestedOperation"].DeepClone();
            }

            return response;
        }

        private static void WriteDescriptor(JObject response, DiagnosticDescriptor descriptor)
        {
            response["catalogReachable"] = true;
            response["subsystem"] = descriptor.Subsystem.ToString();
            response["defaultSeverity"] = descriptor.DefaultSeverity.ToString();
            response["requiredFields"] = descriptor.RequiredFields.ToString();
            response["optionalFields"] = descriptor.OptionalFields.ToString();
        }

        // ---- shared plumbing ------------------------------------------------------------------

        private static bool HasError(DiagnosticCollection diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static (TreeDocument Document, string Path) LoadTreeOrThrow(string projectRoot, JObject args)
        {
            var treeId = new TreeId(RequireString(args, "treeId"));
            var scan = AibtTreeDiscovery.Scan(projectRoot);
            if (!scan.TryFindPath(treeId, out var path))
            {
                throw new McpToolException(McpVerificationDiagnostics.TreeNotFound, "No tree with id '" + treeId.Value + "' was found under the project.");
            }

            var loaded = TreeDocumentPersistence.Load(path);
            if (!loaded.Success)
            {
                throw new McpToolException(McpVerificationDiagnostics.TreeNotFound, "Tree '" + treeId.Value + "' could not be parsed: " + string.Join("; ", loaded.Diagnostics.Select(d => d.Message)));
            }

            return (loaded.Document, path);
        }

        private static string ProjectRootParent(string assetsPath)
        {
            return System.IO.Directory.GetParent(assetsPath)?.FullName ?? assetsPath;
        }

        /// <summary>
        /// ReferenceCompilerOptions.SourceId / ValidationOptions.documentId must be a relative,
        /// forward-slash, ".."-free logical path (AIBT3010) -- mirrors
        /// McpAuthoringToolDispatcher.ToLogicalSourceId (P6-006), duplicated here rather than
        /// referenced across the card boundary.
        /// </summary>
        private static string ToLogicalSourceId(string projectRoot, string absolutePath)
        {
            return System.IO.Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        }

        private static string RequireString(JObject json, string property)
        {
            var value = json[property]?.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Missing required string property '" + property + "'.");
            }

            return value;
        }

        private static JObject RequireObject(JObject json, string property)
        {
            if (!(json[property] is JObject value))
            {
                throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Missing required object property '" + property + "'.");
            }

            return value;
        }

        private static JToken RequireToken(JObject json, string property)
        {
            var value = json[property];
            if (value == null)
            {
                throw new McpToolException(McpVerificationDiagnostics.MalformedArguments, "Missing required property '" + property + "'.");
            }

            return value;
        }
    }
}
