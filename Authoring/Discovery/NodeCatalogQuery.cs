using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    // Read-only query layer over an already-built NodeRegistry (P1-004). Formats results using
    // NodeManifestCanonicalJson directly -- never a second, hand-maintained catalog.
    public sealed class NodeCatalogQuery
    {
        private readonly NodeRegistry _registry;
        private readonly List<NodeRegistryEntry> _ordered;

        public NodeCatalogQuery(NodeRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));

            _ordered = new List<NodeRegistryEntry>(registry);
            _ordered.Sort((left, right) =>
            {
                var comparison = string.Compare(left.Manifest.TypeId, right.Manifest.TypeId, StringComparison.Ordinal);
                return comparison != 0 ? comparison : left.Manifest.Version.CompareTo(right.Manifest.Version);
            });
        }

        public NodeRegistry Registry => _registry;

        public int Count => _ordered.Count;

        // Case-insensitive substring match over TypeId/Category/Summary. Deterministic ordering
        // matches NodeManifestCanonicalJson's own TypeId-then-Version sort.
        public IReadOnlyList<NodeRegistryEntry> Search(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return _ordered.AsReadOnly();
            }

            var results = new List<NodeRegistryEntry>();
            for (var index = 0; index < _ordered.Count; index++)
            {
                var manifest = _ordered[index].Manifest;
                if (Contains(manifest.TypeId, keyword) || Contains(manifest.Category, keyword) || Contains(manifest.Summary, keyword))
                {
                    results.Add(_ordered[index]);
                }
            }

            return results.AsReadOnly();
        }

        // Deterministic pagination over the same TypeId-then-Version ordering as Search's default.
        public IReadOnlyList<NodeRegistryEntry> Page(int offset, int count)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (offset >= _ordered.Count)
            {
                return Array.Empty<NodeRegistryEntry>();
            }

            var available = Math.Min(count, _ordered.Count - offset);
            var page = new NodeRegistryEntry[available];
            _ordered.CopyTo(offset, page, 0, available);
            return page;
        }

        public bool TryGetContract(string typeId, out JObject manifestJson)
        {
            if (_registry.TryGet(typeId, out var entry))
            {
                manifestJson = NodeManifestCanonicalJson.ToJson(entry.Manifest);
                return true;
            }

            manifestJson = null;
            return false;
        }

        public string SerializeCatalog(IEnumerable<NodeRegistryEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var array = new List<NodeRegistryEntry>(entries).ToArray();
            return NodeManifestCanonicalJson.SerializeRegistry(array);
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
