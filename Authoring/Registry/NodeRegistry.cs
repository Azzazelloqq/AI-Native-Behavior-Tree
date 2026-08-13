using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public enum NodeManifestSource : byte
    {
        BuiltIn,
        UserExtension,
        TestFixture,
    }

    [Flags]
    public enum NodeRegistryCapabilityFlags : ushort
    {
        None = 0,
        Burst = 1 << 0,
        Managed = 1 << 1,
        MainThread = 1 << 2,
        NonDeterministic = 1 << 3,
        SideEffects = 1 << 4,
        ReferenceHandlerBindings = 1 << 5,
        UserExtensions = 1 << 6,
    }

    internal sealed class NodeHandlerBindingContract
    {
        internal NodeHandlerBindingContract(string handlerId, uint version, NodeExecutionDomain executionDomain)
        {
            if (!NodeTypeIdRules.IsValid(handlerId))
            {
                throw new ArgumentException("Handler IDs must be canonical project-qualified IDs.", nameof(handlerId));
            }

            if (version == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(version));
            }

            if (!Enum.IsDefined(typeof(NodeExecutionDomain), executionDomain))
            {
                throw new ArgumentOutOfRangeException(nameof(executionDomain));
            }

            HandlerId = handlerId;
            Version = version;
            ExecutionDomain = executionDomain;
        }

        internal string HandlerId { get; }

        internal uint Version { get; }

        internal NodeExecutionDomain ExecutionDomain { get; }
    }

    public sealed class NodeRegistryEntry
    {
        internal NodeRegistryEntry(
            NodeManifest manifest,
            ulong numericTypeId,
            NodeManifestSource source,
            NodeHandlerBindingContract handlerBinding)
        {
            Manifest = manifest;
            NumericTypeId = numericTypeId;
            Source = source;
            HandlerBinding = handlerBinding;
        }

        public NodeManifest Manifest { get; }

        public ulong NumericTypeId { get; }

        public NodeManifestSource Source { get; }

        public bool HasReferenceHandlerBinding => HandlerBinding != null;

        internal NodeHandlerBindingContract HandlerBinding { get; }
    }

    public sealed class NodeRegistry : IReadOnlyList<NodeRegistryEntry>
    {
        private readonly ReadOnlyCollection<NodeRegistryEntry> _entries;
        private readonly ReadOnlyDictionary<string, NodeRegistryEntry> _byCanonicalTypeId;
        private readonly ReadOnlyDictionary<ulong, NodeRegistryEntry> _byNumericTypeId;

        internal NodeRegistry(IList<NodeRegistryEntry> entries, string hash, NodeRegistryCapabilityFlags capabilities)
        {
            _entries = new List<NodeRegistryEntry>(entries).AsReadOnly();
            Hash = hash;
            Capabilities = capabilities;

            var byCanonical = new Dictionary<string, NodeRegistryEntry>(StringComparer.Ordinal);
            var byNumeric = new Dictionary<ulong, NodeRegistryEntry>();
            for (var index = 0; index < entries.Count; index++)
            {
                byCanonical.Add(entries[index].Manifest.TypeId, entries[index]);
                byNumeric.Add(entries[index].NumericTypeId, entries[index]);
            }

            _byCanonicalTypeId = new ReadOnlyDictionary<string, NodeRegistryEntry>(byCanonical);
            _byNumericTypeId = new ReadOnlyDictionary<ulong, NodeRegistryEntry>(byNumeric);
        }

        public int Count => _entries.Count;

        public NodeRegistryEntry this[int index] => _entries[index];

        public string Hash { get; }

        public NodeRegistryCapabilityFlags Capabilities { get; }

        public bool TryGet(string canonicalTypeId, out NodeRegistryEntry entry)
        {
            if (canonicalTypeId == null)
            {
                entry = null;
                return false;
            }

            return _byCanonicalTypeId.TryGetValue(canonicalTypeId, out entry);
        }

        public bool TryGet(ulong numericTypeId, out NodeRegistryEntry entry)
        {
            return _byNumericTypeId.TryGetValue(numericTypeId, out entry);
        }

        public IEnumerator<NodeRegistryEntry> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class NodeRegistryBuildResult
    {
        internal NodeRegistryBuildResult(NodeRegistry registry, DiagnosticCollection diagnostics)
        {
            Registry = registry;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public NodeRegistry Registry { get; }

        public DiagnosticCollection Diagnostics { get; }

        public bool Success => Registry != null && Diagnostics.Count == 0;
    }
}
