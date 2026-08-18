using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.CodeGen.Generation
{
    public sealed class GeneratedBurstDispatchPrebindingContractTests
    {
        private const ulong CanonicalBytesEqualityContractId = 0x69e3a80e385e338eUL;
        private const string InnerTypeId = "aibt.tests.prebinding.inner";
        private const string OuterTypeId = "aibt.tests.prebinding.outer";
        private const string InnerSchemaId = "aibt.tests.prebinding.inner.schema";
        private const string OuterSchemaId = "aibt.tests.prebinding.outer.schema";
        private static readonly string InnerSchemaHash = new string('1', 64);
        private static readonly string OuterSchemaHash = new string('2', 64);

        [Test]
        public void BindingPlan_MapsAllKindsToCanonicalHandlesAndExactValueLayouts()
        {
            var fixture = CreateFixture(reverseDeclarations: true);
            var plan = ReadPlan(fixture.Descriptor, fixture.Catalog);

            Assert.That(plan.Bindings.Select(value => value.BindingOrdinal),
                Is.EqualTo(new uint[] { 0, 1, 2, 3, 4, 5, 6 }));
            Assert.That(plan.Bindings.Select(value => value.ConfigurationFieldOrdinal),
                Is.EqualTo(new uint[] { 6, 5, 4, 3, 2, 1, 0 }),
                "Binding ordinals are canonical binding-ID order, independently of config field order.");
            Assert.That(plan.Bindings.Select(value => value.Kind),
                Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6 }));
            Assert.That(plan.Bindings.Select(value => value.Scope),
                Is.EqualTo(new byte[] { 2, 1, 1, byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue }));
            Assert.That(plan.Bindings.Select(value => value.PhaseMask),
                Is.EqualTo(new byte[] { 0, 0, 0, 0, 1, 3, 4 }));

            AssertPrimary(plan.Bindings[0], OuterTypeId, 0, 10, 96);
            AssertPrimary(plan.Bindings[1], "AgentId", 10, 1, 8);
            AssertPrimary(plan.Bindings[2], "EntityId", 11, 1, 8);
            AssertPrimary(plan.Bindings[3], "OperationId", 12, 3, 24);
            AssertPrimary(plan.Bindings[4], "AssetId", 15, 4, 32);
            AssertPrimary(plan.Bindings[5], "FixedString32", 19, 2, 32);
            AssertPrimary(plan.Bindings[6], "Float64", 31, 1, 8);

            Assert.That(plan.Bindings.Take(5).Concat(plan.Bindings.Skip(6)).All(value =>
                    value.SecondaryTypeNumericId == 0 && value.SecondaryTypeVersion == 0
                    && value.FirstSecondaryValueField == 0 && value.SecondaryValueFieldCount == 0
                    && value.SecondaryValueSize == 0),
                Is.True, "Only AsyncOperation has a secondary payload layout.");
            var asyncBinding = plan.Bindings[5];
            Assert.That(asyncBinding.SecondaryTypeNumericId, Is.EqualTo(StableHash.Fnv1A64(OuterTypeId)));
            Assert.That(asyncBinding.SecondaryTypeVersion, Is.EqualTo(1));
            Assert.That(asyncBinding.FirstSecondaryValueField, Is.EqualTo(21));
            Assert.That(asyncBinding.SecondaryValueFieldCount, Is.EqualTo(10));
            Assert.That(asyncBinding.SecondaryValueSize, Is.EqualTo(96));

            Assert.That(plan.ValueFields.Select(FieldSignature), Is.EqualTo(new[]
            {
                "0:0:0:1:8:8", "0:1:8:1:8:8", "0:2:16:1:8:7", "0:3:24:1:1:0",
                "1:0:32:1:2:4", "1:1:34:30:1:2",
                "2:0:64:1:8:8", "2:1:72:2:4:6", "2:3:80:1:8:8", "2:4:88:1:2:4",
                "0:0:0:1:8:8",
                "0:0:0:1:8:8",
                "0:0:0:1:8:8", "0:1:8:2:4:6", "0:3:16:1:8:8",
                "0:0:0:1:8:8", "0:1:8:1:8:8", "0:2:16:1:8:7", "0:3:24:1:1:0",
                "0:0:0:1:2:4", "0:1:2:30:1:2",
                "0:0:0:1:8:8", "0:1:8:1:8:8", "0:2:16:1:8:7", "0:3:24:1:1:0",
                "1:0:32:1:2:4", "1:1:34:30:1:2",
                "2:0:64:1:8:8", "2:1:72:2:4:6", "2:3:80:1:8:8", "2:4:88:1:2:4",
                "0:0:0:1:8:10",
            }), "Value layouts must match generated codec field/leaf ordinals and canonical encodings exactly.");

            Assert.That(plan.BindingRanges.Select(RangeSignature), Is.EqualTo(new[]
            {
                "0:3", "3:0",
                "3:1", "4:0",
                "4:1", "5:0",
                "5:1", "6:0",
                "6:1", "7:0",
                "7:1", "8:3",
                "11:0", "11:0",
            }), "Each binding must publish primary then secondary canonical-rule ranges.");
            Assert.That(plan.CanonicalRules.Select(RuleSignature), Is.EqualTo(new[]
            {
                "4:0", "5:32", "3:64",
                "1:0", "2:0", "3:0", "4:0", "5:0",
                "4:0", "5:32", "3:64",
            }), "Special canonical rules must preserve root-relative offsets through registered recursion.");
            AssertAnchorsMatchRules(plan);

            var canonicalFixture = CreateFixture(reverseDeclarations: false);
            var canonical = ReadPlan(canonicalFixture.Descriptor, canonicalFixture.Catalog);
            Assert.That(PlanSignature(plan), Is.EqualTo(PlanSignature(canonical)),
                "Declaration order must not change the binding plan.");
        }

        [TestCase("FixedString32", 32u, (byte)5)]
        [TestCase("FixedString64", 64u, (byte)6)]
        [TestCase("FixedString128", 128u, (byte)7)]
        [TestCase("FixedString512", 512u, (byte)8)]
        public void BindingPlan_EmitsExactFixedStringLeavesAndCanonicalRule(
            string typeId,
            uint size,
            byte ruleKind)
        {
            var fixture = CreateSingleBuiltInFixture(typeId);
            var plan = ReadPlan(fixture.Descriptor, fixture.Catalog);

            Assert.That(plan.ValueFields.Select(FieldSignature), Is.EqualTo(new[]
            {
                "0:0:0:1:2:4",
                "0:1:2:" + (size - 2u) + ":1:2",
            }));
            Assert.That(plan.BindingRanges.Select(RangeSignature), Is.EqualTo(new[] { "0:1", "1:0" }));
            Assert.That(plan.CanonicalRules.Select(RuleSignature), Is.EqualTo(new[] { ruleKind + ":0" }));
            AssertAnchorsMatchRules(plan);
        }

        [Test]
        public void BindingPlan_FailsClosedOnCatalogLayoutOrBindingDrift()
        {
            var missingCatalog = CreateFixture(reverseDeclarations: false);
            Assert.Throws<InvalidOperationException>(() =>
                ReadPlan(missingCatalog.Descriptor,
                    new RegisteredBlackboardTypeCatalog(Array.Empty<RegisteredBlackboardTypeCatalogEntry>())));

            var ordinalDrift = CreateFixture(reverseDeclarations: false);
            SetInternalProperty(ordinalDrift.Descriptor.Bindings[0], "Ordinal", 6u);
            Assert.Throws<InvalidOperationException>(() => ReadPlan(ordinalDrift.Descriptor, ordinalDrift.Catalog));

            var configLayoutDrift = CreateFixture(reverseDeclarations: false);
            SetInternalProperty(configLayoutDrift.Descriptor.Configuration[0], "Offset", 4u);
            Assert.Throws<InvalidOperationException>(() => ReadPlan(configLayoutDrift.Descriptor, configLayoutDrift.Catalog));

            var registeredLayoutDrift = CreateFixture(reverseDeclarations: false);
            var outer = registeredLayoutDrift.Catalog.Entries.Single(value => value.CanonicalTypeId == OuterTypeId);
            SetInternalProperty(outer.Fields[1], "Offset", 40u);
            Assert.Throws<InvalidOperationException>(() => ReadPlan(registeredLayoutDrift.Descriptor, registeredLayoutDrift.Catalog));
        }

        [Test]
        public void NodePlan_CombinesCaseAndBindingCanonicalAuthorityAsOneExactPartition()
        {
            var reversedFixture = CreateNodePlanFixture(reverseDeclarations: true);
            var reversed = ReadNodePlan(reversedFixture.Descriptor, reversedFixture.Catalog);

            Assert.That(reversed.ConfigurationFields.Select(FieldSignature), Is.EqualTo(new[]
            {
                "0:0:0:1:4:11",
                "1:0:8:1:8:8",
            }));
            Assert.That(reversed.MemoryFields.Select(FieldSignature), Is.EqualTo(new[]
            {
                "0:0:0:1:8:8", "0:1:8:1:8:8", "0:2:16:1:8:7", "0:3:24:1:1:0",
                "1:0:32:1:2:4", "1:1:34:30:1:2",
            }));
            Assert.That(reversed.ValueFields.Select(FieldSignature), Is.EqualTo(new[]
            {
                "0:0:0:1:2:4", "0:1:2:62:1:2",
            }));
            Assert.That(reversed.CaseRanges.Select(RangeSignature), Is.EqualTo(new[] { "0:1", "1:2" }));
            Assert.That(reversed.BindingRanges.Select(RangeSignature), Is.EqualTo(new[] { "3:1", "4:0" }));
            Assert.That(reversed.CanonicalRules.Select(RuleSignature), Is.EqualTo(new[]
            {
                "1:8", "4:0", "5:32", "6:0",
            }));
            Assert.That(reversed.CaseRanges.Concat(reversed.BindingRanges)
                    .Aggregate(0u, (cursor, range) =>
                    {
                        Assert.That(range.FirstRule, Is.EqualTo(cursor), "Every range must begin at the prior range's EOF.");
                        return checked(cursor + range.RuleCount);
                    }),
                Is.EqualTo((uint)reversed.CanonicalRules.Length),
                "Case config, case memory, then binding primary/secondary must exactly partition one canonical stream.");
            AssertRangeAnchors(reversed.ConfigurationFields, reversed.CanonicalRules, reversed.CaseRanges[0]);
            AssertRangeAnchors(reversed.MemoryFields, reversed.CanonicalRules, reversed.CaseRanges[1]);
            AssertRangeAnchors(reversed.ValueFields, reversed.CanonicalRules, reversed.BindingRanges[0]);

            var canonicalFixture = CreateNodePlanFixture(reverseDeclarations: false);
            var canonical = ReadNodePlan(canonicalFixture.Descriptor, canonicalFixture.Catalog);
            Assert.That(NodePlanSignature(reversed), Is.EqualTo(NodePlanSignature(canonical)),
                "Declaration order must not change the atomic node plan.");
        }

        [Test]
        public void CatalogPlan_RebasesCanonicalCasesAndKeepsCaseRulesBeforeBindingRules()
        {
            var first = CreateNodePlanFixture(reverseDeclarations: false);
            var second = CreateSingleBuiltInFixture("FixedString32");
            var emptyCatalog = new RegisteredBlackboardTypeCatalog(
                Array.Empty<RegisteredBlackboardTypeCatalogEntry>());
            var alpha = CreateShardArtifact(
                "aibt.tests.prebinding.alpha", 1u,
                new[] { second.Descriptor }, emptyCatalog);
            var beta = CreateShardArtifact(
                "aibt.tests.prebinding.beta", 1u,
                new[] { first.Descriptor }, emptyCatalog);

            var canonical = ReadCatalogPlan(
                "aibt.tests.prebinding.catalog", 1u,
                new[] { alpha, beta });
            var reversed = ReadCatalogPlan(
                "aibt.tests.prebinding.catalog", 1u,
                new[] { beta, alpha });

            Assert.That(CatalogPlanSignature(reversed), Is.EqualTo(CatalogPlanSignature(canonical)),
                "Shard declaration order must not change the catalog plan or handshake.");
            Assert.That(canonical.Cases.Select(value => value.TypeNumericId), Is.EqualTo(new[]
            {
                StableHash.Fnv1A64("aibt.tests.prebinding.node-plan"),
                StableHash.Fnv1A64("aibt.tests.prebinding.single"),
            }));
            Assert.That(canonical.Cases.Select(value => value.CatalogCaseIndex),
                Is.EqualTo(new uint[] { 0u, 1u }));
            Assert.That(canonical.Cases.Select(value => value.FirstConfigurationField),
                Is.EqualTo(new uint[] { 0u, 2u }));
            Assert.That(canonical.Cases.Select(value => value.ConfigurationFieldCount),
                Is.EqualTo(new uint[] { 2u, 1u }));
            Assert.That(canonical.Cases.Select(value => value.FirstMemoryField),
                Is.EqualTo(new uint[] { 0u, 6u }));
            Assert.That(canonical.Cases.Select(value => value.MemoryFieldCount),
                Is.EqualTo(new uint[] { 6u, 0u }));
            Assert.That(canonical.Cases.Select(value => value.FirstBinding),
                Is.EqualTo(new uint[] { 0u, 1u }));
            Assert.That(canonical.Cases.Select(value => value.BindingCount),
                Is.EqualTo(new uint[] { 1u, 1u }));
            Assert.That(canonical.CaseRanges.Select(RangeSignature),
                Is.EqualTo(new[] { "0:1", "1:2", "3:0", "3:0" }));
            Assert.That(canonical.BindingRanges.Select(RangeSignature),
                Is.EqualTo(new[] { "3:1", "4:0", "4:1", "5:0" }),
                "All case config/memory rules must precede every binding primary/secondary range.");
            Assert.That(canonical.CanonicalRules.Select(RuleSignature), Is.EqualTo(new[]
            {
                "1:8", "4:0", "5:32", "6:0", "5:0",
            }));
            Assert.That(canonical.HandshakeAbiVersion, Is.EqualTo(2u));
            Assert.That(canonical.HandshakeCompiledFormatVersion, Is.EqualTo(1u));
            Assert.That(canonical.HandshakeSemanticsVersion, Is.EqualTo(1u));
            Assert.That(canonical.HandshakeSignature, Is.Not.EqualTo(new string('0', 64 * 5)));
        }

        private static void AssertPrimary(BindingSnapshot actual, string typeId, uint first, uint count, uint size)
        {
            Assert.That(actual.PrimaryTypeNumericId, Is.EqualTo(StableHash.Fnv1A64(typeId)));
            Assert.That(actual.PrimaryTypeVersion, Is.EqualTo(1));
            Assert.That(actual.FirstPrimaryValueField, Is.EqualTo(first));
            Assert.That(actual.PrimaryValueFieldCount, Is.EqualTo(count));
            Assert.That(actual.PrimaryValueSize, Is.EqualTo(size));
        }

        private static void AssertAnchorsMatchRules(PlanSnapshot plan)
        {
            Assert.That(plan.BindingRanges, Has.Length.EqualTo(plan.Bindings.Length * 2));
            for (var bindingIndex = 0; bindingIndex < plan.Bindings.Length; bindingIndex++)
            {
                var binding = plan.Bindings[bindingIndex];
                AssertRangeAnchors(
                    plan,
                    plan.BindingRanges[bindingIndex * 2],
                    binding.FirstPrimaryValueField,
                    binding.PrimaryValueFieldCount);
                AssertRangeAnchors(
                    plan,
                    plan.BindingRanges[bindingIndex * 2 + 1],
                    binding.FirstSecondaryValueField,
                    binding.SecondaryValueFieldCount);
            }
        }

        private static void AssertRangeAnchors(
            PlanSnapshot plan,
            RangeSnapshot range,
            uint firstField,
            uint fieldCount)
        {
            var anchors = plan.ValueFields
                .Skip(checked((int)firstField))
                .Take(checked((int)fieldCount))
                .Where(value => value.CanonicalRuleKind != 0)
                .ToArray();
            var rules = plan.CanonicalRules
                .Skip(checked((int)range.FirstRule))
                .Take(checked((int)range.RuleCount))
                .ToArray();
            Assert.That(anchors, Has.Length.EqualTo(rules.Length),
                "Removing a sidecar rule must leave a detectable authoritative field annotation.");
            for (var index = 0; index < anchors.Length; index++)
            {
                Assert.That(anchors[index].ElementCount, Is.EqualTo(1),
                    "An annotated run must contain only the special root's first leaf.");
                Assert.That(anchors[index].CanonicalRuleKind, Is.EqualTo(rules[index].Kind));
                Assert.That(anchors[index].ByteOffset, Is.EqualTo(rules[index].ByteOffset));
            }
        }

        private static void AssertRangeAnchors(
            FieldSnapshot[] fields,
            RuleSnapshot[] allRules,
            RangeSnapshot range)
        {
            var anchors = fields.Where(value => value.CanonicalRuleKind != 0).ToArray();
            var rules = allRules
                .Skip(checked((int)range.FirstRule))
                .Take(checked((int)range.RuleCount))
                .ToArray();
            Assert.That(anchors, Has.Length.EqualTo(rules.Length));
            for (var index = 0; index < anchors.Length; index++)
            {
                Assert.That(anchors[index].ElementCount, Is.EqualTo(1));
                Assert.That(anchors[index].CanonicalRuleKind, Is.EqualTo(rules[index].Kind));
                Assert.That(anchors[index].ByteOffset, Is.EqualTo(rules[index].ByteOffset));
            }
        }

        private static Fixture CreateFixture(bool reverseDeclarations)
        {
            var innerDescriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(InnerTypeId), 1, 32, 8, CanonicalBytesEqualityContractId,
                StableHash.Fnv1A64(InnerSchemaId));
            var innerFields = new[]
            {
                Field("operation", "OperationId", 24, 8, GeneratedFieldEncoding.FixedBytes, offset: 0),
                Field("scalar", "UInt16", 2, 2, GeneratedFieldEncoding.UInt16LE, offset: 24),
            };
            var innerEntry = new RegisteredBlackboardTypeCatalogEntry(
                InnerTypeId, 1, InnerSchemaId, InnerSchemaHash, innerDescriptor, innerFields);

            var outerDescriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(OuterTypeId), 1, 96, 8, CanonicalBytesEqualityContractId,
                StableHash.Fnv1A64(OuterSchemaId));
            var outerFields = new[]
            {
                Field("asset", "AssetId", 32, 8, GeneratedFieldEncoding.FixedBytes, offset: 0),
                Field("label", "FixedString32", 32, 2, GeneratedFieldEncoding.FixedBytes, offset: 32),
                Field("nested", InnerTypeId, 32, 8, GeneratedFieldEncoding.Registered, offset: 64,
                    schemaHash: InnerSchemaHash, descriptor: innerDescriptor),
            };
            var outerEntry = new RegisteredBlackboardTypeCatalogEntry(
                OuterTypeId, 1, OuterSchemaId, OuterSchemaHash, outerDescriptor, outerFields);
            var catalog = new RegisteredBlackboardTypeCatalog(new[] { outerEntry, innerEntry });

            var bindings = new[]
            {
                Binding("a-read", GeneratedBindingKind.BlackboardRead, BlackboardScope.Agent,
                    GeneratedPhaseCapability.None, Registered(GeneratedTypeRole.Value, outerEntry)),
                Binding("b-write", GeneratedBindingKind.BlackboardWrite, BlackboardScope.Tree,
                    GeneratedPhaseCapability.None, BuiltIn(GeneratedTypeRole.Value, "AgentId")),
                Binding("c-read-write", GeneratedBindingKind.BlackboardReadWrite, BlackboardScope.Tree,
                    GeneratedPhaseCapability.None, BuiltIn(GeneratedTypeRole.Value, "EntityId")),
                Binding("d-snapshot", GeneratedBindingKind.SnapshotRead, (BlackboardScope)byte.MaxValue,
                    GeneratedPhaseCapability.None, BuiltIn(GeneratedTypeRole.Value, "OperationId")),
                Binding("e-command", GeneratedBindingKind.EffectCommand, (BlackboardScope)byte.MaxValue,
                    GeneratedPhaseCapability.Execute, BuiltIn(GeneratedTypeRole.EffectPayload, "AssetId")),
                Binding("f-async", GeneratedBindingKind.AsyncOperation, (BlackboardScope)byte.MaxValue,
                    GeneratedPhaseCapability.Execute | GeneratedPhaseCapability.Cancel,
                    Registered(GeneratedTypeRole.AsyncCancelPayload, outerEntry),
                    BuiltIn(GeneratedTypeRole.AsyncStartPayload, "FixedString32")),
                Binding("g-completion", GeneratedBindingKind.Completion, (BlackboardScope)byte.MaxValue,
                    GeneratedPhaseCapability.Completion, BuiltIn(GeneratedTypeRole.CompletionPayload, "Float64")),
            };
            var config = new[]
            {
                Handle("a-config-completion", "g-completion"),
                Handle("b-config-async", "f-async"),
                Handle("c-config-command", "e-command"),
                Handle("d-config-snapshot", "d-snapshot"),
                Handle("e-config-read-write", "c-read-write"),
                Handle("f-config-write", "b-write"),
                Handle("g-config-read", "a-read"),
            };
            if (reverseDeclarations)
            {
                Array.Reverse(bindings);
                Array.Reverse(config);
            }

            var manifestFields = new List<NodeConfigurationField>();
            for (var index = 0; index < 7; index++)
                manifestFields.Add(new NodeConfigurationField(
                    ((IEnumerable<GeneratedStorageField>)config).OrderBy(value => value.FieldId, StringComparer.Ordinal).ElementAt(index).FieldId,
                    checked((uint)index * 4u), 4, 4, isGeneratedHandle: true));
            var manifest = new NodeManifest(
                "aibt.tests.prebinding.all", 1, "Prebinding", "Tests", NodeBehaviorKind.Action,
                "Verify binding plans", "Not production", NodeExecutionDomain.Burst, true,
                Array.Empty<NodeParameterContract>(), new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Running },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(28, 4, manifestFields),
                NodeCancellationMode.Command, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("prebinding", "{}", "Prebinding") });
            return new Fixture(
                new GeneratedNodeDescriptor(manifest, config, Array.Empty<GeneratedStorageField>(), bindings),
                catalog);
        }

        private static Fixture CreateSingleBuiltInFixture(string typeId)
        {
            var config = new[] { Handle("handle", "target") };
            var manifest = new NodeManifest(
                "aibt.tests.prebinding.single", 1, "Prebinding", "Tests", NodeBehaviorKind.Condition,
                "Verify binding plans", "Not production", NodeExecutionDomain.Burst, true,
                Array.Empty<NodeParameterContract>(), new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(4, 4,
                    new[] { new NodeConfigurationField("handle", 0, 4, 4, isGeneratedHandle: true) }),
                NodeCancellationMode.NotApplicable, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("prebinding", "{}", "Prebinding") });
            var binding = Binding(
                "target", GeneratedBindingKind.SnapshotRead, (BlackboardScope)byte.MaxValue,
                GeneratedPhaseCapability.None, BuiltIn(GeneratedTypeRole.Value, typeId));
            return new Fixture(
                new GeneratedNodeDescriptor(
                    manifest, config, Array.Empty<GeneratedStorageField>(), new[] { binding }),
                new RegisteredBlackboardTypeCatalog(Array.Empty<RegisteredBlackboardTypeCatalogEntry>()));
        }

        private static Fixture CreateNodePlanFixture(bool reverseDeclarations)
        {
            var configuration = new[]
            {
                Handle("a-handle", "target"),
                Field("b-agent", "AgentId", 8, 8, GeneratedFieldEncoding.FixedBytes, offset: 8),
            };
            var memory = new[]
            {
                Field("a-asset", "AssetId", 32, 8, GeneratedFieldEncoding.FixedBytes, offset: 0),
                Field("b-label", "FixedString32", 32, 2, GeneratedFieldEncoding.FixedBytes, offset: 32),
            };
            var bindings = new[]
            {
                Binding("target", GeneratedBindingKind.SnapshotRead, (BlackboardScope)byte.MaxValue,
                    GeneratedPhaseCapability.None, BuiltIn(GeneratedTypeRole.Value, "FixedString64")),
            };
            if (reverseDeclarations)
            {
                Array.Reverse(configuration);
                Array.Reverse(memory);
                Array.Reverse(bindings);
            }

            var manifest = new NodeManifest(
                "aibt.tests.prebinding.node-plan", 1, "Node plan", "Tests", NodeBehaviorKind.Condition,
                "Verify atomic canonical authority", "Not production", NodeExecutionDomain.Burst, true,
                new[] { new NodeParameterContract("b-agent", NodeParameterType.UInt64, true) },
                new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(64, 8, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(16, 8, new[]
                {
                    new NodeConfigurationField("a-handle", 0, 4, 4, isGeneratedHandle: true),
                    new NodeConfigurationField("b-agent", 8, 8, 8),
                }),
                NodeCancellationMode.NotApplicable, NodeCostHint.Trivial,
                new[] { new NodeManifestExample("node-plan", "{}", "Node plan") });
            return new Fixture(
                new GeneratedNodeDescriptor(manifest, configuration, memory, bindings),
                new RegisteredBlackboardTypeCatalog(Array.Empty<RegisteredBlackboardTypeCatalogEntry>()));
        }

        private static GeneratedStorageField Field(
            string id,
            string typeId,
            uint size,
            byte alignment,
            GeneratedFieldEncoding encoding,
            uint offset,
            string schemaHash = null,
            RegisteredUnmanagedTypeDescriptor descriptor = default)
        {
            var field = new GeneratedStorageField(
                id, typeId, 1, size, alignment, encoding, schemaHash, registeredDescriptor: descriptor);
            SetInternalProperty(field, "Offset", offset);
            return field;
        }

        private static GeneratedStorageField Handle(string fieldId, string bindingId)
            => new GeneratedStorageField(
                fieldId, "GeneratedHandle", 1, 4, 4, GeneratedFieldEncoding.GeneratedHandle,
                bindingId: bindingId);

        private static GeneratedTypeRecord BuiltIn(GeneratedTypeRole role, string typeId)
            => new GeneratedTypeRecord(role, typeId, 1);

        private static GeneratedTypeRecord Registered(
            GeneratedTypeRole role,
            RegisteredBlackboardTypeCatalogEntry entry)
            => new GeneratedTypeRecord(role, entry.CanonicalTypeId, entry.Version, entry.SchemaHash, entry.Descriptor);

        private static GeneratedBindingDescriptor Binding(
            string id,
            GeneratedBindingKind kind,
            BlackboardScope scope,
            GeneratedPhaseCapability phase,
            params GeneratedTypeRecord[] types)
            => new GeneratedBindingDescriptor(id, kind, scope, phase, types);

        private static PlanSnapshot ReadPlan(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog catalog)
        {
            var adapter = typeof(GeneratedNodeDescriptor).Assembly.GetType(
                "AIBT.Authoring.GeneratedBurstDispatchPrebindingV2", throwOnError: true);
            var method = adapter.GetMethod("BindingPlan", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The production Authoring adapter must expose its internal binding-plan seam.");
            object plan;
            try
            {
                plan = method.Invoke(null, new object[] { descriptor, catalog });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            return new PlanSnapshot(
                Values(plan, "Bindings").Select(ReadBinding).ToArray(),
                Values(plan, "ValueFields").Select(ReadField).ToArray(),
                Values(plan, "BindingRanges").Select(ReadRange).ToArray(),
                Values(plan, "CanonicalRules").Select(ReadRule).ToArray());
        }

        private static NodePlanSnapshot ReadNodePlan(
            GeneratedNodeDescriptor descriptor,
            RegisteredBlackboardTypeCatalog catalog)
        {
            var adapter = typeof(GeneratedNodeDescriptor).Assembly.GetType(
                "AIBT.Authoring.GeneratedBurstDispatchPrebindingV2", throwOnError: true);
            var method = adapter.GetMethod("NodePlan", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "The production Authoring adapter must expose one atomic node-plan seam.");
            object plan;
            try
            {
                plan = method.Invoke(null, new object[] { descriptor, catalog });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            return new NodePlanSnapshot(
                Values(plan, "ConfigurationFields").Select(ReadField).ToArray(),
                Values(plan, "MemoryFields").Select(ReadField).ToArray(),
                Values(plan, "CaseRanges").Select(ReadRange).ToArray(),
                Values(plan, "Bindings").Select(ReadBinding).ToArray(),
                Values(plan, "ValueFields").Select(ReadField).ToArray(),
                Values(plan, "BindingRanges").Select(ReadRange).ToArray(),
                Values(plan, "CanonicalRules").Select(ReadRule).ToArray());
        }

        private static GeneratedShardMetadataArtifact CreateShardArtifact(
            string shardId,
            uint shardVersion,
            IReadOnlyList<GeneratedNodeDescriptor> descriptors,
            RegisteredBlackboardTypeCatalog catalog)
        {
            var constructor = typeof(GeneratedShardMetadataArtifact).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(string), typeof(uint), typeof(IReadOnlyList<GeneratedNodeDescriptor>),
                    typeof(RegisteredBlackboardTypeCatalog),
                },
                null);
            Assert.That(constructor, Is.Not.Null,
                "A materialized shard must retain exact shard identity for catalog hashing.");
            return (GeneratedShardMetadataArtifact)constructor.Invoke(
                new object[] { shardId, shardVersion, descriptors, catalog });
        }

        private static CatalogPlanSnapshot ReadCatalogPlan(
            string catalogId,
            uint catalogVersion,
            IReadOnlyList<GeneratedShardMetadataArtifact> shards)
        {
            var adapter = typeof(GeneratedNodeDescriptor).Assembly.GetType(
                "AIBT.Authoring.GeneratedBurstDispatchPrebindingV2", throwOnError: true);
            var method = adapter.GetMethod("CatalogPlan", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null,
                "The production Authoring adapter must expose one catalog-level dispatch authority.");
            object plan;
            try
            {
                plan = method.Invoke(null, new object[] { catalogId, catalogVersion, shards });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            var handshake = Property(plan, "Handshake");
            return new CatalogPlanSnapshot(
                Values(plan, "Cases").Select(ReadCase).ToArray(),
                Values(plan, "ConfigurationFields").Select(ReadField).ToArray(),
                Values(plan, "MemoryFields").Select(ReadField).ToArray(),
                Values(plan, "Bindings").Select(ReadBinding).ToArray(),
                Values(plan, "ValueFields").Select(ReadField).ToArray(),
                Values(plan, "CaseRanges").Select(ReadRange).ToArray(),
                Values(plan, "BindingRanges").Select(ReadRange).ToArray(),
                Values(plan, "CanonicalRules").Select(ReadRule).ToArray(),
                U32(handshake, "AbiVersion"),
                U32(handshake, "CompiledFormatVersion"),
                U32(handshake, "ExecutionSemanticsVersion"),
                HandshakeSignature(handshake));
        }

        private static IEnumerable<object> Values(object owner, string propertyName)
        {
            var property = owner.GetType().GetProperty(
                propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            return ((IEnumerable)property.GetValue(owner)).Cast<object>();
        }

        private static BindingSnapshot ReadBinding(object value)
            => new BindingSnapshot
            {
                BindingOrdinal = U32(value, "BindingOrdinal"),
                ConfigurationFieldOrdinal = U32(value, "ConfigurationFieldOrdinal"),
                Kind = U8(value, "Kind"),
                Scope = U8(value, "Scope"),
                PhaseMask = U8(value, "PhaseMask"),
                PrimaryTypeNumericId = U64(value, "PrimaryTypeNumericId"),
                PrimaryTypeVersion = U32(value, "PrimaryTypeVersion"),
                FirstPrimaryValueField = U32(value, "FirstPrimaryValueField"),
                PrimaryValueFieldCount = U32(value, "PrimaryValueFieldCount"),
                PrimaryValueSize = U32(value, "PrimaryValueSize"),
                SecondaryTypeNumericId = U64(value, "SecondaryTypeNumericId"),
                SecondaryTypeVersion = U32(value, "SecondaryTypeVersion"),
                FirstSecondaryValueField = U32(value, "FirstSecondaryValueField"),
                SecondaryValueFieldCount = U32(value, "SecondaryValueFieldCount"),
                SecondaryValueSize = U32(value, "SecondaryValueSize"),
            };

        private static CaseSnapshot ReadCase(object value)
            => new CaseSnapshot
            {
                TypeNumericId = U64(value, "TypeNumericId"),
                TypeVersion = U32(value, "TypeVersion"),
                CatalogCaseIndex = U32(value, "CatalogCaseIndex"),
                FirstConfigurationField = U32(value, "FirstConfigurationField"),
                ConfigurationFieldCount = U32(value, "ConfigurationFieldCount"),
                FirstMemoryField = U32(value, "FirstMemoryField"),
                MemoryFieldCount = U32(value, "MemoryFieldCount"),
                FirstBinding = U32(value, "FirstBinding"),
                BindingCount = U32(value, "BindingCount"),
            };

        private static FieldSnapshot ReadField(object value)
            => new FieldSnapshot
            {
                FieldOrdinal = U32(value, "FieldOrdinal"),
                FirstElementIndex = U32(value, "FirstElementIndex"),
                ByteOffset = U32(value, "ByteOffset"),
                ElementCount = U32(value, "ElementCount"),
                ElementSize = U32(value, "ElementSize"),
                Encoding = U8(value, "Encoding"),
                CanonicalRuleKind = U8(value, "CanonicalRuleKind"),
            };

        private static RangeSnapshot ReadRange(object value)
            => new RangeSnapshot
            {
                FirstRule = U32(value, "FirstRule"),
                RuleCount = U32(value, "RuleCount"),
            };

        private static RuleSnapshot ReadRule(object value)
            => new RuleSnapshot
            {
                Kind = U8(value, "Kind"),
                ByteOffset = U32(value, "ByteOffset"),
            };

        private static object Property(object value, string name)
            => value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(value);

        private static byte U8(object value, string name) => Convert.ToByte(Property(value, name));
        private static uint U32(object value, string name) => Convert.ToUInt32(Property(value, name));
        private static ulong U64(object value, string name) => Convert.ToUInt64(Property(value, name));

        private static void SetInternalProperty(object owner, string name, object value)
        {
            var property = owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null);
            var setter = property.GetSetMethod(nonPublic: true);
            Assert.That(setter, Is.Not.Null);
            setter.Invoke(owner, new[] { value });
        }

        private static string FieldSignature(FieldSnapshot field)
            => string.Join(":", field.FieldOrdinal, field.FirstElementIndex, field.ByteOffset,
                field.ElementCount, field.ElementSize, field.Encoding);

        private static string RangeSignature(RangeSnapshot range)
            => range.FirstRule + ":" + range.RuleCount;

        private static string RuleSignature(RuleSnapshot rule)
            => rule.Kind + ":" + rule.ByteOffset;

        private static string PlanSignature(PlanSnapshot plan)
            => string.Join(";", plan.Bindings.Select(value => string.Join(":",
                    value.BindingOrdinal, value.ConfigurationFieldOrdinal, value.Kind, value.Scope, value.PhaseMask,
                    value.PrimaryTypeNumericId, value.PrimaryTypeVersion, value.FirstPrimaryValueField,
                    value.PrimaryValueFieldCount, value.PrimaryValueSize, value.SecondaryTypeNumericId,
                    value.SecondaryTypeVersion, value.FirstSecondaryValueField, value.SecondaryValueFieldCount,
                    value.SecondaryValueSize)))
                + "|" + string.Join(";", plan.ValueFields.Select(value =>
                    FieldSignature(value) + ":" + value.CanonicalRuleKind))
                + "|" + string.Join(";", plan.BindingRanges.Select(RangeSignature))
                + "|" + string.Join(";", plan.CanonicalRules.Select(RuleSignature));

        private static string NodePlanSignature(NodePlanSnapshot plan)
            => string.Join(";", plan.ConfigurationFields.Select(value => FieldSignature(value) + ":" + value.CanonicalRuleKind))
                + "|" + string.Join(";", plan.MemoryFields.Select(value => FieldSignature(value) + ":" + value.CanonicalRuleKind))
                + "|" + string.Join(";", plan.CaseRanges.Select(RangeSignature))
                + "|" + string.Join(";", plan.Bindings.Select(value => string.Join(":",
                    value.BindingOrdinal, value.ConfigurationFieldOrdinal, value.Kind, value.Scope, value.PhaseMask,
                    value.PrimaryTypeNumericId, value.PrimaryTypeVersion, value.FirstPrimaryValueField,
                    value.PrimaryValueFieldCount, value.PrimaryValueSize, value.SecondaryTypeNumericId,
                    value.SecondaryTypeVersion, value.FirstSecondaryValueField, value.SecondaryValueFieldCount,
                    value.SecondaryValueSize)))
                + "|" + string.Join(";", plan.ValueFields.Select(value => FieldSignature(value) + ":" + value.CanonicalRuleKind))
                + "|" + string.Join(";", plan.BindingRanges.Select(RangeSignature))
                + "|" + string.Join(";", plan.CanonicalRules.Select(RuleSignature));

        private static string CatalogPlanSignature(CatalogPlanSnapshot plan)
            => string.Join(";", plan.Cases.Select(value => string.Join(":",
                    value.TypeNumericId, value.TypeVersion, value.CatalogCaseIndex,
                    value.FirstConfigurationField, value.ConfigurationFieldCount,
                    value.FirstMemoryField, value.MemoryFieldCount,
                    value.FirstBinding, value.BindingCount)))
                + "|" + string.Join(";", plan.ConfigurationFields.Select(FieldSignature))
                + "|" + string.Join(";", plan.MemoryFields.Select(FieldSignature))
                + "|" + string.Join(";", plan.Bindings.Select(value => string.Join(":",
                    value.BindingOrdinal, value.ConfigurationFieldOrdinal, value.FirstPrimaryValueField,
                    value.FirstSecondaryValueField)))
                + "|" + string.Join(";", plan.ValueFields.Select(FieldSignature))
                + "|" + string.Join(";", plan.CaseRanges.Select(RangeSignature))
                + "|" + string.Join(";", plan.BindingRanges.Select(RangeSignature))
                + "|" + string.Join(";", plan.CanonicalRules.Select(RuleSignature))
                + "|" + plan.HandshakeSignature;

        private static string HandshakeSignature(object handshake)
        {
            var catalog = Property(Property(handshake, "Catalog"), "Value");
            return HashSignature(catalog)
                + HashSignature(Property(handshake, "NodeRegistry"))
                + HashSignature(Property(handshake, "ConfigurationLayout"))
                + HashSignature(Property(handshake, "MemoryLayout"))
                + HashSignature(Property(handshake, "AccessLayout"));
        }

        private static string HashSignature(object hash)
            => string.Concat(Enumerable.Range(0, 8).Select(index =>
                U32(hash, "Word" + index).ToString("x8")));

        private sealed class Fixture
        {
            internal Fixture(GeneratedNodeDescriptor descriptor, RegisteredBlackboardTypeCatalog catalog)
            {
                Descriptor = descriptor;
                Catalog = catalog;
            }

            internal GeneratedNodeDescriptor Descriptor { get; }
            internal RegisteredBlackboardTypeCatalog Catalog { get; }
        }

        private sealed class PlanSnapshot
        {
            internal PlanSnapshot(
                BindingSnapshot[] bindings,
                FieldSnapshot[] valueFields,
                RangeSnapshot[] bindingRanges,
                RuleSnapshot[] canonicalRules)
            {
                Bindings = bindings;
                ValueFields = valueFields;
                BindingRanges = bindingRanges;
                CanonicalRules = canonicalRules;
            }

            internal BindingSnapshot[] Bindings { get; }
            internal FieldSnapshot[] ValueFields { get; }
            internal RangeSnapshot[] BindingRanges { get; }
            internal RuleSnapshot[] CanonicalRules { get; }
        }

        private sealed class NodePlanSnapshot
        {
            internal NodePlanSnapshot(
                FieldSnapshot[] configurationFields,
                FieldSnapshot[] memoryFields,
                RangeSnapshot[] caseRanges,
                BindingSnapshot[] bindings,
                FieldSnapshot[] valueFields,
                RangeSnapshot[] bindingRanges,
                RuleSnapshot[] canonicalRules)
            {
                ConfigurationFields = configurationFields;
                MemoryFields = memoryFields;
                CaseRanges = caseRanges;
                Bindings = bindings;
                ValueFields = valueFields;
                BindingRanges = bindingRanges;
                CanonicalRules = canonicalRules;
            }

            internal FieldSnapshot[] ConfigurationFields { get; }
            internal FieldSnapshot[] MemoryFields { get; }
            internal RangeSnapshot[] CaseRanges { get; }
            internal BindingSnapshot[] Bindings { get; }
            internal FieldSnapshot[] ValueFields { get; }
            internal RangeSnapshot[] BindingRanges { get; }
            internal RuleSnapshot[] CanonicalRules { get; }
        }

        private sealed class CatalogPlanSnapshot
        {
            internal CatalogPlanSnapshot(
                CaseSnapshot[] cases,
                FieldSnapshot[] configurationFields,
                FieldSnapshot[] memoryFields,
                BindingSnapshot[] bindings,
                FieldSnapshot[] valueFields,
                RangeSnapshot[] caseRanges,
                RangeSnapshot[] bindingRanges,
                RuleSnapshot[] canonicalRules,
                uint handshakeAbiVersion,
                uint handshakeCompiledFormatVersion,
                uint handshakeSemanticsVersion,
                string handshakeSignature)
            {
                Cases = cases;
                ConfigurationFields = configurationFields;
                MemoryFields = memoryFields;
                Bindings = bindings;
                ValueFields = valueFields;
                CaseRanges = caseRanges;
                BindingRanges = bindingRanges;
                CanonicalRules = canonicalRules;
                HandshakeAbiVersion = handshakeAbiVersion;
                HandshakeCompiledFormatVersion = handshakeCompiledFormatVersion;
                HandshakeSemanticsVersion = handshakeSemanticsVersion;
                HandshakeSignature = handshakeSignature;
            }

            internal CaseSnapshot[] Cases { get; }
            internal FieldSnapshot[] ConfigurationFields { get; }
            internal FieldSnapshot[] MemoryFields { get; }
            internal BindingSnapshot[] Bindings { get; }
            internal FieldSnapshot[] ValueFields { get; }
            internal RangeSnapshot[] CaseRanges { get; }
            internal RangeSnapshot[] BindingRanges { get; }
            internal RuleSnapshot[] CanonicalRules { get; }
            internal uint HandshakeAbiVersion { get; }
            internal uint HandshakeCompiledFormatVersion { get; }
            internal uint HandshakeSemanticsVersion { get; }
            internal string HandshakeSignature { get; }
        }

        private sealed class CaseSnapshot
        {
            internal ulong TypeNumericId;
            internal uint TypeVersion;
            internal uint CatalogCaseIndex;
            internal uint FirstConfigurationField;
            internal uint ConfigurationFieldCount;
            internal uint FirstMemoryField;
            internal uint MemoryFieldCount;
            internal uint FirstBinding;
            internal uint BindingCount;
        }

        private sealed class BindingSnapshot
        {
            internal uint BindingOrdinal;
            internal uint ConfigurationFieldOrdinal;
            internal byte Kind;
            internal byte Scope;
            internal byte PhaseMask;
            internal ulong PrimaryTypeNumericId;
            internal uint PrimaryTypeVersion;
            internal uint FirstPrimaryValueField;
            internal uint PrimaryValueFieldCount;
            internal uint PrimaryValueSize;
            internal ulong SecondaryTypeNumericId;
            internal uint SecondaryTypeVersion;
            internal uint FirstSecondaryValueField;
            internal uint SecondaryValueFieldCount;
            internal uint SecondaryValueSize;
        }

        private sealed class FieldSnapshot
        {
            internal uint FieldOrdinal;
            internal uint FirstElementIndex;
            internal uint ByteOffset;
            internal uint ElementCount;
            internal uint ElementSize;
            internal byte Encoding;
            internal byte CanonicalRuleKind;
        }

        private sealed class RangeSnapshot
        {
            internal uint FirstRule;
            internal uint RuleCount;
        }

        private sealed class RuleSnapshot
        {
            internal byte Kind;
            internal uint ByteOffset;
        }
    }
}
