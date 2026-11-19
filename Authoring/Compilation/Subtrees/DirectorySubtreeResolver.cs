using System;
using System.Collections.Generic;
using System.IO;

namespace AIBT.Authoring
{
    /// <summary>
    /// Resolves subtree references against every <c>.aibt</c> / <c>.aibt.json</c> file under one
    /// directory (recursively). The directory is indexed by tree ID on the first lookup. A file's
    /// source ID is <c>sourceIdPrefix/relative/path</c> with forward slashes; files that cannot be
    /// read are skipped and named in a not-found diagnostic.
    /// </summary>
    public sealed class DirectorySubtreeResolver : ISubtreeResolver
    {
        private readonly string _root;
        private readonly string _sourceIdPrefix;
        private Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>> _index;
        private List<string> _unreadable;

        /// <param name="rootDirectory">The directory searched, recursively.</param>
        /// <param name="sourceIdPrefix">
        /// Logical prefix of the source IDs (for example the directory's path relative to a
        /// project or fixture root); null or empty for none.
        /// </param>
        public DirectorySubtreeResolver(string rootDirectory, string sourceIdPrefix = null)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw new ArgumentException("A directory is required.", nameof(rootDirectory));
            _root = Path.GetFullPath(rootDirectory);
            _sourceIdPrefix = string.IsNullOrEmpty(sourceIdPrefix) ? null : sourceIdPrefix.Replace('\\', '/').Trim('/');
        }

        public SubtreeResolution Resolve(TreeId treeId)
        {
            EnsureIndex();
            if (!_index.TryGetValue(treeId, out var entries))
            {
                return SubtreeResolution.NotFound(_unreadable.Count == 0
                    ? null
                    : "Unreadable tree files in the searched directory: " + string.Join(", ", _unreadable) + ".");
            }

            if (entries.Count == 1) return SubtreeResolution.Found(entries[0].Value, entries[0].Key);
            var sources = new List<string>(entries.Count);
            for (var index = 0; index < entries.Count; index++) sources.Add(entries[index].Key);
            return SubtreeResolution.Ambiguous(sources);
        }

        private void EnsureIndex()
        {
            if (_index != null) return;
            _index = new Dictionary<TreeId, List<KeyValuePair<string, TreeDocument>>>();
            _unreadable = new List<string>();
            var files = TreeSourcePaths.FindFiles(_root);
            for (var index = 0; index < files.Length; index++)
            {
                var sourceId = SourceIdFor(files[index]);
                TreeJsonReadResult read;
                try
                {
                    read = CanonicalTreeJson.Parse(File.ReadAllBytes(files[index]), sourceId);
                }
                catch (IOException)
                {
                    _unreadable.Add(sourceId);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    _unreadable.Add(sourceId);
                    continue;
                }

                if (!read.Success)
                {
                    _unreadable.Add(sourceId);
                    continue;
                }

                if (!_index.TryGetValue(read.Document.TreeId, out var entries))
                {
                    entries = new List<KeyValuePair<string, TreeDocument>>();
                    _index.Add(read.Document.TreeId, entries);
                }

                entries.Add(new KeyValuePair<string, TreeDocument>(sourceId, read.Document));
            }
        }

        private string SourceIdFor(string path)
        {
            var full = Path.GetFullPath(path);
            var relative = full.Length > _root.Length ? full.Substring(_root.Length) : Path.GetFileName(full);
            relative = relative.Replace('\\', '/').Trim('/');
            return _sourceIdPrefix == null ? relative : _sourceIdPrefix + "/" + relative;
        }
    }
}
