using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public sealed class NodeRegistryBuilder
    {
        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly Func<string, ulong> _typeIdHash;
        private readonly Dictionary<string, IReferenceLeafBehavior> _projectBehaviors = new Dictionary<string, IReferenceLeafBehavior>(StringComparer.Ordinal);

        public NodeRegistryBuilder()
            : this(StableHash.Fnv1A64)
        {
        }

        internal NodeRegistryBuilder(Func<string, ulong> typeIdHash)
        {
            _typeIdHash = typeIdHash ?? throw new ArgumentNullException(nameof(typeIdHash));
        }

        public static NodeRegistryBuilder CreateWithBuiltIns()
        {
            var builder = new NodeRegistryBuilder();
            for (var index = 0; index < BuiltInNodeManifests.All.Count; index++)
            {
                builder.AddBuiltIn(BuiltInNodeManifests.All[index]);
            }

            for (var index = 0; index < BuiltInLeafManifests.All.Count; index++)
            {
                var provider = BuiltInLeafManifests.All[index];
                builder.AddBuiltInLeaf(provider.Manifest, provider.CreateBehavior());
            }

            return builder;
        }

        public NodeRegistryBuilder AddUserExtension(NodeManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            _registrations.Add(new Registration(manifest, NodeManifestSource.UserExtension, null));
            return this;
        }

        // Applies ADR-P6-017 (P7-008): the public registration path for a project's own
        // reference-executor leaf. Unlike AddUserExtension, this attaches a real handler binding
        // (ValidateBinding accepts one for UserExtension only when it arrived this way) and keeps
        // the actual behavior instance available via TryGetProjectLeafBehavior so a registry
        // consumer can wire it into a ReferenceLeafRegistry for execution.
        public NodeRegistryBuilder AddProjectExtension(NodeManifest manifest, IReferenceLeafBehavior behavior)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (behavior == null)
            {
                throw new ArgumentNullException(nameof(behavior));
            }

            _registrations.Add(new Registration(manifest, NodeManifestSource.UserExtension, CreateProjectHandlerBinding(manifest)));
            _projectBehaviors[manifest.TypeId] = behavior;
            return this;
        }

        // Populated by AddProjectExtension and AddBuiltInLeaf. Lets a registry consumer (e.g. a test
        // or the MCP discovery-tool wiring) obtain the actual executable behavior for a
        // project-registered or built-in leaf, to fold into a ReferenceLeafRegistry alongside the
        // fixture bindings.
        public bool TryGetProjectLeafBehavior(string typeId, out IReferenceLeafBehavior behavior)
            => _projectBehaviors.TryGetValue(typeId, out behavior);

        // Built-in leaf counterpart of AddProjectExtension (P7-028): unlike a project extension, a
        // built-in leaf's type ID must use a reserved built-in namespace (aibt.core or
        // aibt.stdlib -- see ValidateSource), so it is registered with NodeManifestSource.BuiltIn
        // and a synthesized reference handler binding, exactly like the structural built-ins
        // AddBuiltIn registers -- the only difference is this source also carries a real,
        // executable IReferenceLeafBehavior (the structural built-ins are interpreted directly by
        // ReferenceExecutionMachine and need none).
        public NodeRegistryBuilder AddBuiltInLeaf(NodeManifest manifest, IReferenceLeafBehavior behavior)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (behavior == null)
            {
                throw new ArgumentNullException(nameof(behavior));
            }

            _registrations.Add(new Registration(manifest, NodeManifestSource.BuiltIn, CreateReferenceBinding(manifest)));
            _projectBehaviors[manifest.TypeId] = behavior;
            return this;
        }

        public NodeRegistryBuildResult Build()
        {
            var ordered = new List<Registration>(_registrations);
            ordered.Sort(RegistrationComparer.Instance);
            var diagnostics = new List<Diagnostic>();
            var accepted = new List<NodeRegistryEntry>(ordered.Count);
            var byCanonical = new Dictionary<string, Registration>(StringComparer.Ordinal);
            var byNumeric = new Dictionary<ulong, Registration>();

            for (var index = 0; index < ordered.Count; index++)
            {
                var registration = ordered[index];
                ValidateSource(registration, diagnostics);
                ValidateBinding(registration, diagnostics);

                if (byCanonical.TryGetValue(registration.Manifest.TypeId, out var existingCanonical))
                {
                    diagnostics.Add(registration.Manifest.Version == existingCanonical.Manifest.Version
                        ? NodeRegistryDiagnostics.Duplicate(registration.Manifest.TypeId, registration.Manifest.Version)
                        : NodeRegistryDiagnostics.IncompatibleVersions(
                            registration.Manifest.TypeId,
                            existingCanonical.Manifest.Version,
                            registration.Manifest.Version));
                    continue;
                }

                var numericTypeId = _typeIdHash(registration.Manifest.TypeId);
                if (byNumeric.TryGetValue(numericTypeId, out var existingNumeric)
                    && !string.Equals(existingNumeric.Manifest.TypeId, registration.Manifest.TypeId, StringComparison.Ordinal))
                {
                    diagnostics.Add(NodeRegistryDiagnostics.NumericCollision(
                        numericTypeId,
                        existingNumeric.Manifest.TypeId,
                        registration.Manifest.TypeId));
                    continue;
                }

                byCanonical.Add(registration.Manifest.TypeId, registration);
                byNumeric.Add(numericTypeId, registration);
                accepted.Add(new NodeRegistryEntry(
                    registration.Manifest,
                    numericTypeId,
                    registration.Source,
                    registration.HandlerBinding));
            }

            var diagnosticCollection = new DiagnosticCollection(diagnostics);
            if (diagnosticCollection.Count != 0)
            {
                return new NodeRegistryBuildResult(null, diagnosticCollection);
            }

            var entries = accepted.ToArray();
            var canonicalBytes = NodeManifestCanonicalJson.SerializeRegistryUtf8(entries);
            var hash = StableHash.Sha256Hex(canonicalBytes);
            return new NodeRegistryBuildResult(
                new NodeRegistry(entries, hash, ComputeCapabilities(entries)),
                diagnosticCollection);
        }

        internal NodeRegistryBuilder AddTestFixtures()
        {
            for (var index = 0; index < ReferenceFixtureNodeManifests.All.Count; index++)
            {
                var manifest = ReferenceFixtureNodeManifests.All[index];
                _registrations.Add(new Registration(
                    manifest,
                    NodeManifestSource.TestFixture,
                    CreateReferenceBinding(manifest)));
            }

            return this;
        }

        internal NodeRegistryBuilder AddBuiltInForTest(NodeManifest manifest, NodeHandlerBindingContract binding)
        {
            _registrations.Add(new Registration(
                manifest ?? throw new ArgumentNullException(nameof(manifest)),
                NodeManifestSource.BuiltIn,
                binding));
            return this;
        }

        private void AddBuiltIn(NodeManifest manifest)
        {
            _registrations.Add(new Registration(manifest, NodeManifestSource.BuiltIn, CreateReferenceBinding(manifest)));
        }

        private static NodeHandlerBindingContract CreateReferenceBinding(NodeManifest manifest)
        {
            return new NodeHandlerBindingContract(
                "aibt.reference." + manifest.TypeId.Substring("aibt.".Length),
                manifest.Version,
                manifest.ExecutionDomain);
        }

        // Project extension type IDs are project-qualified (not "aibt."-prefixed, per
        // ValidateSource's reserved-namespace check), so the binding's HandlerId is derived by
        // prefixing rather than substring-stripping "aibt." off the front.
        private static NodeHandlerBindingContract CreateProjectHandlerBinding(NodeManifest manifest)
        {
            return new NodeHandlerBindingContract(
                "aibt.reference.project." + manifest.TypeId,
                manifest.Version,
                manifest.ExecutionDomain);
        }

        private static void ValidateSource(Registration registration, ICollection<Diagnostic> diagnostics)
        {
            var typeId = registration.Manifest.TypeId;
            switch (registration.Source)
            {
                case NodeManifestSource.BuiltIn:
                    // aibt.core. is reserved for the original structural composites/decorators,
                    // whose execution is hardcoded directly into NativeLifecycleMachineV1
                    // (NativeLifecycleNodeKindV1) -- AIBT.CodeGen's BurstNodeGenerator permanently
                    // freezes that namespace against any [AibtCatalogSet]-declared shard (AIBT5012,
                    // discovered implementing P7-028). aibt.stdlib. is the always-on built-in
                    // namespace for leaf nodes that DO carry a real [AibtBurstNode] declaration.
                    if (!typeId.StartsWith("aibt.core.", StringComparison.Ordinal)
                        && !typeId.StartsWith("aibt.stdlib.", StringComparison.Ordinal))
                    {
                        diagnostics.Add(NodeRegistryDiagnostics.InvalidSource(typeId, "Built-in node IDs must use the aibt.core or aibt.stdlib namespace."));
                    }

                    break;
                case NodeManifestSource.TestFixture:
                    if (!typeId.StartsWith("aibt.test.", StringComparison.Ordinal))
                    {
                        diagnostics.Add(NodeRegistryDiagnostics.InvalidSource(typeId, "Test fixture node IDs must use the aibt.test namespace."));
                    }

                    break;
                case NodeManifestSource.UserExtension:
                    if (typeId.StartsWith("aibt.core.", StringComparison.Ordinal)
                        || typeId.StartsWith("aibt.test.", StringComparison.Ordinal)
                        || typeId.StartsWith("aibt.reference.", StringComparison.Ordinal))
                    {
                        diagnostics.Add(NodeRegistryDiagnostics.InvalidSource(typeId, "User extensions cannot use a reserved AIBT namespace."));
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static void ValidateBinding(Registration registration, ICollection<Diagnostic> diagnostics)
        {
            var binding = registration.HandlerBinding;

            // UserExtension is the only source where a binding is optional: AddUserExtension never
            // attaches one (unbound, as before P7-008 -- unchanged), while AddProjectExtension
            // (P7-008, ADR-P6-017) does attach one and expects it validated exactly like a
            // built-in/fixture binding below.
            if (registration.Source == NodeManifestSource.UserExtension && binding == null)
            {
                return;
            }

            if (binding == null)
            {
                diagnostics.Add(NodeRegistryDiagnostics.InvalidBinding(registration.Manifest.TypeId, "Built-in and fixture nodes require an explicit reference handler binding."));
                return;
            }

            if (binding.Version != registration.Manifest.Version || binding.ExecutionDomain != registration.Manifest.ExecutionDomain)
            {
                diagnostics.Add(NodeRegistryDiagnostics.InvalidBinding(registration.Manifest.TypeId, "Handler version and execution domain must match the manifest."));
            }
        }

        private static NodeRegistryCapabilityFlags ComputeCapabilities(IReadOnlyList<NodeRegistryEntry> entries)
        {
            var result = NodeRegistryCapabilityFlags.None;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                switch (entry.Manifest.ExecutionDomain)
                {
                    case NodeExecutionDomain.Burst:
                        result |= NodeRegistryCapabilityFlags.Burst;
                        break;
                    case NodeExecutionDomain.Managed:
                        result |= NodeRegistryCapabilityFlags.Managed;
                        break;
                    case NodeExecutionDomain.MainThread:
                        result |= NodeRegistryCapabilityFlags.MainThread;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (!entry.Manifest.Deterministic)
                {
                    result |= NodeRegistryCapabilityFlags.NonDeterministic;
                }

                if (entry.Manifest.SideEffects.Count != 0)
                {
                    result |= NodeRegistryCapabilityFlags.SideEffects;
                }

                if (entry.HasReferenceHandlerBinding)
                {
                    result |= NodeRegistryCapabilityFlags.ReferenceHandlerBindings;
                }

                if (entry.Source == NodeManifestSource.UserExtension)
                {
                    result |= NodeRegistryCapabilityFlags.UserExtensions;
                }
            }

            return result;
        }

        private sealed class Registration
        {
            internal Registration(NodeManifest manifest, NodeManifestSource source, NodeHandlerBindingContract handlerBinding)
            {
                Manifest = manifest;
                Source = source;
                HandlerBinding = handlerBinding;
            }

            internal NodeManifest Manifest { get; }

            internal NodeManifestSource Source { get; }

            internal NodeHandlerBindingContract HandlerBinding { get; }
        }

        private sealed class RegistrationComparer : IComparer<Registration>
        {
            internal static RegistrationComparer Instance { get; } = new RegistrationComparer();

            public int Compare(Registration left, Registration right)
            {
                var result = string.Compare(left.Manifest.TypeId, right.Manifest.TypeId, StringComparison.Ordinal);
                if (result != 0)
                {
                    return result;
                }

                result = left.Manifest.Version.CompareTo(right.Manifest.Version);
                if (result != 0)
                {
                    return result;
                }

                return left.Source.CompareTo(right.Source);
            }
        }
    }
}
