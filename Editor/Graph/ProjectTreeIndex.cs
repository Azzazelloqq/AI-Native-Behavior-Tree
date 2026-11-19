using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using UnityEditor;
using UnityEngine;

namespace AIBT.Editor.Graph
{
    /// <summary>
    /// The project's tree-ID index for subtree references (ADR-P11-001 §3(a) "Assets", §5): every
    /// <c>.aibt</c> / <c>.aibt.json</c> file under <c>Assets/</c> that Unity imports (folders whose
    /// name starts with '.' or ends with '~' are skipped, as Unity skips them), keyed by tree ID.
    /// A snapshot is an <see cref="ISubtreeResolver"/> whose source IDs are project asset paths; a
    /// tree ID declared by several files is ambiguous (<c>AIBT3021</c>), never resolved to one of them.
    /// <para>
    /// The importer makes every tree that uses subtrees depend on one Unity custom dependency per
    /// tree ID it looked up (<see cref="DependencyName"/>). Its value is a hash of the files that
    /// declare the ID; this class re-registers the values whenever a tree file is imported, moved
    /// or deleted and after every domain reload, so adding, removing or retargeting a file with a
    /// given tree ID re-imports every tree that resolved that ID.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectTreeIndex
    {
        internal const string DependencyPrefix = "AIBT/Subtrees/TreeId/";

        /// <summary>
        /// The project node registry's hash: an expansion renames <c>BlackboardKey</c> parameters by
        /// their manifest types, so a stored expansion depends on the node catalog.
        /// </summary>
        internal const string RegistryDependencyName = "AIBT/Subtrees/NodeRegistry";

        private const string AssetsRoot = "Assets";
        private static readonly Dictionary<string, CachedFile> s_files = new Dictionary<string, CachedFile>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Hash128> s_registered = new Dictionary<string, Hash128>(StringComparer.Ordinal);
        private static bool s_refreshScheduled;
        private static bool s_registrySubscribed;

        static ProjectTreeIndex()
        {
            AssemblyReloadEvents.afterAssemblyReload += RegisterAfterReload;
        }

        internal static string DependencyName(TreeId treeId) => DependencyPrefix + treeId.Value;

        /// <summary>
        /// Expands <paramref name="document"/> (a project tree at <paramref name="path"/>) for import:
        /// structural validation only, with a policy that admits every node domain and scope, since
        /// the managed agent and the native bake each compile the expansion under their own policy.
        /// </summary>
        internal static SubtreeExpansion Expand(string path, TreeDocument document, NodeRegistry registry,
            RegisteredBlackboardTypeCatalog registeredTypes, ISubtreeResolver resolver)
            => SubtreeExpander.Expand(document, resolver, registry, ExpansionPolicy.CreateValidationOptions(path), registeredTypes);

        private static readonly ReferenceCompilationPolicy ExpansionPolicy = new ReferenceCompilationPolicy(
            allowManagedNodes: true, allowMainThreadNodes: true, requireDeterministicNodes: false,
            supportsAgentScope: true, supportsSharedScope: true);

        /// <summary>Reads the current tree files (unchanged files are not parsed again).</summary>
        internal static Snapshot Scan()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = new Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>>();
            var unreadable = new List<string>();
            foreach (var path in FindTreeFiles())
            {
                seen.Add(path);
                var document = Read(path);
                if (document == null)
                {
                    unreadable.Add(path);
                    continue;
                }
                if (!entries.TryGetValue(document.TreeId, out var list)) entries.Add(document.TreeId, list = new List<KeyValuePair<string, TreeDocument>>());
                list.Add(new KeyValuePair<string, TreeDocument>(path, document));
            }
            foreach (var stale in s_files.Keys.Where(path => !seen.Contains(path)).ToList()) s_files.Remove(stale);
            return new Snapshot(entries, unreadable, entries.Values.Any(list => list.Any(item => SubtreeExpander.RequiresExpansion(item.Value))));
        }

