using System;
using System.Collections.Generic;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;

namespace AIBT.Authoring
{
    internal sealed class GeneratedBurstDispatchBindingPlanV2
    {
        internal GeneratedBurstDispatchBindingPlanV2(
            List<NativeBurstDispatchBindingV2> bindings,
            List<NativeBurstDispatchFieldV2> valueFields,
            List<NativeBurstDispatchCanonicalRangeV2> bindingRanges,
            List<NativeBurstDispatchCanonicalRuleV2> canonicalRules)
        {
            Bindings = bindings.AsReadOnly();
            ValueFields = valueFields.AsReadOnly();
            BindingRanges = bindingRanges.AsReadOnly();
            CanonicalRules = canonicalRules.AsReadOnly();
        }

        internal IReadOnlyList<NativeBurstDispatchBindingV2> Bindings { get; }
        internal IReadOnlyList<NativeBurstDispatchFieldV2> ValueFields { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRangeV2> BindingRanges { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRuleV2> CanonicalRules { get; }
    }

    internal sealed class GeneratedBurstDispatchNodePlanV2
    {
        internal GeneratedBurstDispatchNodePlanV2(
            List<NativeBurstDispatchFieldV2> configurationFields,
            List<NativeBurstDispatchFieldV2> memoryFields,
            List<NativeBurstDispatchCanonicalRangeV2> caseRanges,
            List<NativeBurstDispatchBindingV2> bindings,
            List<NativeBurstDispatchFieldV2> valueFields,
            List<NativeBurstDispatchCanonicalRangeV2> bindingRanges,
            List<NativeBurstDispatchCanonicalRuleV2> canonicalRules)
        {
            ConfigurationFields = configurationFields.AsReadOnly();
            MemoryFields = memoryFields.AsReadOnly();
            CaseRanges = caseRanges.AsReadOnly();
            Bindings = bindings.AsReadOnly();
            ValueFields = valueFields.AsReadOnly();
            BindingRanges = bindingRanges.AsReadOnly();
            CanonicalRules = canonicalRules.AsReadOnly();
        }

        internal IReadOnlyList<NativeBurstDispatchFieldV2> ConfigurationFields { get; }
        internal IReadOnlyList<NativeBurstDispatchFieldV2> MemoryFields { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRangeV2> CaseRanges { get; }
        internal IReadOnlyList<NativeBurstDispatchBindingV2> Bindings { get; }
        internal IReadOnlyList<NativeBurstDispatchFieldV2> ValueFields { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRangeV2> BindingRanges { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRuleV2> CanonicalRules { get; }
    }

    internal sealed class GeneratedBurstDispatchCatalogPlanV2
    {
        internal GeneratedBurstDispatchCatalogPlanV2(
            BurstCatalogHandshake handshake,
            List<NativeBurstDispatchCaseV2> cases,
            List<NativeBurstDispatchFieldV2> configurationFields,
            List<NativeBurstDispatchFieldV2> memoryFields,
            List<NativeBurstDispatchBindingV2> bindings,
            List<NativeBurstDispatchFieldV2> valueFields,
            List<NativeBurstDispatchCanonicalRangeV2> caseRanges,
            List<NativeBurstDispatchCanonicalRangeV2> bindingRanges,
            List<NativeBurstDispatchCanonicalRuleV2> canonicalRules,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            Handshake = handshake;
            Cases = cases.AsReadOnly();
            ConfigurationFields = configurationFields.AsReadOnly();
            MemoryFields = memoryFields.AsReadOnly();
            Bindings = bindings.AsReadOnly();
            ValueFields = valueFields.AsReadOnly();
            CaseRanges = caseRanges.AsReadOnly();
            BindingRanges = bindingRanges.AsReadOnly();
            CanonicalRules = canonicalRules.AsReadOnly();
            RegisteredTypes = registeredTypes;
        }

        internal BurstCatalogHandshake Handshake { get; }
        internal IReadOnlyList<NativeBurstDispatchCaseV2> Cases { get; }
        internal IReadOnlyList<NativeBurstDispatchFieldV2> ConfigurationFields { get; }
        internal IReadOnlyList<NativeBurstDispatchFieldV2> MemoryFields { get; }
        internal IReadOnlyList<NativeBurstDispatchBindingV2> Bindings { get; }
        internal IReadOnlyList<NativeBurstDispatchFieldV2> ValueFields { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRangeV2> CaseRanges { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRangeV2> BindingRanges { get; }
        internal IReadOnlyList<NativeBurstDispatchCanonicalRuleV2> CanonicalRules { get; }
        internal RegisteredBlackboardTypeCatalog RegisteredTypes { get; }
    }

    internal static class GeneratedBurstDispatchPrebindingV2
    {
        internal static GeneratedBurstDispatchCatalogPlanV2 CatalogPlan(
            string catalogId,
            uint catalogVersion,
            IReadOnlyList<GeneratedShardMetadataArtifact> shards)
        {
            if (!NodeTypeIdRules.IsValid(catalogId))
                throw new ArgumentException("Generated dispatch catalog ID is not canonical.", nameof(catalogId));
            if (catalogVersion == 0u) throw new ArgumentOutOfRangeException(nameof(catalogVersion));
            if (shards == null || shards.Count == 0)
                throw new ArgumentException("Generated dispatch catalog requires at least one shard.", nameof(shards));

            var orderedShards = new List<GeneratedShardMetadataArtifact>(shards);
            orderedShards.Sort(CompareShards);
            var shardIdentities = new HashSet<string>(StringComparer.Ordinal);
            var numericShardIdentities = new Dictionary<ulong, string>();
            var allDescriptors = new List<GeneratedNodeDescriptor>();
            var descriptorOwners = new Dictionary<string, GeneratedShardMetadataArtifact>(StringComparer.Ordinal);
            var registeredEntries = new Dictionary<string, RegisteredBlackboardTypeCatalogEntry>(StringComparer.Ordinal);
            for (var shardIndex = 0; shardIndex < orderedShards.Count; shardIndex++)
            {
                var shard = orderedShards[shardIndex];
                if (shard == null || !NodeTypeIdRules.IsValid(shard.ShardId) || shard.ShardVersion == 0u)
                    throw new InvalidOperationException("Generated dispatch shard lacks exact identity authority.");
                var shardIdentity = Identity(shard.ShardId, shard.ShardVersion);
                if (!shardIdentities.Add(shardIdentity))
                    throw new InvalidOperationException("Generated dispatch shard identities must be unique.");
                var numericShardId = StableHash.Fnv1A64(shard.ShardId);
                if (numericShardIdentities.TryGetValue(numericShardId, out var otherShardId)
                    && !string.Equals(otherShardId, shard.ShardId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Generated dispatch shard numeric identities collide.");
                numericShardIdentities[numericShardId] = shard.ShardId;

                for (var entryIndex = 0; entryIndex < shard.RegisteredTypes.Entries.Count; entryIndex++)
                {
                    var entry = shard.RegisteredTypes.Entries[entryIndex];
                    var identity = Identity(entry.CanonicalTypeId, entry.Version);
                    if (registeredEntries.TryGetValue(identity, out var existing))
                    {
                        if (!SameRegisteredEntry(existing, entry))
                            throw new InvalidOperationException("Generated dispatch shards disagree on registered value authority.");
                    }
                    else registeredEntries.Add(identity, entry);
                }

                for (var nodeIndex = 0; nodeIndex < shard.Nodes.Count; nodeIndex++)
                {
                    var descriptor = shard.Nodes[nodeIndex]
                        ?? throw new InvalidOperationException("Generated dispatch shard contains a null node descriptor.");
                    var identity = Identity(descriptor.Manifest.TypeId, descriptor.Manifest.Version);
                    if (descriptorOwners.ContainsKey(identity))
                        throw new InvalidOperationException("Generated dispatch node identities must be unique across shards.");
                    descriptorOwners.Add(identity, shard);
                    allDescriptors.Add(descriptor);
                }
            }

            allDescriptors.Sort(CompareDescriptors);
            var registeredTypes = new RegisteredBlackboardTypeCatalog(registeredEntries.Values);
            var registryHash = CatalogRegistryHash(allDescriptors);

            var nodePlans = new List<GeneratedBurstDispatchNodePlanV2>(allDescriptors.Count);
            for (var index = 0; index < allDescriptors.Count; index++)
                nodePlans.Add(NodePlan(allDescriptors[index], registeredTypes));

            var cases = new List<NativeBurstDispatchCaseV2>(allDescriptors.Count);
            var configurationFields = new List<NativeBurstDispatchFieldV2>();
            var memoryFields = new List<NativeBurstDispatchFieldV2>();
            var bindings = new List<NativeBurstDispatchBindingV2>();
            var valueFields = new List<NativeBurstDispatchFieldV2>();
            var caseRanges = new List<NativeBurstDispatchCanonicalRangeV2>(checked(allDescriptors.Count * 2));
            var bindingRanges = new List<NativeBurstDispatchCanonicalRangeV2>();
            var canonicalRules = new List<NativeBurstDispatchCanonicalRuleV2>();

            for (var nodeIndex = 0; nodeIndex < allDescriptors.Count; nodeIndex++)
            {
                var descriptor = allDescriptors[nodeIndex];
                var plan = nodePlans[nodeIndex];
                var firstConfigurationField = checked((uint)configurationFields.Count);
                var firstMemoryField = checked((uint)memoryFields.Count);
                var firstBinding = checked((uint)bindings.Count);
                var firstValueField = checked((uint)valueFields.Count);
                configurationFields.AddRange(plan.ConfigurationFields);
                memoryFields.AddRange(plan.MemoryFields);
                valueFields.AddRange(plan.ValueFields);

                for (var bindingIndex = 0; bindingIndex < plan.Bindings.Count; bindingIndex++)
                {
                    var binding = plan.Bindings[bindingIndex];
                    bindings.Add(new NativeBurstDispatchBindingV2(
                        binding.BindingOrdinal,
                        binding.ConfigurationFieldOrdinal,
                        binding.Kind,
                        binding.Scope,
                        binding.PhaseMask,
                        binding.PrimaryTypeNumericId,
                        binding.PrimaryTypeVersion,
                        checked(firstValueField + binding.FirstPrimaryValueField),
                        binding.PrimaryValueFieldCount,
                        binding.PrimaryValueSize,
                        binding.SecondaryTypeNumericId,
                        binding.SecondaryTypeVersion,
                        binding.SecondaryValueFieldCount == 0u
                            ? 0u
                            : checked(firstValueField + binding.FirstSecondaryValueField),
                        binding.SecondaryValueFieldCount,
                        binding.SecondaryValueSize));
                }

                for (var rangeIndex = 0; rangeIndex < plan.CaseRanges.Count; rangeIndex++)
                    AppendCanonicalRange(plan.CaseRanges[rangeIndex], plan.CanonicalRules, canonicalRules, caseRanges);

                var phases = (NativeBurstDispatchPhaseMaskV2)descriptor.CallbackCapabilities;
                cases.Add(new NativeBurstDispatchCaseV2(
                    StableHash.Fnv1A64(descriptor.Manifest.TypeId),
                    descriptor.Manifest.Version,
                    checked((uint)nodeIndex),
                    firstConfigurationField,
                    checked((uint)plan.ConfigurationFields.Count),
                    checked((uint)descriptor.Manifest.Configuration.Size),
                    firstMemoryField,
                    checked((uint)plan.MemoryFields.Count),
                    checked((uint)descriptor.Manifest.Memory.Size),
                    phases,
                    StatusMask(descriptor.Manifest),
                    descriptor.HasRandomStream,
                    firstBinding,
                    checked((uint)plan.Bindings.Count)));
            }

            for (var nodeIndex = 0; nodeIndex < nodePlans.Count; nodeIndex++)
            {
                var plan = nodePlans[nodeIndex];
                for (var rangeIndex = 0; rangeIndex < plan.BindingRanges.Count; rangeIndex++)
                    AppendCanonicalRange(plan.BindingRanges[rangeIndex], plan.CanonicalRules, canonicalRules, bindingRanges);
            }
            ValidateCanonicalPartition(caseRanges, bindingRanges, canonicalRules.Count);

            var configHash = CatalogLayoutHash(
                "AIBT-CATALOG-CONFIG-LAYOUT-V1\0", catalogId, catalogVersion,
                allDescriptors, value => value.ConfigurationLayoutHash);
            var memoryHash = CatalogLayoutHash(
                "AIBT-CATALOG-MEMORY-LAYOUT-V1\0", catalogId, catalogVersion,
                allDescriptors, value => value.MemoryLayoutHash);
            var accessHash = CatalogLayoutHash(
                "AIBT-CATALOG-ACCESS-LAYOUT-V1\0", catalogId, catalogVersion,
                allDescriptors, value => value.AccessLayoutHash);
            var catalogHash = CatalogHash(catalogId, catalogVersion, orderedShards, descriptorOwners);
            var handshake = new BurstCatalogHandshake(
                2u,
                new BurstCatalogFingerprint(ToBurstHash(catalogHash)),
                ToBurstHash(registryHash),
                1u,
                1u,
                ToBurstHash(configHash),
                ToBurstHash(memoryHash),
                ToBurstHash(accessHash));
            return new GeneratedBurstDispatchCatalogPlanV2(
                handshake, cases, configurationFields, memoryFields, bindings, valueFields,
                caseRanges, bindingRanges, canonicalRules, registeredTypes);
        }

        internal static GeneratedBurstDispatchBindingPlanV2 BindingPlan(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var nodePlan = NodePlan(descriptor, registeredTypes);
            var firstBindingRule = nodePlan.CaseRanges.Count == 0
                ? 0u
                : checked(nodePlan.CaseRanges[nodePlan.CaseRanges.Count - 1].FirstRule
                    + nodePlan.CaseRanges[nodePlan.CaseRanges.Count - 1].RuleCount);
            var bindingRanges = new List<NativeBurstDispatchCanonicalRangeV2>(nodePlan.BindingRanges.Count);
            for (var index = 0; index < nodePlan.BindingRanges.Count; index++)
            {
                var range = nodePlan.BindingRanges[index];
                if (range.FirstRule < firstBindingRule)
                    throw new InvalidOperationException("Generated dispatch binding canonical range precedes case authority.");
                bindingRanges.Add(new NativeBurstDispatchCanonicalRangeV2(
                    range.FirstRule - firstBindingRule,
                    range.RuleCount));
            }
            var canonicalRules = new List<NativeBurstDispatchCanonicalRuleV2>(
                checked(nodePlan.CanonicalRules.Count - (int)firstBindingRule));
            for (var index = checked((int)firstBindingRule); index < nodePlan.CanonicalRules.Count; index++)
                canonicalRules.Add(nodePlan.CanonicalRules[index]);
            ValidateCanonicalPartition(bindingRanges, canonicalRules.Count);
            return new GeneratedBurstDispatchBindingPlanV2(
                new List<NativeBurstDispatchBindingV2>(nodePlan.Bindings),
                new List<NativeBurstDispatchFieldV2>(nodePlan.ValueFields),
                bindingRanges,
                canonicalRules);
        }

        internal static GeneratedBurstDispatchNodePlanV2 NodePlan(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var handleFields = ValidateConfigurationAndHandles(descriptor, registeredTypes);
            ValidateMemory(descriptor, registeredTypes);
            if (handleFields.Count != descriptor.Bindings.Count)
                throw new InvalidOperationException("Generated dispatch bindings and configuration handles must match one-to-one.");

            var bindings = new List<NativeBurstDispatchBindingV2>(descriptor.Bindings.Count);
            var configurationFields = new List<NativeBurstDispatchFieldV2>();
            var memoryFields = new List<NativeBurstDispatchFieldV2>();
            var valueFields = new List<NativeBurstDispatchFieldV2>();
            var caseRanges = new List<NativeBurstDispatchCanonicalRangeV2>(2);
            var bindingRanges = new List<NativeBurstDispatchCanonicalRangeV2>(checked(descriptor.Bindings.Count * 2));
            var canonicalRules = new List<NativeBurstDispatchCanonicalRuleV2>();
            AppendFields(descriptor.Configuration, registeredTypes, configurationFields, canonicalRules, out var configurationRange);
            caseRanges.Add(configurationRange);
            AppendFields(descriptor.Memory, registeredTypes, memoryFields, canonicalRules, out var memoryRange);
            caseRanges.Add(memoryRange);

            string previousBindingId = null;
            for (var bindingIndex = 0; bindingIndex < descriptor.Bindings.Count; bindingIndex++)
            {
                var binding = descriptor.Bindings[bindingIndex];
                ValidateBindingShape(binding, checked((uint)bindingIndex), previousBindingId,
                    out var primaryType, out var secondaryType);
                previousBindingId = binding.BindingId;
                if (!handleFields.TryGetValue(binding.BindingId, out var configurationFieldOrdinal))
                    throw new InvalidOperationException("Generated dispatch binding lacks its exact configuration handle.");

                var primary = AppendValueLayout(primaryType, registeredTypes, valueFields, canonicalRules);
                bindingRanges.Add(primary.CanonicalRange);
                ValueLayout secondary = default;
                if (secondaryType != null)
                {
                    secondary = AppendValueLayout(secondaryType, registeredTypes, valueFields, canonicalRules);
                    bindingRanges.Add(secondary.CanonicalRange);
                }
                else
                {
                    bindingRanges.Add(new NativeBurstDispatchCanonicalRangeV2(
                        checked((uint)canonicalRules.Count), 0u));
                }

                bindings.Add(new NativeBurstDispatchBindingV2(
                    binding.Ordinal,
                    configurationFieldOrdinal,
                    (NativeBurstDispatchBindingKindV2)(byte)binding.Kind,
                    binding.IsBlackboard ? (byte)binding.Scope : NativeBurstDispatchBindingV2.NoScope,
                    (NativeBurstDispatchBindingPhaseMaskV2)(byte)binding.PhaseCapabilities,
                    primaryType.NumericTypeId,
                    primaryType.Version,
                    primary.FirstField,
                    primary.FieldCount,
                    primary.ValueSize,
                    secondaryType == null ? 0UL : secondaryType.NumericTypeId,
                    secondaryType == null ? 0u : secondaryType.Version,
                    secondaryType == null ? 0u : secondary.FirstField,
                    secondaryType == null ? 0u : secondary.FieldCount,
                        secondaryType == null ? 0u : secondary.ValueSize));
            }

            ValidateCanonicalPartition(caseRanges, bindingRanges, canonicalRules.Count);
            return new GeneratedBurstDispatchNodePlanV2(
                configurationFields,
                memoryFields,
                caseRanges,
                bindings,
                valueFields,
                bindingRanges,
                canonicalRules);
        }

        internal static IReadOnlyList<NativeBurstDispatchFieldV2> ConfigurationFields(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
            => BuildFields(descriptor, registeredTypes, true);

        internal static IReadOnlyList<NativeBurstDispatchFieldV2> MemoryFields(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
            => BuildFields(descriptor, registeredTypes, false);

        private static Dictionary<string, uint> ValidateConfigurationAndHandles(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var fields = descriptor.Configuration;
            var manifestFields = descriptor.Manifest.Configuration.Fields;
            if (manifestFields.Count != fields.Count)
                throw new InvalidOperationException("Generated dispatch configuration differs from its manifest projection.");

            var handles = new Dictionary<string, uint>(StringComparer.Ordinal);
            ulong cursor = 0;
            byte layoutAlignment = 1;
            string previousFieldId = null;
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field == null
                    || previousFieldId != null && Utf8OrdinalComparer.Instance.Compare(previousFieldId, field.FieldId) >= 0
                    || field.NumericFieldId == 0
                    || field.NumericFieldId != StableHash.Fnv1A64(field.FieldId)
                    || !GeneratedStorageField.ValidAlignment(field.Alignment))
                    throw new InvalidOperationException("Generated dispatch configuration is not in canonical field order.");
                previousFieldId = field.FieldId;

                cursor = Align(cursor, field.Alignment);
                if (cursor >= uint.MaxValue || cursor + field.Size > uint.MaxValue || field.Offset != (uint)cursor)
                    throw new InvalidOperationException("Generated dispatch configuration layout differs from canonical packing.");
                cursor += field.Size;
                if (field.Alignment > layoutAlignment) layoutAlignment = field.Alignment;

                var manifestField = manifestFields[index];
                if (manifestField == null
                    || manifestField.ParameterName != field.FieldId
                    || manifestField.Offset != field.Offset
                    || manifestField.Size != field.Size
                    || manifestField.Alignment != field.Alignment
                    || manifestField.IsGeneratedHandle != (field.Encoding == GeneratedFieldEncoding.GeneratedHandle))
                    throw new InvalidOperationException("Generated dispatch configuration differs from its manifest field layout.");

                if (field.Encoding == GeneratedFieldEncoding.GeneratedHandle)
                {
                    if (field.ValueTypeId != "GeneratedHandle"
                        || field.ValueTypeVersion != 1u
                        || field.Size != 4u
                        || field.Alignment != 4
                        || field.BindingId == null
                        || handles.ContainsKey(field.BindingId))
                        throw new InvalidOperationException("Generated dispatch configuration handle differs from the closed ABI.");
                    handles.Add(field.BindingId, checked((uint)index));
                }
                else
                {
                    if (field.BindingId != null)
                        throw new InvalidOperationException("Generated dispatch non-handle configuration cannot declare a binding.");
                    ValidateStorageFieldShape(field, registeredTypes);
                }
            }

            cursor = Align(cursor, layoutAlignment);
            if (cursor != descriptor.Manifest.Configuration.Size
                || layoutAlignment != descriptor.Manifest.Configuration.Alignment)
                throw new InvalidOperationException("Generated dispatch configuration total layout differs from its manifest.");
            return handles;
        }

        private static void ValidateMemory(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            var fields = descriptor.Memory;
            ulong cursor = 0;
            byte layoutAlignment = 1;
            string previousFieldId = null;
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field == null
                    || previousFieldId != null && Utf8OrdinalComparer.Instance.Compare(previousFieldId, field.FieldId) >= 0
                    || field.NumericFieldId == 0
                    || field.NumericFieldId != StableHash.Fnv1A64(field.FieldId)
                    || !GeneratedStorageField.ValidAlignment(field.Alignment)
                    || field.BindingId != null
                    || field.Encoding == GeneratedFieldEncoding.GeneratedHandle)
                    throw new InvalidOperationException("Generated dispatch memory is not in canonical field order.");
                previousFieldId = field.FieldId;

                cursor = Align(cursor, field.Alignment);
                if (cursor >= uint.MaxValue || cursor + field.Size > uint.MaxValue || field.Offset != (uint)cursor)
                    throw new InvalidOperationException("Generated dispatch memory layout differs from canonical packing.");
                cursor += field.Size;
                if (field.Alignment > layoutAlignment) layoutAlignment = field.Alignment;
                ValidateStorageFieldShape(field, registeredTypes);
            }

            cursor = Align(cursor, layoutAlignment);
            if (cursor != descriptor.Manifest.Memory.Size
                || layoutAlignment != descriptor.Manifest.Memory.Alignment)
                throw new InvalidOperationException("Generated dispatch memory total layout differs from its manifest.");
        }

        private static void ValidateBindingShape(
            GeneratedBindingDescriptor binding,
            uint expectedOrdinal,
            string previousBindingId,
            out GeneratedTypeRecord primaryType,
            out GeneratedTypeRecord secondaryType)
        {
            if (binding == null
                || binding.Ordinal != expectedOrdinal
                || binding.Ordinal == uint.MaxValue
                || previousBindingId != null && Utf8OrdinalComparer.Instance.Compare(previousBindingId, binding.BindingId) >= 0
                || binding.NumericBindingId == 0
                || binding.NumericBindingId != StableHash.Fnv1A64(binding.BindingId))
                throw new InvalidOperationException("Generated dispatch binding ordinals are not canonical.");

            GeneratedTypeRole primaryRole;
            GeneratedTypeRole? secondaryRole = null;
            switch (binding.Kind)
            {
                case GeneratedBindingKind.BlackboardRead:
                    if ((binding.Scope != BlackboardScope.Tree
                            && binding.Scope != BlackboardScope.Agent
                            && binding.Scope != BlackboardScope.Shared)
                        || binding.PhaseCapabilities != GeneratedPhaseCapability.None)
                        throw new InvalidOperationException("Generated dispatch blackboard-read binding shape differs from the closed ABI.");
                    primaryRole = GeneratedTypeRole.Value;
                    break;
                case GeneratedBindingKind.BlackboardWrite:
                case GeneratedBindingKind.BlackboardReadWrite:
                    if ((binding.Scope != BlackboardScope.Tree && binding.Scope != BlackboardScope.Agent)
                        || binding.PhaseCapabilities != GeneratedPhaseCapability.None)
                        throw new InvalidOperationException("Generated dispatch blackboard-write binding shape differs from the closed ABI.");
                    primaryRole = GeneratedTypeRole.Value;
                    break;
                case GeneratedBindingKind.SnapshotRead:
                    RequireNonBlackboardShape(binding, GeneratedPhaseCapability.None);
                    primaryRole = GeneratedTypeRole.Value;
                    break;
                case GeneratedBindingKind.EffectCommand:
                    RequireNonBlackboardShape(binding, GeneratedPhaseCapability.Execute);
                    primaryRole = GeneratedTypeRole.EffectPayload;
                    break;
                case GeneratedBindingKind.AsyncOperation:
                    RequireNonBlackboardShape(binding,
                        GeneratedPhaseCapability.Execute | GeneratedPhaseCapability.Cancel);
                    primaryRole = GeneratedTypeRole.AsyncStartPayload;
                    secondaryRole = GeneratedTypeRole.AsyncCancelPayload;
                    break;
                case GeneratedBindingKind.Completion:
                    RequireNonBlackboardShape(binding, GeneratedPhaseCapability.Completion);
                    primaryRole = GeneratedTypeRole.CompletionPayload;
                    break;
                default:
                    throw new InvalidOperationException("Generated dispatch binding kind is outside the closed ABI.");
            }

            var expectedTypeCount = secondaryRole.HasValue ? 2 : 1;
            if (binding.Types.Count != expectedTypeCount
                || binding.Types[0] == null
                || binding.Types[0].Role != primaryRole
                || secondaryRole.HasValue && (binding.Types[1] == null || binding.Types[1].Role != secondaryRole.Value))
                throw new InvalidOperationException("Generated dispatch binding type roles differ from the closed ABI.");
            primaryType = binding.Types[0];
            secondaryType = secondaryRole.HasValue ? binding.Types[1] : null;
            ValidateTypeIdentity(primaryType);
            if (secondaryType != null) ValidateTypeIdentity(secondaryType);
        }

        private static void RequireNonBlackboardShape(
            GeneratedBindingDescriptor binding,
            GeneratedPhaseCapability expectedPhase)
        {
            if ((byte)binding.Scope != byte.MaxValue || binding.PhaseCapabilities != expectedPhase)
                throw new InvalidOperationException("Generated dispatch non-blackboard binding shape differs from the closed ABI.");
        }

        private static void ValidateTypeIdentity(GeneratedTypeRecord type)
        {
            if (type == null
                || type.NumericTypeId == 0
                || type.NumericTypeId != StableHash.Fnv1A64(type.CanonicalTypeId)
                || type.Version == 0)
                throw new InvalidOperationException("Generated dispatch binding type identity is invalid.");
        }

        private static ValueLayout AppendValueLayout(
            GeneratedTypeRecord type,
            RegisteredBlackboardTypeCatalog registeredTypes,
            IList<NativeBurstDispatchFieldV2> valueFields,
            IList<NativeBurstDispatchCanonicalRuleV2> canonicalRules)
        {
            var firstField = checked((uint)valueFields.Count);
            var firstRule = checked((uint)canonicalRules.Count);
            uint valueSize;
            if (type.RegisteredDescriptor.IsValid)
            {
                var entry = ExactRegisteredEntry(
                    type.CanonicalTypeId,
                    type.Version,
                    type.SchemaHash,
                    type.RegisteredDescriptor,
                    registeredTypes);
                ValidateRegisteredEntryLayout(entry, registeredTypes);
                valueSize = checked((uint)entry.Descriptor.Size);
                var visiting = new HashSet<string>(StringComparer.Ordinal);
                var identity = RegisteredIdentity(type.CanonicalTypeId, type.Version);
                if (!visiting.Add(identity))
                    throw new InvalidOperationException("Generated dispatch registered value contains a recursive type cycle.");
                try
                {
                    for (var fieldIndex = 0; fieldIndex < entry.Fields.Count; fieldIndex++)
                        AppendValueField(entry.Fields[fieldIndex], checked((uint)fieldIndex), registeredTypes,
                            visiting, valueFields, canonicalRules);
                }
                finally
                {
                    visiting.Remove(identity);
                }
            }
            else
            {
                if (!string.Equals(type.SchemaHash, GeneratedNodeMetadata.ZeroHash, StringComparison.Ordinal)
                    || !GeneratedTypeLayoutRules.TryBuiltIn(type.CanonicalTypeId,
                        out valueSize, out var alignment, out var encoding)
                    || type.Version != 1u)
                    throw new InvalidOperationException("Generated dispatch binding type is outside the closed built-in ABI.");
                var field = new GeneratedStorageField(
                    "value", type.CanonicalTypeId, type.Version, valueSize, alignment, encoding);
                AppendValueField(field, 0u, registeredTypes,
                    new HashSet<string>(StringComparer.Ordinal), valueFields, canonicalRules);
            }

            var fieldCount = checked((uint)valueFields.Count - firstField);
            if (fieldCount == 0 || valueSize == 0)
                throw new InvalidOperationException("Generated dispatch binding values require at least one transport field.");
            return new ValueLayout(
                firstField,
                fieldCount,
                valueSize,
                new NativeBurstDispatchCanonicalRangeV2(
                    firstRule,
                    checked((uint)canonicalRules.Count - firstRule)));
        }

        private static void AppendValueField(
            GeneratedStorageField field,
            uint fieldOrdinal,
            RegisteredBlackboardTypeCatalog registeredTypes,
            HashSet<string> visiting,
            IList<NativeBurstDispatchFieldV2> valueFields,
            IList<NativeBurstDispatchCanonicalRuleV2> canonicalRules)
        {
            var leaves = new List<Leaf>();
            Flatten(field, field.Offset, registeredTypes, visiting, leaves, canonicalRules);
            if (leaves.Count == 0)
                throw new InvalidOperationException("Generated dispatch binding value fields must contain a transport leaf.");
            var fieldEnd = checked(field.Offset + field.Size);
            for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
            {
                var leaf = leaves[leafIndex];
                if (leaf.ByteOffset < field.Offset || checked(leaf.ByteOffset + leaf.ElementSize) > fieldEnd)
                    throw new InvalidOperationException("Generated dispatch binding value leaf is outside its canonical field.");
                AppendRun(valueFields, fieldOrdinal, checked((uint)leafIndex), leaf);
            }
        }

        private static void ValidateCanonicalPartition(
            IList<NativeBurstDispatchCanonicalRangeV2> ranges,
            int ruleCount)
        {
            var cursor = ValidateCanonicalRanges(ranges, 0u);
            if (cursor != checked((uint)ruleCount))
                throw new InvalidOperationException("Generated dispatch canonical binding ranges do not cover their rule table.");
        }

        private static void ValidateCanonicalPartition(
            IList<NativeBurstDispatchCanonicalRangeV2> caseRanges,
            IList<NativeBurstDispatchCanonicalRangeV2> bindingRanges,
            int ruleCount)
        {
            var cursor = ValidateCanonicalRanges(caseRanges, 0u);
            cursor = ValidateCanonicalRanges(bindingRanges, cursor);
            if (cursor != checked((uint)ruleCount))
                throw new InvalidOperationException("Generated dispatch canonical ranges do not cover their rule table.");
        }

        private static uint ValidateCanonicalRanges(
            IList<NativeBurstDispatchCanonicalRangeV2> ranges,
            uint cursor)
        {
            for (var index = 0; index < ranges.Count; index++)
            {
                var range = ranges[index];
                if (range.FirstRule != cursor)
                    throw new InvalidOperationException("Generated dispatch canonical ranges are not an exact partition.");
                cursor = checked(cursor + range.RuleCount);
            }
            return cursor;
        }

        private static RegisteredBlackboardTypeCatalogEntry ExactRegisteredEntry(
            string typeId,
            uint version,
            string schemaHash,
            RegisteredUnmanagedTypeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            if (registeredTypes == null
                || !registeredTypes.TryGet(typeId, version, out var entry)
                || entry == null
                || entry.CanonicalTypeId != typeId
                || entry.Version != version
                || entry.Descriptor != descriptor
                || !string.Equals(entry.SchemaHash, schemaHash, StringComparison.Ordinal)
                || entry.Descriptor.TypeId != StableHash.Fnv1A64(entry.CanonicalTypeId)
                || entry.Descriptor.Version != entry.Version
                || entry.Descriptor.Size <= 0
                || entry.Descriptor.Size > int.MaxValue
                || !GeneratedStorageField.ValidAlignment(checked((byte)entry.Descriptor.Alignment))
                || entry.Descriptor.EqualityContractId != GeneratedNodeMetadata.CanonicalBytesEqualityContractId
                || entry.Descriptor.CanonicalSchemaId != StableHash.Fnv1A64(entry.CanonicalSchemaId)
                || !entry.Descriptor.HasCanonicalSchema
                || entry.Descriptor.HasMigration
                || !GeneratedTypeRecordHash.IsHash(entry.SchemaHash))
                throw new InvalidOperationException("Generated dispatch registered value is absent from the exact accepted catalog.");
            return entry;
        }

        private static void ValidateRegisteredEntryLayout(
            RegisteredBlackboardTypeCatalogEntry entry,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            ulong cursor = 0;
            byte layoutAlignment = 1;
            string previousFieldId = null;
            for (var index = 0; index < entry.Fields.Count; index++)
            {
                var field = entry.Fields[index];
                if (field == null
                    || previousFieldId != null && Utf8OrdinalComparer.Instance.Compare(previousFieldId, field.FieldId) >= 0
                    || field.NumericFieldId == 0
                    || field.NumericFieldId != StableHash.Fnv1A64(field.FieldId)
                    || !GeneratedStorageField.ValidAlignment(field.Alignment)
                    || field.BindingId != null
                    || field.Encoding == GeneratedFieldEncoding.GeneratedHandle)
                    throw new InvalidOperationException("Generated dispatch registered fields are not canonical.");
                previousFieldId = field.FieldId;

                cursor = Align(cursor, field.Alignment);
                if (cursor >= uint.MaxValue || cursor + field.Size > uint.MaxValue || field.Offset != (uint)cursor)
                    throw new InvalidOperationException("Generated dispatch registered layout differs from canonical packing.");
                cursor += field.Size;
                if (field.Alignment > layoutAlignment) layoutAlignment = field.Alignment;
                ValidateStorageFieldShape(field, registeredTypes);
            }

            cursor = Align(cursor, layoutAlignment);
            if (cursor != checked((uint)entry.Descriptor.Size)
                || layoutAlignment != entry.Descriptor.Alignment)
                throw new InvalidOperationException("Generated dispatch registered total layout differs from its accepted descriptor.");
        }

        private static void ValidateStorageFieldShape(
            GeneratedStorageField field,
            RegisteredBlackboardTypeCatalog registeredTypes)
        {
            if (field.Encoding == GeneratedFieldEncoding.Registered)
            {
                var nested = ExactRegisteredEntry(
                    field.ValueTypeId,
                    field.ValueTypeVersion,
                    field.RegisteredSchemaHash,
                    field.RegisteredDescriptor,
                    registeredTypes);
                if (field.Size != nested.Descriptor.Size || field.Alignment != nested.Descriptor.Alignment)
                    throw new InvalidOperationException("Generated dispatch nested registered layout differs from its accepted descriptor.");
                return;
            }

            if (!GeneratedTypeLayoutRules.TryBuiltIn(
                    field.ValueTypeId,
                    out var expectedSize,
                    out var expectedAlignment,
                    out var expectedEncoding)
                || field.ValueTypeVersion != 1u
                || field.Size != expectedSize
                || field.Alignment != expectedAlignment
                || field.Encoding != expectedEncoding
                || field.RegisteredDescriptor.IsValid
                || !string.Equals(field.RegisteredSchemaHash, GeneratedNodeMetadata.ZeroHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Generated dispatch storage field differs from the closed built-in ABI.");
        }

        private static int CompareShards(
            GeneratedShardMetadataArtifact left,
            GeneratedShardMetadataArtifact right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            var comparison = Utf8OrdinalComparer.Instance.Compare(left.ShardId, right.ShardId);
            return comparison != 0 ? comparison : left.ShardVersion.CompareTo(right.ShardVersion);
        }

        private static int CompareDescriptors(GeneratedNodeDescriptor left, GeneratedNodeDescriptor right)
        {
            var comparison = Utf8OrdinalComparer.Instance.Compare(left.Manifest.TypeId, right.Manifest.TypeId);
            return comparison != 0 ? comparison : left.Manifest.Version.CompareTo(right.Manifest.Version);
        }

        private static void AppendCanonicalRange(
            NativeBurstDispatchCanonicalRangeV2 source,
            IReadOnlyList<NativeBurstDispatchCanonicalRuleV2> sourceRules,
            List<NativeBurstDispatchCanonicalRuleV2> targetRules,
            List<NativeBurstDispatchCanonicalRangeV2> targetRanges)
        {
            var first = checked((uint)targetRules.Count);
            if (source.FirstRule > sourceRules.Count
                || source.RuleCount > sourceRules.Count - source.FirstRule)
                throw new InvalidOperationException("Generated dispatch canonical range exceeds its node rule stream.");
            for (uint index = 0; index < source.RuleCount; index++)
                targetRules.Add(sourceRules[checked((int)(source.FirstRule + index))]);
            targetRanges.Add(new NativeBurstDispatchCanonicalRangeV2(first, source.RuleCount));
        }

        private static BurstNodeStatusMask StatusMask(NodeManifest manifest)
        {
            var result = BurstNodeStatusMask.None;
            for (var index = 0; index < manifest.PossibleStatuses.Count; index++)
            {
                switch (manifest.PossibleStatuses[index])
                {
                    case NodeStatus.Success: result |= BurstNodeStatusMask.Success; break;
                    case NodeStatus.Failure: result |= BurstNodeStatusMask.Failure; break;
                    case NodeStatus.Running: result |= BurstNodeStatusMask.Running; break;
                    default: throw new InvalidOperationException("Generated dispatch node status is outside the closed ABI.");
                }
            }
            if (result == BurstNodeStatusMask.None)
                throw new InvalidOperationException("Generated dispatch node must declare at least one terminal or running status.");
            return result;
        }

        private static CompiledHash CatalogLayoutHash(
            string domain,
            string catalogId,
            uint catalogVersion,
            IReadOnlyList<GeneratedNodeDescriptor> descriptors,
            Func<GeneratedNodeDescriptor, CompiledHash> select)
        {
            var writer = new GeneratedByteWriter(domain);
            writer.U32(1u);
            writer.String(catalogId);
            writer.U32(catalogVersion);
            writer.U32(checked((uint)descriptors.Count));
            for (var index = 0; index < descriptors.Count; index++)
            {
                var descriptor = descriptors[index];
                writer.String(descriptor.Manifest.TypeId);
                writer.U32(descriptor.Manifest.Version);
                writer.Hash(select(descriptor).HexadecimalValue);
            }
            return writer.Finish();
        }

        private static CompiledHash CatalogHash(
            string catalogId,
            uint catalogVersion,
            IReadOnlyList<GeneratedShardMetadataArtifact> shards,
            IReadOnlyDictionary<string, GeneratedShardMetadataArtifact> descriptorOwners)
        {
            var writer = new GeneratedByteWriter("AIBT-CATALOG-V2\0");
            writer.U32(2u);
            writer.String(catalogId);
            writer.U32(catalogVersion);
            writer.U32(checked((uint)shards.Count));
            for (var shardIndex = 0; shardIndex < shards.Count; shardIndex++)
            {
                var shard = shards[shardIndex];
                writer.String(shard.ShardId);
                writer.U32(shard.ShardVersion);
                var nodes = new List<GeneratedNodeDescriptor>();
                for (var nodeIndex = 0; nodeIndex < shard.Nodes.Count; nodeIndex++)
                {
                    var descriptor = shard.Nodes[nodeIndex];
                    var identity = Identity(descriptor.Manifest.TypeId, descriptor.Manifest.Version);
                    if (!descriptorOwners.TryGetValue(identity, out var owner) || !ReferenceEquals(owner, shard))
                        throw new InvalidOperationException("Generated dispatch shard ownership is inconsistent.");
                    nodes.Add(descriptor);
                }
                nodes.Sort(CompareDescriptors);
                writer.U32(checked((uint)nodes.Count));
                for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    var descriptor = nodes[nodeIndex];
                    var manifest = descriptor.Manifest;
                    writer.String(manifest.TypeId);
                    writer.U64(StableHash.Fnv1A64(manifest.TypeId));
                    writer.U32(manifest.Version);
                    writer.U8(NodeKind(manifest.Kind));
                    writer.U8(manifest.Deterministic ? (byte)1 : (byte)0);
                    writer.U8((byte)manifest.Cancellation);
                    writer.U8((byte)manifest.CostHint);
                    writer.U8((byte)StatusMask(manifest));
                    writer.U8((byte)manifest.Memory.Lifetime);
                    writer.U8(descriptor.CallbackCapabilities);
                    writer.U8(descriptor.HasRandomStream ? (byte)1 : (byte)0);
                    writer.Hash(descriptor.ConfigurationLayoutHash.HexadecimalValue);
                    writer.Hash(descriptor.MemoryLayoutHash.HexadecimalValue);
                    writer.Hash(descriptor.AccessLayoutHash.HexadecimalValue);
                }
            }
            return writer.Finish();
        }

        private static byte NodeKind(NodeBehaviorKind kind)
        {
            if (kind == NodeBehaviorKind.Condition) return 0;
            if (kind == NodeBehaviorKind.Action) return 1;
            throw new InvalidOperationException("Generated dispatch catalogs can contain only condition and action leaves.");
        }

        private static BurstHash256 ToBurstHash(CompiledHash hash)
        {
            if (!hash.IsValid) throw new InvalidOperationException("Generated dispatch hash is invalid.");
            var bytes = new byte[32];
            for (var index = 0; index < bytes.Length; index++)
            {
                var offset = index * 2;
                bytes[index] = (byte)((HexNibble(hash.HexadecimalValue[offset]) << 4)
                    | HexNibble(hash.HexadecimalValue[offset + 1]));
            }
            var words = new uint[8];
            for (var index = 0; index < words.Length; index++)
            {
                var offset = index * 4;
                words[index] = (uint)(bytes[offset]
                    | bytes[offset + 1] << 8
                    | bytes[offset + 2] << 16
                    | bytes[offset + 3] << 24);
            }
            return new BurstHash256(
                words[0], words[1], words[2], words[3],
                words[4], words[5], words[6], words[7]);
        }

        private static CompiledHash CatalogRegistryHash(IReadOnlyList<GeneratedNodeDescriptor> descriptors)
        {
            var generated = GeneratedNodeRegistry.Build(descriptors, includeBuiltIns: false);
            if (!generated.Success)
                throw new InvalidOperationException("Generated dispatch catalog node registry is invalid.");

            var entries = new List<NodeRegistryEntry>(
                RuntimeBuiltInCatalogAuthorityVerifier.RebuildAuthorityEntries());
            var canonicalIds = new HashSet<string>(StringComparer.Ordinal);
            var numericIds = new Dictionary<ulong, string>();
            for (var index = 0; index < entries.Count; index++)
            {
                canonicalIds.Add(entries[index].Manifest.TypeId);
                numericIds.Add(entries[index].NumericTypeId, entries[index].Manifest.TypeId);
            }

            foreach (var entry in generated.Registry)
            {
                var typeId = entry.Manifest.TypeId;
                if (typeId.StartsWith("aibt.core.", StringComparison.Ordinal)
                    || !canonicalIds.Add(typeId)
                    || (numericIds.TryGetValue(entry.NumericTypeId, out var existing)
                        && !string.Equals(existing, typeId, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Generated dispatch catalog collides with the runtime core authority.");
                numericIds[entry.NumericTypeId] = typeId;
                entries.Add(entry);
            }

            entries.Sort((left, right) =>
            {
                var comparison = Utf8OrdinalComparer.Instance.Compare(
                    left.Manifest.TypeId,
                    right.Manifest.TypeId);
                return comparison != 0
                    ? comparison
                    : left.Manifest.Version.CompareTo(right.Manifest.Version);
            });
            return new CompiledHash(StableHash.Sha256Hex(
                NodeManifestCanonicalJson.SerializeRegistryUtf8(entries.ToArray())));
        }

        private static int HexNibble(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            throw new InvalidOperationException("Generated dispatch hash is not lowercase hexadecimal.");
        }

        private static bool SameRegisteredEntry(
            RegisteredBlackboardTypeCatalogEntry left,
            RegisteredBlackboardTypeCatalogEntry right)
        {
            if (left.CanonicalTypeId != right.CanonicalTypeId
                || left.Version != right.Version
                || left.CanonicalSchemaId != right.CanonicalSchemaId
                || left.SchemaHash != right.SchemaHash
                || left.Descriptor != right.Descriptor
                || left.Fields.Count != right.Fields.Count)
                return false;
            for (var index = 0; index < left.Fields.Count; index++)
            {
                var first = left.Fields[index];
                var second = right.Fields[index];
                if (first.FieldId != second.FieldId
                    || first.ValueTypeId != second.ValueTypeId
                    || first.ValueTypeVersion != second.ValueTypeVersion
                    || first.Size != second.Size
                    || first.Alignment != second.Alignment
                    || first.Encoding != second.Encoding
                    || first.RegisteredSchemaHash != second.RegisteredSchemaHash
                    || first.RegisteredDescriptor != second.RegisteredDescriptor
                    || first.Offset != second.Offset)
                    return false;
            }
            return true;
        }

        private static string RegisteredIdentity(string typeId, uint version)
            => typeId + "\0" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Identity(string id, uint version)
            => id + "\0" + version.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static ulong Align(ulong value, byte alignment)
        {
            if (!GeneratedStorageField.ValidAlignment(alignment))
                throw new InvalidOperationException("Generated dispatch alignment is outside the closed ABI.");
            var mask = (ulong)alignment - 1UL;
            return checked((value + mask) & ~mask);
        }

        private static IReadOnlyList<NativeBurstDispatchFieldV2> BuildFields(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog registeredTypes,
            bool configuration)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var fields = configuration ? descriptor.Configuration : descriptor.Memory;
            var result = new List<NativeBurstDispatchFieldV2>();
            AppendFields(fields, registeredTypes, result, null, out _);
            return result.AsReadOnly();
        }

        private static void AppendFields(
            IReadOnlyList<GeneratedStorageField> fields,
            RegisteredBlackboardTypeCatalog registeredTypes,
            IList<NativeBurstDispatchFieldV2> result,
            IList<NativeBurstDispatchCanonicalRuleV2> canonicalRules,
            out NativeBurstDispatchCanonicalRangeV2 canonicalRange)
        {
            var firstRule = canonicalRules == null ? 0u : checked((uint)canonicalRules.Count);
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var leaves = new List<Leaf>();
                Flatten(field, field.Offset, registeredTypes, new HashSet<string>(StringComparer.Ordinal), leaves, canonicalRules);
                if (leaves.Count == 0)
                    throw new InvalidOperationException("Generated dispatch storage fields must contain at least one transport leaf.");

                var fieldEnd = checked(field.Offset + field.Size);
                for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
                {
                    var leaf = leaves[leafIndex];
                    if (leaf.ByteOffset < field.Offset || checked(leaf.ByteOffset + leaf.ElementSize) > fieldEnd)
                        throw new InvalidOperationException("Generated dispatch transport leaf is outside its storage field.");
                    AppendRun(result, (uint)fieldIndex, (uint)leafIndex, leaf);
                }
            }
            canonicalRange = new NativeBurstDispatchCanonicalRangeV2(
                firstRule,
                canonicalRules == null ? 0u : checked((uint)canonicalRules.Count - firstRule));
        }

        private static void Flatten(
            GeneratedStorageField field,
            uint byteOffset,
            RegisteredBlackboardTypeCatalog registeredTypes,
            HashSet<string> visiting,
            IList<Leaf> result,
            IList<NativeBurstDispatchCanonicalRuleV2> canonicalRules = null)
        {
            if (field == null) throw new InvalidOperationException("Generated dispatch storage fields cannot be null.");
            switch (field.Encoding)
            {
                case GeneratedFieldEncoding.Bool8:
                case GeneratedFieldEncoding.Int8:
                case GeneratedFieldEncoding.UInt8:
                case GeneratedFieldEncoding.Int16LE:
                case GeneratedFieldEncoding.UInt16LE:
                case GeneratedFieldEncoding.Int32LE:
                case GeneratedFieldEncoding.UInt32LE:
                case GeneratedFieldEncoding.Int64LE:
                case GeneratedFieldEncoding.UInt64LE:
                case GeneratedFieldEncoding.Float32BitsLE:
                case GeneratedFieldEncoding.Float64BitsLE:
                    AddScalar(result, byteOffset, PrimitiveSize(field.Encoding),
                        (NativeBurstDispatchFieldEncodingV2)(byte)field.Encoding);
                    return;
                case GeneratedFieldEncoding.GeneratedHandle:
                    AddScalar(result, byteOffset, 4u, NativeBurstDispatchFieldEncodingV2.GeneratedHandle);
                    return;
                case GeneratedFieldEncoding.FixedBytes:
                    var firstLeaf = result.Count;
                    FlattenClosedValue(field, byteOffset, result);
                    var canonicalKind = CanonicalRuleKind(field.ValueTypeId);
                    if (canonicalKind != NativeBurstDispatchCanonicalRuleKindV2.None)
                    {
                        if (result.Count == firstLeaf)
                            throw new InvalidOperationException("Generated dispatch canonical value lacks its authoritative first leaf.");
                        result[firstLeaf] = result[firstLeaf].WithCanonicalRule(canonicalKind);
                        canonicalRules?.Add(new NativeBurstDispatchCanonicalRuleV2(canonicalKind, byteOffset));
                    }
                    return;
                case GeneratedFieldEncoding.Registered:
                    FlattenRegistered(field, byteOffset, registeredTypes, visiting, result, canonicalRules);
                    return;
                default:
                    throw new InvalidOperationException("Generated dispatch storage encoding is outside the closed ABI.");
            }
        }

        private static void FlattenClosedValue(GeneratedStorageField field, uint byteOffset, IList<Leaf> result)
        {
            switch (field.ValueTypeId)
            {
                case "Float2":
                    RequireClosedShape(field, 8u);
                    AddRepeated(result, byteOffset, 2u, 4u, NativeBurstDispatchFieldEncodingV2.Float32);
                    return;
                case "Float3":
                    RequireClosedShape(field, 12u);
                    AddRepeated(result, byteOffset, 3u, 4u, NativeBurstDispatchFieldEncodingV2.Float32);
                    return;
                case "Quaternion":
                    RequireClosedShape(field, 16u);
                    AddRepeated(result, byteOffset, 4u, 4u, NativeBurstDispatchFieldEncodingV2.Float32);
                    return;
                case "AgentId":
                case "EntityId":
                    RequireClosedShape(field, 8u);
                    AddScalar(result, byteOffset, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                    return;
                case "OperationId":
                    RequireClosedShape(field, 24u);
                    AddScalar(result, byteOffset, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                    AddScalar(result, checked(byteOffset + 8u), 4u, NativeBurstDispatchFieldEncodingV2.UInt32);
                    AddScalar(result, checked(byteOffset + 12u), 4u, NativeBurstDispatchFieldEncodingV2.UInt32);
                    AddScalar(result, checked(byteOffset + 16u), 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                    return;
                case "AssetId":
                    RequireClosedShape(field, 32u);
                    AddScalar(result, byteOffset, 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                    AddScalar(result, checked(byteOffset + 8u), 8u, NativeBurstDispatchFieldEncodingV2.UInt64);
                    AddScalar(result, checked(byteOffset + 16u), 8u, NativeBurstDispatchFieldEncodingV2.Int64);
                    AddScalar(result, checked(byteOffset + 24u), 1u, NativeBurstDispatchFieldEncodingV2.Boolean);
                    return;
                case "FixedString32":
                    FlattenFixedString(field, byteOffset, 32u, result);
                    return;
                case "FixedString64":
                    FlattenFixedString(field, byteOffset, 64u, result);
                    return;
                case "FixedString128":
                    FlattenFixedString(field, byteOffset, 128u, result);
                    return;
                case "FixedString512":
                    FlattenFixedString(field, byteOffset, 512u, result);
                    return;
                default:
                    throw new InvalidOperationException("Generated dispatch fixed storage type is outside the closed ABI.");
            }
        }

        private static void FlattenRegistered(
            GeneratedStorageField field,
            uint byteOffset,
            RegisteredBlackboardTypeCatalog registeredTypes,
            HashSet<string> visiting,
            IList<Leaf> result,
            IList<NativeBurstDispatchCanonicalRuleV2> canonicalRules)
        {
            var entry = ExactRegisteredEntry(
                field.ValueTypeId,
                field.ValueTypeVersion,
                field.RegisteredSchemaHash,
                field.RegisteredDescriptor,
                registeredTypes);
            ValidateRegisteredEntryLayout(entry, registeredTypes);

            var identity = RegisteredIdentity(field.ValueTypeId, field.ValueTypeVersion);
            if (!visiting.Add(identity))
                throw new InvalidOperationException("Generated dispatch registered storage contains a recursive type cycle.");
            try
            {
                for (var index = 0; index < entry.Fields.Count; index++)
                {
                    var nested = entry.Fields[index];
                    Flatten(nested, checked(byteOffset + nested.Offset), registeredTypes,
                        visiting, result, canonicalRules);
                }
            }
            finally
            {
                visiting.Remove(identity);
            }
        }

        private static NativeBurstDispatchCanonicalRuleKindV2 CanonicalRuleKind(string valueTypeId)
        {
            switch (valueTypeId)
            {
                case "AgentId": return NativeBurstDispatchCanonicalRuleKindV2.AgentId;
                case "EntityId": return NativeBurstDispatchCanonicalRuleKindV2.EntityId;
                case "OperationId": return NativeBurstDispatchCanonicalRuleKindV2.OperationId;
                case "AssetId": return NativeBurstDispatchCanonicalRuleKindV2.AssetId;
                case "FixedString32": return NativeBurstDispatchCanonicalRuleKindV2.FixedString32;
                case "FixedString64": return NativeBurstDispatchCanonicalRuleKindV2.FixedString64;
                case "FixedString128": return NativeBurstDispatchCanonicalRuleKindV2.FixedString128;
                case "FixedString512": return NativeBurstDispatchCanonicalRuleKindV2.FixedString512;
                default: return NativeBurstDispatchCanonicalRuleKindV2.None;
            }
        }

        private static void FlattenFixedString(GeneratedStorageField field, uint byteOffset, uint size, IList<Leaf> result)
        {
            RequireClosedShape(field, size);
            AddScalar(result, byteOffset, 2u, NativeBurstDispatchFieldEncodingV2.UInt16);
            AddRepeated(result, checked(byteOffset + 2u), size - 2u, 1u, NativeBurstDispatchFieldEncodingV2.UInt8);
        }

        private static void RequireClosedShape(GeneratedStorageField field, uint size)
        {
            if (field.ValueTypeVersion != 1u || field.Size != size)
                throw new InvalidOperationException("Generated dispatch fixed storage layout differs from the closed ABI.");
        }

        private static uint PrimitiveSize(GeneratedFieldEncoding encoding)
        {
            switch (encoding)
            {
                case GeneratedFieldEncoding.Bool8:
                case GeneratedFieldEncoding.Int8:
                case GeneratedFieldEncoding.UInt8: return 1u;
                case GeneratedFieldEncoding.Int16LE:
                case GeneratedFieldEncoding.UInt16LE: return 2u;
                case GeneratedFieldEncoding.Int32LE:
                case GeneratedFieldEncoding.UInt32LE:
                case GeneratedFieldEncoding.Float32BitsLE: return 4u;
                case GeneratedFieldEncoding.Int64LE:
                case GeneratedFieldEncoding.UInt64LE:
                case GeneratedFieldEncoding.Float64BitsLE: return 8u;
                default: throw new InvalidOperationException("Generated dispatch scalar encoding is outside the closed ABI.");
            }
        }

        private static void AddRepeated(
            IList<Leaf> result,
            uint byteOffset,
            uint count,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding)
        {
            for (uint index = 0; index < count; index++)
                AddScalar(result, checked(byteOffset + checked(index * elementSize)), elementSize, encoding);
        }

        private static void AddScalar(
            IList<Leaf> result,
            uint byteOffset,
            uint elementSize,
            NativeBurstDispatchFieldEncodingV2 encoding)
            => result.Add(new Leaf(byteOffset, elementSize, encoding));

        private static void AppendRun(
            IList<NativeBurstDispatchFieldV2> result,
            uint fieldOrdinal,
            uint elementIndex,
            Leaf leaf)
        {
            if (result.Count != 0)
            {
                var previous = result[result.Count - 1];
                var previousEnd = checked(previous.ByteOffset + checked(previous.ElementCount * previous.ElementSize));
                if (previous.FieldOrdinal == fieldOrdinal
                    && previous.CanonicalRuleKind == NativeBurstDispatchCanonicalRuleKindV2.None
                    && leaf.CanonicalRuleKind == NativeBurstDispatchCanonicalRuleKindV2.None
                    && previous.Encoding == leaf.Encoding
                    && previous.ElementSize == leaf.ElementSize
                    && checked(previous.FirstElementIndex + previous.ElementCount) == elementIndex
                    && previousEnd == leaf.ByteOffset)
                {
                    result[result.Count - 1] = new NativeBurstDispatchFieldV2(
                        previous.FieldOrdinal,
                        previous.FirstElementIndex,
                        previous.ByteOffset,
                        checked(previous.ElementCount + 1u),
                        previous.ElementSize,
                        previous.Encoding,
                        NativeBurstDispatchCanonicalRuleKindV2.None);
                    return;
                }
            }
            result.Add(new NativeBurstDispatchFieldV2(
                fieldOrdinal,
                elementIndex,
                leaf.ByteOffset,
                1u,
                leaf.ElementSize,
                leaf.Encoding,
                leaf.CanonicalRuleKind));
        }

        private readonly struct ValueLayout
        {
            internal ValueLayout(
                uint firstField,
                uint fieldCount,
                uint valueSize,
                NativeBurstDispatchCanonicalRangeV2 canonicalRange)
            {
                FirstField = firstField;
                FieldCount = fieldCount;
                ValueSize = valueSize;
                CanonicalRange = canonicalRange;
            }

            internal uint FirstField { get; }
            internal uint FieldCount { get; }
            internal uint ValueSize { get; }
            internal NativeBurstDispatchCanonicalRangeV2 CanonicalRange { get; }
        }

        private readonly struct Leaf
        {
            internal Leaf(uint byteOffset, uint elementSize, NativeBurstDispatchFieldEncodingV2 encoding)
            {
                ByteOffset = byteOffset;
                ElementSize = elementSize;
                Encoding = encoding;
                CanonicalRuleKind = NativeBurstDispatchCanonicalRuleKindV2.None;
            }

            private Leaf(
                uint byteOffset,
                uint elementSize,
                NativeBurstDispatchFieldEncodingV2 encoding,
                NativeBurstDispatchCanonicalRuleKindV2 canonicalRuleKind)
            {
                ByteOffset = byteOffset;
                ElementSize = elementSize;
                Encoding = encoding;
                CanonicalRuleKind = canonicalRuleKind;
            }

            internal Leaf WithCanonicalRule(NativeBurstDispatchCanonicalRuleKindV2 canonicalRuleKind)
                => new Leaf(ByteOffset, ElementSize, Encoding, canonicalRuleKind);

            internal uint ByteOffset { get; }
            internal uint ElementSize { get; }
            internal NativeBurstDispatchFieldEncodingV2 Encoding { get; }
            internal NativeBurstDispatchCanonicalRuleKindV2 CanonicalRuleKind { get; }
        }
    }
}
