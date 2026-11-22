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
    /// <c>.aibt</c> / <c>.aibt.json</c> file under <c>Assets/</c> and under every package the
    /// AssetDatabase sees (<c>Packages/&lt;name&gt;/</c>, read from the package's resolved folder)
    /// that Unity imports (folders whose name starts with '.' or ends with '~', and <c>cvs</c>, are
    /// skipped, as Unity skips them), keyed by tree ID. Source IDs are asset paths
    /// (<c>Assets/…</c> or <c>Packages/&lt;name&gt;/…</c>).
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

        /// <summary>Scans since the last domain reload (P11-007: lets tests and probes count disk walks).</summary>
        internal static int ScanCount { get; private set; }

        /// <summary>Reads the current tree files (unchanged files are not parsed again).</summary>
        internal static Snapshot Scan() => Scan(Roots());

        /// <summary>
        /// Reads the tree files under <paramref name="roots"/>: each an asset-path prefix (such as
        /// <c>Assets</c> or <c>Packages/com.example.trees</c>) and the folder on disk it maps to.
        /// </summary>
        internal static Snapshot Scan(IEnumerable<ScanRoot> roots)
        {
            ScanCount++;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = new Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>>();
            var unreadable = new List<string>();
            foreach (var file in FindTreeFiles(roots))
            {
                var path = file.Key;
                if (!seen.Add(path)) continue;
                var document = Read(path, file.Value);
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

        // P11-007 (review F5): the values registered in this Editor session, kept across domain
        // reloads. The AssetDatabase keeps registered custom dependencies for the session, and every
        // tree file imported, moved or deleted since is re-registered by TreeFilePostprocessor, so a
        // reload restores them instead of scanning, registering and refreshing again.
        private const string SessionRegisteredKey = "AIBT.Subtrees.ProjectTreeIndex.Registered";
        private const string SessionUsesSubtreesKey = "AIBT.Subtrees.ProjectTreeIndex.UsesSubtrees";
        private const string SessionSavedKey = "AIBT.Subtrees.ProjectTreeIndex.Saved";

        private static void RegisterAfterReload()
        {
            AssemblyReloadEvents.afterAssemblyReload -= RegisterAfterReload;
            if (RestoreSession()) return;
            Update(Scan());
        }

        private static void Update(Snapshot snapshot)
        {
            // No importer depends on these values while no tree uses subtrees; the first tree that
            // does registers them all when it is imported.
            if ((snapshot.UsesSubtrees || s_registered.Count != 0) && RegisterDependencies(snapshot)) ScheduleRefresh();
            // The node-registry dependency is registered only from a finished catalog discovery
            // (never from the built-ins-only initial catalog), and only while a tree uses subtrees.
            if (snapshot.UsesSubtrees) SubscribeRegistry();
            SaveSession(snapshot.UsesSubtrees);
        }

        private static void SubscribeRegistry()
        {
            if (s_registrySubscribed) return;
            s_registrySubscribed = true;
            ProjectNodeCatalogCache.Changed += RegisterRegistryDependency;
        }

        private static void RegisterRegistryDependency()
        {
            var registry = ProjectNodeCatalogCache.Current.Registry;
            if (registry == null || !Register(RegistryDependencyName, Hash128.Compute(registry.Hash))) return;
            SaveSession(SessionState.GetBool(SessionUsesSubtreesKey, true));
            ScheduleRefresh();
        }

        private static void SaveSession(bool usesSubtrees)
        {
            var text = new StringBuilder();
            foreach (var pair in s_registered.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                text.Append(pair.Key).Append('\t').Append(pair.Value.ToString()).Append('\n');
            SessionState.SetString(SessionRegisteredKey, text.ToString());
            SessionState.SetBool(SessionUsesSubtreesKey, usesSubtrees);
            SessionState.SetBool(SessionSavedKey, true);
        }

        private static bool RestoreSession()
        {
            // A new Editor session starts without saved values and registers them all once.
            if (!SessionState.GetBool(SessionSavedKey, false)) return false;
            var text = SessionState.GetString(SessionRegisteredKey, string.Empty);
            foreach (var line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = line.LastIndexOf('\t');
                if (tab <= 0) return false;
                s_registered[line.Substring(0, tab)] = Hash128.Parse(line.Substring(tab + 1));
            }
            if (SessionState.GetBool(SessionUsesSubtreesKey, false)) SubscribeRegistry();
            return true;
        }

        /// <summary>AssetDatabase refreshes requested since the last domain reload (P11-007: lets tests and probes count them).</summary>
        internal static int RefreshRequests { get; private set; }

        // A custom dependency change takes effect at the next refresh; one is requested once per batch.
        private static void ScheduleRefresh()
        {
            if (s_refreshScheduled) return;
            s_refreshScheduled = true;
            RefreshRequests++;
            EditorApplication.delayCall += () =>
            {
                s_refreshScheduled = false;
                AssetDatabase.Refresh();
            };
        }

        /// <summary>An asset-path prefix and the folder on disk whose files appear under it.</summary>
        internal readonly struct ScanRoot
        {
            internal ScanRoot(string assetRoot, string directory, bool immutable)
            {
                AssetRoot = assetRoot.TrimEnd('/');
                Directory = directory;
                Immutable = immutable;
            }

            internal string AssetRoot { get; }

            internal string Directory { get; }

            /// <summary>
            /// True for packages whose files change only when the package itself changes (registry,
            /// Git, built-in and tarball packages, held by Unity in its package cache): their tree
            /// files are listed once per resolved folder instead of on every scan.
            /// </summary>
            internal bool Immutable { get; }
        }

        /// <summary><c>Assets</c>, then every registered package with a folder on disk, in asset-path order.</summary>
        internal static IReadOnlyList<ScanRoot> Roots()
        {
            var roots = new List<ScanRoot> { new ScanRoot(AssetsRoot, AssetsRoot, false) };
            UnityEditor.PackageManager.PackageInfo[] packages;
            try { packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages(); }
            catch (InvalidOperationException) { return roots; }
            foreach (var package in (packages ?? Array.Empty<UnityEditor.PackageManager.PackageInfo>())
                         .Where(item => item != null && !string.IsNullOrEmpty(item.assetPath) && !string.IsNullOrEmpty(item.resolvedPath))
                         .OrderBy(item => item.assetPath, StringComparer.Ordinal))
            {
                if (!Directory.Exists(package.resolvedPath)) continue;
                var source = package.source;
                var immutable = source != UnityEditor.PackageManager.PackageSource.Embedded
                                && source != UnityEditor.PackageManager.PackageSource.Local;
                roots.Add(new ScanRoot(package.assetPath, package.resolvedPath, immutable));
            }
            return roots;
        }

        // Tree files of immutable packages, keyed by resolved folder (a new package version has a new folder).
        private static readonly Dictionary<string, List<KeyValuePair<string, string>>> s_immutableFiles
            = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);

        /// <summary>Asset path and path on disk of every tree file under <paramref name="roots"/>.</summary>
        private static IEnumerable<KeyValuePair<string, string>> FindTreeFiles(IEnumerable<ScanRoot> roots)
        {
            foreach (var root in roots)
            {
                if (!root.Immutable)
                {
                    foreach (var file in FindTreeFiles(root)) yield return file;
                    continue;
                }
                if (!s_immutableFiles.TryGetValue(root.Directory, out var files))
                    s_immutableFiles[root.Directory] = files = FindTreeFiles(root).ToList();
                foreach (var file in files) yield return file;
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> FindTreeFiles(ScanRoot root)
        {
            var pending = new Stack<KeyValuePair<string, string>>();
            pending.Push(new KeyValuePair<string, string>(root.AssetRoot, root.Directory));
            while (pending.Count != 0)
            {
                var directory = pending.Pop();
                string[] files, directories;
                try
                {
                    files = Directory.GetFiles(directory.Value);
                    directories = Directory.GetDirectories(directory.Value);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (var file in files.OrderBy(item => item, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(file);
                    var path = directory.Key + "/" + name;
                    if (!name.StartsWith(".", StringComparison.Ordinal) && TreeSourcePaths.IsTree(path))
                        yield return new KeyValuePair<string, string>(path, file);
                }
                foreach (var child in directories.OrderByDescending(item => item, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal)
                        || string.Equals(name, "cvs", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(new KeyValuePair<string, string>(directory.Key + "/" + name, child));
                }
            }
        }

        private static TreeDocument Read(string path, string file)
        {
            FileInfo info;
            try { info = new FileInfo(file); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException) { return null; }
            if (!info.Exists) return null;
            if (s_files.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.WriteTicks == info.LastWriteTimeUtc.Ticks)
                return cached.Document;
            TreeDocument document = null;
            try
            {
                var read = CanonicalTreeJson.Parse(File.ReadAllBytes(file), path);
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
                        ? "No tree file under Assets/ or Packages/ declares this tree ID."
                        : "No readable tree file under Assets/ or Packages/ declares this tree ID; unreadable tree files: " + string.Join(", ", _unreadable) + ".");
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