        /// <summary>
        /// Registers the custom dependency of every tree ID in <paramref name="snapshot"/> (and clears
        /// the ones that disappeared). Returns true when a value changed, so dependants need a refresh.
        /// </summary>
        internal static bool RegisterDependencies(Snapshot snapshot)
        {
            var changed = false;
            var current = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in snapshot.Paths)
            {
                var name = DependencyName(pair.Key);
                current.Add(name);
                changed |= Register(name, Hash128.Compute("files\n" + string.Join("\n", pair.Value)));
            }
            foreach (var name in s_registered.Keys.Where(name => name.StartsWith(DependencyPrefix, StringComparison.Ordinal) && !current.Contains(name)).ToList())
                changed |= Register(name, Hash128.Compute("none"));
            return changed;
        }

        private static bool Register(string name, Hash128 value)
        {
            if (s_registered.TryGetValue(name, out var previous) && previous == value) return false;
            s_registered[name] = value;
            AssetDatabase.RegisterCustomDependency(name, value);
            return true;
        }

        private static void RegisterAfterReload()
        {
            AssemblyReloadEvents.afterAssemblyReload -= RegisterAfterReload;
            Update(Scan());
        }

        private static void Update(Snapshot snapshot)
        {
            if (RegisterDependencies(snapshot)) ScheduleRefresh();
            // The node-registry dependency is registered only from a finished catalog discovery
            // (never from the built-ins-only initial catalog), and only while a tree uses subtrees.
            if (snapshot.UsesSubtrees && !s_registrySubscribed)
            {
                s_registrySubscribed = true;
                ProjectNodeCatalogCache.Changed += RegisterRegistryDependency;
            }
        }

        private static void RegisterRegistryDependency()
        {
            var registry = ProjectNodeCatalogCache.Current.Registry;
            if (registry != null && Register(RegistryDependencyName, Hash128.Compute(registry.Hash))) ScheduleRefresh();
        }

        // A custom dependency change takes effect at the next refresh; one is requested once per batch.
        private static void ScheduleRefresh()
        {
            if (s_refreshScheduled) return;
            s_refreshScheduled = true;
            EditorApplication.delayCall += () =>
            {
                s_refreshScheduled = false;
                AssetDatabase.Refresh();
            };
        }

        private static IEnumerable<string> FindTreeFiles()
        {
            var pending = new Stack<string>();
            pending.Push(AssetsRoot);
            while (pending.Count != 0)
            {
                var directory = pending.Pop();
                string[] files, directories;
                try
                {
                    files = Directory.GetFiles(directory);
                    directories = Directory.GetDirectories(directory);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var file in files.OrderBy(item => item, StringComparer.Ordinal))
                {
                    var path = file.Replace('\\', '/');
                    if (!Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal) && TreeSourcePaths.IsTree(path)) yield return path;
                }
                foreach (var child in directories.OrderByDescending(item => item, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal)
                        || string.Equals(name, "cvs", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(child);
                }
            }
        }

        private static TreeDocument Read(string path)
        {
            FileInfo info;
            try { info = new FileInfo(path); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException) { return null; }
            if (!info.Exists) return null;
            if (s_files.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.WriteTicks == info.LastWriteTimeUtc.Ticks)
                return cached.Document;
            TreeDocument document = null;
            try
            {
                var read = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                if (read.Success) document = read.Document;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            s_files[path] = new CachedFile { Length = info.Length, WriteTicks = info.LastWriteTimeUtc.Ticks, Document = document };
            return document;
        }

        private struct CachedFile
        {
            internal long Length;
            internal long WriteTicks;
            internal TreeDocument Document;
        }

        /// <summary>One reading of the project's tree files.</summary>
        internal sealed class Snapshot : ISubtreeResolver
        {
            private readonly Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>> _entries;
            private readonly List<string> _unreadable;

            internal Snapshot(Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>> entries, List<string> unreadable, bool usesSubtrees)
            {
                _entries = entries;
                _unreadable = unreadable;
                UsesSubtrees = usesSubtrees;
            }

            /// <summary>True when a tree file has a reference node or an interface.</summary>
            internal bool UsesSubtrees { get; }

            /// <summary>Every tree ID with the project paths that declare it, in ordinal path order.</summary>
            internal IEnumerable<KeyValuePair<TreeId, IReadOnlyList<string>>> Paths
                => _entries.Select(pair => new KeyValuePair<TreeId, IReadOnlyList<string>>(pair.Key, pair.Value.Select(item => item.Key).ToList()));

            public SubtreeResolution Resolve(TreeId treeId)
            {
                if (!_entries.TryGetValue(treeId, out var list))
                    return SubtreeResolution.NotFound(_unreadable.Count == 0
                        ? "No tree file under Assets/ declares this tree ID."
                        : "No readable tree file under Assets/ declares this tree ID; unreadable tree files: " + string.Join(", ", _unreadable) + ".");
                if (list.Count == 1) return SubtreeResolution.Found(list[0].Value, list[0].Key);
                return SubtreeResolution.Ambiguous(list.Select(item => item.Key));
            }
        }

        /// <summary>Re-registers the tree-ID dependencies whenever a tree file is added, changed, moved or deleted.</summary>
        private sealed class TreeFilePostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (!imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(TreeSourcePaths.IsTree)) return;
                Update(Scan());
            }
        }
    }

    /// <summary>
    /// Records what an expansion looked up: every tree ID it asked for and every file it was given
    /// (found or ambiguous), so the importer can declare them as dependencies.
    /// </summary>
    internal sealed class RecordingSubtreeResolver : ISubtreeResolver
    {
        private readonly ISubtreeResolver _inner;
        private readonly List<TreeId> _requested = new List<TreeId>();
        private readonly SortedDictionary<string, string> _found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _sources = new SortedSet<string>(StringComparer.Ordinal);
        private readonly List<TreeDocument> _documents = new List<TreeDocument>();

        internal RecordingSubtreeResolver(ISubtreeResolver inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Tree IDs asked for, in first-request order.</summary>
        internal IReadOnlyList<TreeId> Requested => _requested;

        /// <summary>Every source ID the inner resolver named (found or ambiguous candidates), ordinal order.</summary>
        internal IEnumerable<string> Sources => _sources;

        /// <summary>Resolved tree ID → source ID, ordered by tree ID (ordinal).</summary>
        internal IEnumerable<KeyValuePair<string, string>> Found => _found;

        /// <summary>Every resolved document, once per tree ID, in first-resolution order.</summary>
        internal IReadOnlyList<TreeDocument> Documents => _documents;

        /// <summary>True when every requested tree ID resolved to exactly one source.</summary>
        internal bool Complete { get; private set; } = true;

        public SubtreeResolution Resolve(TreeId treeId)
        {
            var resolution = _inner.Resolve(treeId) ?? SubtreeResolution.NotFound();
            if (!_requested.Contains(treeId)) _requested.Add(treeId);
            if (resolution.Status == SubtreeResolutionStatus.Found)
            {
                if (!_found.ContainsKey(treeId.Value)) _documents.Add(resolution.Document);
                _found[treeId.Value] = resolution.SourceId;
                _sources.Add(resolution.SourceId);
            }
            else
            {
                Complete = false;
                foreach (var candidate in resolution.CandidateSourceIds) _sources.Add(candidate);
            }
            return resolution;
        }

        /// <summary>
        /// Follows every reference of <paramref name="document"/> and, recursively, of each resolved
        /// subtree (each tree ID once), without validating or expanding: the set of files an expansion
        /// of the document reads.
        /// </summary>
        internal void Walk(TreeDocument document)
        {
            var visited = new HashSet<TreeId>();
            var pending = new Stack<TreeDocument>();
            pending.Push(document);
            visited.Add(document.TreeId);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                foreach (var node in current.Nodes)
                {
                    if (node?.Subtree == null || !visited.Add(node.Subtree.TreeId)) continue;
                    var resolution = Resolve(node.Subtree.TreeId);
                    if (resolution.Status == SubtreeResolutionStatus.Found) pending.Push(resolution.Document);
                }
            }
        }
    }
}
