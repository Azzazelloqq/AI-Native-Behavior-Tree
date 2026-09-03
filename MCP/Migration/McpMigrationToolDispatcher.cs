using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Authoring.Migration;
using AIBT.Mcp;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.Migration
{
    /// <summary>
    /// P7-006 (ADR-P7-005): <c>aibt_migrate_document</c>. `validate`/`compile`
    /// (<see cref="McpVerificationToolDispatcher"/>) already apply every registered migration rule
    /// to a document in memory, transparently, on every call -- this tool is the separate, explicit
    /// action that persists that same migration to disk, mirroring
    /// <see cref="McpAuthoringToolDispatcher"/>'s own accept-then-explicitly-persist shape
    /// (dry-run by default behavior, write only when asked).
    /// </summary>
    internal static class McpMigrationToolDispatcher
    {
        /// <summary>
        /// <paramref name="rules"/> defaults to <see cref="NodeMigrationRegistry.Empty"/> (no real
        /// production migration rules exist yet) and <paramref name="registry"/> defaults to the
        /// real production node registry. Both parameters exist so a test can inject a populated
        /// node type + rule and prove this exact dispatcher entry point persists a real migration
        /// correctly, not only the standalone <see cref="DocumentMigrator"/> engine.
        /// </summary>
        internal static JObject MigrateDocument(string projectRoot, JObject args, NodeMigrationRegistry rules = null, NodeRegistry registry = null)
        {
            var (document, path) = LoadTreeOrThrow(projectRoot, args);
            registry = registry ?? NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var dryRun = args["dryRun"]?.Value<bool>() ?? false;

            var migrated = DocumentMigrator.TryMigrate(document, registry, rules ?? NodeMigrationRegistry.Empty, out var outcomes);

            if (outcomes.Count == 0)
            {
                return new JObject
                {
                    ["migrated"] = false,
                    ["persisted"] = false,
                    ["outcomes"] = new JArray(),
                };
            }

            if (!dryRun)
            {
                var writeDiagnostics = TreeDocumentPersistence.Save(path, migrated);
                if (writeDiagnostics.Count > 0)
                {
                    return new JObject
                    {
                        ["migrated"] = true,
                        ["persisted"] = false,
                        ["outcomes"] = WriteOutcomes(outcomes),
                        ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(writeDiagnostics),
                    };
                }
            }

            return new JObject
            {
                ["migrated"] = true,
                ["persisted"] = !dryRun,
                ["outcomes"] = WriteOutcomes(outcomes),
            };
        }

        private static JArray WriteOutcomes(IReadOnlyList<NodeMigrationOutcome> outcomes)
        {
            var array = new JArray();
            foreach (var outcome in outcomes)
            {
                array.Add(new JObject
                {
                    ["nodeId"] = outcome.NodeId.Value,
                    ["typeId"] = outcome.TypeId,
                    ["fromVersion"] = outcome.FromVersion,
                    ["toVersion"] = outcome.ToVersion,
                    ["changes"] = new JArray(outcome.Changes.Select(c => c.Description)),
                });
            }

            return array;
        }

        private static (TreeDocument Document, string Path) LoadTreeOrThrow(string projectRoot, JObject args)
        {
            var treeId = new TreeId(RequireString(args, "treeId"));
            var scan = AibtTreeDiscovery.Scan(projectRoot);
            if (!scan.TryFindPath(treeId, out var path))
            {
                throw new McpToolException(McpMigrationDiagnostics.TreeNotFound, "No tree with id '" + treeId.Value + "' was found under the project.");
            }

            var loaded = TreeDocumentPersistence.Load(path);
            if (!loaded.Success)
            {
                throw new McpToolException(McpMigrationDiagnostics.TreeNotFound, "Tree '" + treeId.Value + "' could not be parsed: " + string.Join("; ", loaded.Diagnostics.Select(d => d.Message)));
            }

            return (loaded.Document, path);
        }

        private static string RequireString(JObject json, string property)
        {
            var value = json[property]?.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new McpToolException(McpMigrationDiagnostics.MalformedArguments, "Missing required string property '" + property + "'.");
            }

            return value;
        }
    }
}
