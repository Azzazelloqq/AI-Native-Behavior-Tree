using System;
using System.IO;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;

namespace AIBT.Editor.Graph
{
    /// <summary>A read-only source summary, shared by browser and imported-asset inspector.</summary>
    public sealed class TreeCatalogEntry
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public string TreeId { get; private set; }
        public string[] Tags { get; private set; } = Array.Empty<string>();
        public string Status { get; private set; }
        public string Details { get; private set; }
        public int NodeCount { get; private set; }

        /// <summary>
        /// The subtree expansion of a tree with references or an interface (ADR-P11-001); null for
        /// every other tree. On success the status and node count describe the expanded tree.
        /// </summary>
        internal SubtreeExpansion Expansion { get; private set; }

        public static TreeCatalogEntry Read(string path)
            => Read(path, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry);

        internal static TreeCatalogEntry Read(string path, NodeRegistry registry)
            => Read(path, registry, null);

        /// <param name="expansion">
        /// The expansion to describe for a tree that uses subtrees; when null it is expanded here
        /// against the project's trees with <paramref name="registry"/>.
        /// </param>
        internal static TreeCatalogEntry Read(string path, NodeRegistry registry, SubtreeExpansion expansion)
        {
            var entry = new TreeCatalogEntry { Path = path, Name = System.IO.Path.GetFileName(path), TreeId = "", Status = "Errors" };
            try
            {
                var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                entry.Details = string.Join("\n", parsed.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                if (!parsed.Success) return entry;
                var document = parsed.Document;
                entry.Name = document.Name;
                entry.TreeId = document.TreeId.Value;
                entry.Tags = document.Tags.Values.ToArray();
                entry.NodeCount = document.Nodes.Count;
                if (SubtreeExpander.RequiresExpansion(document))
                {
                    entry.Expansion = expansion ?? ProjectTreeIndex.Expand(path, document, registry, null, ProjectTreeIndex.Scan());
                    if (!entry.Expansion.Success)
                    {
                        entry.Details = string.Join("\n", entry.Expansion.Diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                        if (OnlyUnknownNodeTypes(entry.Expansion.Diagnostics))
                        {
                            // As for a tree without subtrees: node types the catalog does not know
                            // are not a source error; the tree is reported as needing a registry.
                            entry.Status = "Needs registry";
                            entry.Details = "Source parsed. Project node contracts are not validated here; the subtree expansion "
                                + "needs node types the project catalog does not know:\n" + entry.Details;
                        }
                        return entry;
                    }
                    document = entry.Expansion.Document;
                    entry.NodeCount = document.Nodes.Count;
                }
                var unresolved = document.Nodes.Where(n => !registry.TryGet(n.TypeId, out _))
                    .Select(n => n.TypeId).Distinct().ToArray();
                if (unresolved.Length != 0)
                {
                    entry.Status = "Needs registry";
                    entry.Details = "Source parsed. Project node contracts are not validated here: " + string.Join(", ", unresolved);
                    return entry;
                }
                // Trees with managed (C#) nodes run on BehaviorTreeAgent and are validated with the
                // managed policy, which accepts the main-thread nodes the native policy rejects.
                var managed = document.Nodes.Any(node => registry.TryGet(node.TypeId, out var item) &&
                                                         item.Manifest.ExecutionDomain != NodeExecutionDomain.Burst);
                var options = managed
                    ? ManagedTreeProgram.CreateOptions(path).Policy.CreateValidationOptions(path)
                    : new ValidationOptions(path, supportsAgentScope: true, supportsSharedScope: true);
                var diagnostics = TreeValidator.Validate(document, registry, options);
                entry.Status = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? "Errors"
                    : diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning) ? "Warnings"
                    : !managed && document.Nodes.Any(node => registry.TryGet(node.TypeId, out var item) &&
                                                 item.Source == NodeManifestSource.UserExtension && !item.HasReferenceHandlerBinding)
                        ? "Needs native compile" : "Valid";
                entry.Details = string.Join("\n", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                if (entry.Status == "Valid" && managed)
                    entry.Details = "Node contracts validated; managed C# nodes run on a BehaviorTreeAgent.";
                else if (entry.Status == "Needs native compile")
                    entry.Details = "Node contracts validated; generated runtime compilation has not been checked here.";
                else if (entry.Status == "Valid")
                    entry.Details = "Node contracts validated; runtime compilation is a separate step.";
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                entry.Details = exception.Message;
            }
            return entry;
        }

        /// <summary>
        /// True when every error of a failed expansion is <c>AIBT2021</c> (a node type the registry
        /// does not know). Any other error — a subtree that is missing, ambiguous or recursive, a key
        /// map error, any other validation error — keeps the status <c>Errors</c>.
        /// </summary>
        internal static bool OnlyUnknownNodeTypes(DiagnosticCollection diagnostics)
        {
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            return errors.Count != 0 && errors.All(d => d.Code == TreeValidationDiagnosticCodes.UnknownNodeType);
        }

        public bool Matches(string search, string tag, string status)
        {
            bool Contains(string value) => value.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
            return (Contains(Name) || Contains(TreeId) || Contains(Path))
                && (string.IsNullOrEmpty(tag) || tag == "All tags" || Tags.Contains(tag))
                && (string.IsNullOrEmpty(status) || status == "All states" || Status == status);
        }
    }
}
