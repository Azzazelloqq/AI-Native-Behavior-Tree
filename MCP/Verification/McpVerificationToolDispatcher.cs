using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using AIBT.Editor.Layout;
using AIBT.Editor.Registry;
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
            var registry = ProjectNodeCatalogCache.Current.Registry;
            var document = ApplyMigrations(loadedDocument, registry, out var migrationDiagnostics);

            // P11-015 (D1, ADR-P9-001 amendment 2026-11-24): with a readable policy file the file decides;
            // without one (or with one that cannot be read, reported as a warning) the Editor's options.
            var snapshot = ReadProjectPolicy(projectRoot, out var policyWarning);
            var policyApplied = snapshot != null;
            var sourceId = ToLogicalSourceId(projectRoot, path);
            var options = policyApplied
                ? new ValidationOptions(
                    sourceId,
                    McpVerificationJson.ToUnreachableNodePolicy(snapshot.UnreachableNodes),
                    snapshot.SupportsAgentScope,
                    snapshot.SupportsSharedScope,
                    McpVerificationJson.ToValidationPolicy(snapshot))
                : EditorDefaultPolicy(document, McpSubtrees.ResolverFor(document, projectRoot), sourceId).CreateValidationOptions(sourceId);
            var policyDiagnostics = policyWarning == null ? Array.Empty<Diagnostic>() : new[] { policyWarning };

            var registeredTypes = ProjectNodeCatalogCache.Current.RegisteredTypes;
            var referenced = McpSubtrees.ReferencedTreeIds(document);
            if (referenced.Count == 0)
            {
                var diagnostics = new DiagnosticCollection(TreeValidator.Validate(document, registry, options, registeredTypes)
                    .Concat(ObserverDiagnostics(document, registry, projectRoot, path)).Concat(migrationDiagnostics).Concat(policyDiagnostics));

                return new JObject
                {
                    ["valid"] = !HasError(diagnostics),
                    ["policyApplied"] = policyApplied,
                    ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
                };
            }

            // ADR-P11-001: a tree with subtree references is validated as the compiler sees it. The
            // expansion validates the tree and each subtree on its own (AIBT2047-2050) and checks the
            // references (AIBT3020-3028); a successful expansion is then validated as one tree.
            var expansion = SubtreeExpander.Expand(document, McpSubtrees.Resolver(projectRoot), registry, options, registeredTypes);
            var expandedDiagnostics = new DiagnosticCollection((expansion.Success
                ? TreeValidator.Validate(expansion.Document, registry, options, registeredTypes).Concat(ObserverDiagnostics(expansion.Document, registry, projectRoot, path))
                : expansion.Diagnostics).Concat(migrationDiagnostics).Concat(policyDiagnostics));
            return new JObject
            {
                ["valid"] = !HasError(expandedDiagnostics),
                ["policyApplied"] = policyApplied,
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(expandedDiagnostics),
                ["subtrees"] = new JObject { ["expanded"] = expansion.Success, ["referencedTreeIds"] = new JArray(referenced) },
            };
        }

        // P11-009 (review F10, native half) and P11-014 (item 1): what the host does with each observer
        // block, as in the graph: AIBT2051 for a tree a BehaviorTreeAgent runs (it refuses the tree when it
        // loads it), the warning AIBT2054 for a tree a native host runs.
        private static IEnumerable<Diagnostic> ObserverDiagnostics(TreeDocument document, NodeRegistry registry, string projectRoot, string path)
            => ProjectNodeCatalog.ObserverDiagnostics(document, registry, ToLogicalSourceId(projectRoot, path));

        // ---- compile -------------------------------------------------------------------------

        internal static JObject Compile(string projectRoot, JObject args)
        {
            var (loadedDocument, path) = LoadTreeOrThrow(projectRoot, args);
            var registry = ProjectNodeCatalogCache.Current.Registry;
            var document = ApplyMigrations(loadedDocument, registry, out var migrationDiagnostics);

            // P7-018: mirrors Validate's project-policy read exactly. P11-015 (D1, ADR-P9-001 amendment
            // 2026-11-24): without a readable policy file, the Editor's options, not Phase1.
            var snapshot = ReadProjectPolicy(projectRoot, out var policyWarning);
            var sourceId = ToLogicalSourceId(projectRoot, path);

            // ADR-P11-001: references resolve through the project's trees; a tree without subtree
            // features compiles through the unchanged overload.
            var resolver = McpSubtrees.ResolverFor(document, projectRoot);
            var policy = snapshot != null
                ? McpVerificationJson.ToCompilationPolicy(snapshot)
                : EditorDefaultPolicy(document, resolver, sourceId);
            var options = new ReferenceCompilerOptions(sourceId, policy, CompilerVersion);
            var result = resolver == null ? ReferenceCompiler.Compile(document, registry, options) : ReferenceCompiler.Compile(document, registry, options, resolver);
            // P11-014 (item 1): a tree a BehaviorTreeAgent would refuse (AIBT2051) does not compile here either.
            var observed = ProjectNodeCatalogCache.Current.ExpandForObserverRule(document, resolver, options.SourceId);
            var observerDiagnostics = observed == null ? new List<Diagnostic>() : ProjectNodeCatalog.ObserverDiagnostics(observed, registry, options.SourceId);
            var success = result.Success && observerDiagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
            var diagnostics = new DiagnosticCollection(result.Diagnostics.Concat(observerDiagnostics).Concat(migrationDiagnostics)
                .Concat(policyWarning == null ? Array.Empty<Diagnostic>() : new[] { policyWarning }));

            return new JObject
            {
                ["success"] = success,
                ["contentHash"] = success ? result.Program.Header.CompiledContentHash.HexadecimalValue : null,
                ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
            };
        }

        // ---- migration hook (ADR-P7-005 / P7-006) --------------------------------------------

        /// <summary>
        /// Applies every registered migration rule to <paramref name="document"/> in memory only
        /// (per ADR-P7-005 -- the on-disk file is never touched here) and returns the migrated
        /// document plus one AIBT2052 Info diagnostic per migrated node. <paramref name="rules"/>
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

        // ---- project policy (P11-015, D1) ------------------------------------------------------

        /// <summary>
        /// Reads <c>&lt;project&gt;/.aibt/policy.json</c>. Returns null when there is no file, and null with
        /// <paramref name="warning"/> set (AIBT9008, a warning naming the problem, such as the missing field)
        /// when the file exists and cannot be read: the caller then uses the Editor's options
        /// (ADR-P9-001 amendment 2026-11-24). Used by <c>validate</c>, <c>compile</c> and
        /// <c>get_project_manifest</c>.
        /// </summary>
        internal static ProjectPolicySnapshot ReadProjectPolicy(string projectRoot, out Diagnostic warning)
        {
            warning = null;
            var policyPath = System.IO.Path.Combine(ProjectRootParent(projectRoot), ".aibt", "policy.json");
            if (!System.IO.File.Exists(policyPath)) return null;
            if (ProjectPolicySnapshot.TryReadFile(policyPath, out var snapshot, out var error)) return snapshot;
            warning = new Diagnostic(DiscoveryDiagnosticCodes.MalformedPolicyDocument, DiagnosticSeverity.Warning,
                "The project policy file .aibt/policy.json was not applied: " + (error?.Message ?? "it could not be read.")
                + " The Editor's defaults were used instead: the managed policy for a tree with managed nodes, otherwise the "
                + "native default with Agent and Shared scope allowed.",
                new DiagnosticLocation(".aibt/policy.json"));
            return null;
        }

        /// <summary>
        /// The Editor's policy for <paramref name="document"/> without a policy file
        /// (<see cref="ProjectNodeCatalog.DefaultPolicy"/>), decided on the expanded tree when the tree
        /// references subtrees and they resolve.
        /// </summary>
        private static ReferenceCompilationPolicy EditorDefaultPolicy(TreeDocument document, ISubtreeResolver resolver, string sourceId)
        {
            var catalog = ProjectNodeCatalogCache.Current;
            var expanded = catalog.ExpandForObserverRule(document, resolver, sourceId) ?? document;
            return ProjectNodeCatalog.DefaultPolicy(expanded, catalog.Registry);
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
