using System;
using System.IO;
using System.Linq;
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

        public static TreeCatalogEntry Read(string path)
            => Read(path, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry);

        internal static TreeCatalogEntry Read(string path, NodeRegistry registry)
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
                var unresolved = document.Nodes.Where(n => !registry.TryGet(n.TypeId, out _))
                    .Select(n => n.TypeId).Distinct().ToArray();
                if (unresolved.Length != 0)
                {
                    entry.Status = "Needs registry";
                    entry.Details = "Source parsed. Project node contracts are not validated here: " + string.Join(", ", unresolved);
                    return entry;
                }
                var diagnostics = TreeValidator.Validate(document, registry,
                    new ValidationOptions(path, supportsAgentScope: true, supportsSharedScope: true));
                entry.Status = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? "Errors"
                    : diagnostics.Count > 0 ? "Warnings"
                    : document.Nodes.Any(node => registry.TryGet(node.TypeId, out var item) &&
                                                 item.Source == NodeManifestSource.UserExtension && !item.HasReferenceHandlerBinding)
                        ? "Needs native compile" : "Valid";
                entry.Details = string.Join("\n", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
                if (entry.Status == "Needs native compile")
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

        public bool Matches(string search, string tag, string status)
        {
            bool Contains(string value) => value.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
            return (Contains(Name) || Contains(TreeId) || Contains(Path))
                && (string.IsNullOrEmpty(tag) || tag == "All tags" || Tags.Contains(tag))
                && (string.IsNullOrEmpty(status) || status == "All states" || Status == status);
        }
    }
}
