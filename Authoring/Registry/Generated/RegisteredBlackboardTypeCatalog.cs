using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class RegisteredBlackboardTypeCatalogEntry
    {
        private readonly ReadOnlyCollection<GeneratedStorageField> _fields;

        public RegisteredBlackboardTypeCatalogEntry(
            string canonicalTypeId,
            uint version,
            string canonicalSchemaId,
            string schemaHash,
            RegisteredUnmanagedTypeDescriptor descriptor,
            IEnumerable<GeneratedStorageField> fields)
        {
            if (!NodeTypeIdRules.IsValid(canonicalTypeId)) throw new ArgumentException("Invalid canonical registered type ID.", nameof(canonicalTypeId));
            if (!NodeTypeIdRules.IsValid(canonicalSchemaId)) throw new ArgumentException("Invalid canonical registered schema ID.", nameof(canonicalSchemaId));
            if (!descriptor.IsValid || descriptor.TypeId != StableHash.Fnv1A64(canonicalTypeId)
                || descriptor.Version != version || descriptor.CanonicalSchemaId != StableHash.Fnv1A64(canonicalSchemaId)
                || descriptor.EqualityContractId != GeneratedNodeMetadata.CanonicalBytesEqualityContractId
                || !GeneratedTypeRecordHash.IsHash(schemaHash))
                throw new ArgumentException("Registered catalog entry differs from the accepted schema/equality descriptor.", nameof(descriptor));
            var values = new List<GeneratedStorageField>(fields ?? throw new ArgumentNullException(nameof(fields)));
            values.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.FieldId, right.FieldId));
            var offsets = new uint[values.Count];
            for (var index = 0; index < values.Count; index++) offsets[index] = values[index].Offset;
            var layout = GeneratedNodeMetadata.Layout(values, out var size, out var alignment);
            if (size != descriptor.Size || alignment != descriptor.Alignment)
                throw new ArgumentException("Registered catalog layout differs from its descriptor.", nameof(fields));
            for (var index = 0; index < values.Count; index++)
                if (offsets[index] != layout[index].Offset)
                    throw new ArgumentException("Registered catalog fields are not canonically packed.", nameof(fields));
            CanonicalTypeId = canonicalTypeId;
            Version = version;
            CanonicalSchemaId = canonicalSchemaId;
            SchemaHash = schemaHash;
            Descriptor = descriptor;
            _fields = values.AsReadOnly();
        }

        public string CanonicalTypeId { get; }
        public uint Version { get; }
        public string CanonicalSchemaId { get; }
        public string SchemaHash { get; }
        public RegisteredUnmanagedTypeDescriptor Descriptor { get; }
        public IReadOnlyList<GeneratedStorageField> Fields => _fields;
    }

    public sealed class RegisteredBlackboardTypeCatalog
    {
        private readonly ReadOnlyCollection<RegisteredBlackboardTypeCatalogEntry> _entries;
        private readonly Dictionary<string, RegisteredBlackboardTypeCatalogEntry> _byIdentity;

        public RegisteredBlackboardTypeCatalog(IEnumerable<RegisteredBlackboardTypeCatalogEntry> entries)
        {
            var values = new List<RegisteredBlackboardTypeCatalogEntry>(entries ?? throw new ArgumentNullException(nameof(entries)));
            values.Sort((left, right) =>
            {
                var comparison = Utf8OrdinalComparer.Instance.Compare(left.CanonicalTypeId, right.CanonicalTypeId);
                return comparison != 0 ? comparison : left.Version.CompareTo(right.Version);
            });
            _byIdentity = new Dictionary<string, RegisteredBlackboardTypeCatalogEntry>(StringComparer.Ordinal);
            for (var index = 0; index < values.Count; index++)
            {
                var entry = values[index] ?? throw new ArgumentException("Registered catalog entries cannot be null.", nameof(entries));
                var identity = Identity(entry.CanonicalTypeId, entry.Version);
                if (_byIdentity.ContainsKey(identity))
                    throw new ArgumentException("Registered catalog identities must be unique.", nameof(entries));
                _byIdentity.Add(identity, entry);
            }
            _entries = values.AsReadOnly();
        }

        public IReadOnlyList<RegisteredBlackboardTypeCatalogEntry> Entries => _entries;

        public bool TryGet(string canonicalTypeId, uint version, out RegisteredBlackboardTypeCatalogEntry entry)
            => _byIdentity.TryGetValue(Identity(canonicalTypeId, version), out entry);

        public BlackboardDefaultValue CreateDefault(string canonicalTypeId, uint version, SemanticObject value)
        {
            if (!TryGet(canonicalTypeId, version, out var entry))
                throw new ArgumentException("Registered default type/version is absent from the accepted catalog.", nameof(canonicalTypeId));
            return BlackboardDefaultValue.RegisteredCanonical(canonicalTypeId, version, value,
                RegisteredBlackboardDefaultCodec.Encode(entry, value, this));
        }

        private static string Identity(string id, uint version) => id + "\0" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class GeneratedShardMetadataArtifact
    {
        internal GeneratedShardMetadataArtifact(
            IReadOnlyList<GeneratedNodeDescriptor> nodes,
            RegisteredBlackboardTypeCatalog registeredTypes)
            : this(null, 0u, nodes, registeredTypes)
        {
        }

        internal GeneratedShardMetadataArtifact(
            string shardId,
            uint shardVersion,
            IReadOnlyList<GeneratedNodeDescriptor> nodes,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            if (shardId != null && (!NodeTypeIdRules.IsValid(shardId) || shardVersion == 0u))
                throw new ArgumentException("Generated shard identity is invalid.", nameof(shardId));
            if (shardId == null && shardVersion != 0u)
                throw new ArgumentException("Generated shard identity must be supplied atomically.", nameof(shardVersion));
            ShardId = shardId;
            ShardVersion = shardVersion;
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            RegisteredTypes = registeredTypes ?? throw new ArgumentNullException(nameof(registeredTypes));
        }

        public string ShardId { get; }
        public uint ShardVersion { get; }
        public IReadOnlyList<GeneratedNodeDescriptor> Nodes { get; }
        public RegisteredBlackboardTypeCatalog RegisteredTypes { get; }
    }
}
