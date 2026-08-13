using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.NodeRegistry
{
    public sealed class NodeManifestContractTests
    {
        [Test]
        public void BuiltIns_UseNormativeCanonicalTypeIdsAndVersionOne()
        {
            var expected = new[]
            {
                Authoring.BuiltInNodeManifests.MemorySequenceTypeId,
                Authoring.BuiltInNodeManifests.ReactiveSequenceTypeId,
                Authoring.BuiltInNodeManifests.MemorySelectorTypeId,
                Authoring.BuiltInNodeManifests.ReactiveSelectorTypeId,
                Authoring.BuiltInNodeManifests.ParallelTypeId,
                Authoring.BuiltInNodeManifests.InverterTypeId,
                Authoring.BuiltInNodeManifests.SucceederTypeId,
                Authoring.BuiltInNodeManifests.FailerTypeId,
                Authoring.BuiltInNodeManifests.RepeaterTypeId,
                Authoring.BuiltInNodeManifests.TimeoutTypeId,
                Authoring.BuiltInNodeManifests.CooldownTypeId,
            };

            Assert.That(Authoring.BuiltInNodeManifests.All.Select(item => item.TypeId), Is.EquivalentTo(expected));
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Version == 1), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Examples.Count > 0), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.ExecutionDomain == Authoring.NodeExecutionDomain.Burst), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Deterministic), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.Where(item => item.ChildPolicy.Maximum == 1)
                .All(item => item.Kind == Authoring.NodeBehaviorKind.Decorator), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.Where(item => item.ChildPolicy.Maximum != 1)
                .All(item => item.Kind == Authoring.NodeBehaviorKind.Composite), Is.True);
        }

        [Test]
        public void BuiltInParameterContracts_MatchNormativeNamesAndPacking()
        {
            AssertParameters(Authoring.BuiltInNodeManifests.ParallelTypeId, "failureThreshold", "policy", "successThreshold", "tieBreak");
            AssertParameters(Authoring.BuiltInNodeManifests.RepeaterTypeId, "count", "stopOnFailure");
            AssertParameters(Authoring.BuiltInNodeManifests.TimeoutTypeId, "durationMicroseconds", "terminalResult");
            AssertParameters(Authoring.BuiltInNodeManifests.CooldownTypeId, "blockedResult", "durationMicroseconds", "startPolicy");

            var parameterizedIds = new HashSet<string>(StringComparer.Ordinal)
            {
                Authoring.BuiltInNodeManifests.ParallelTypeId,
                Authoring.BuiltInNodeManifests.RepeaterTypeId,
                Authoring.BuiltInNodeManifests.TimeoutTypeId,
                Authoring.BuiltInNodeManifests.CooldownTypeId,
            };
            Assert.That(Authoring.BuiltInNodeManifests.All.Where(item => !parameterizedIds.Contains(item.TypeId)).All(item => item.Parameters.Count == 0), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Parameters.Count == item.Configuration.Fields.Count), Is.True);

            AssertParameter(
                Authoring.BuiltInNodeManifests.ParallelTypeId,
                "policy",
                Authoring.NodeParameterType.StringEnum,
                true,
                null,
                new[] { "require-all-success", "require-any-success", "threshold" },
                0, 1, 1);
            AssertConditionalParameter(Authoring.BuiltInNodeManifests.ParallelTypeId, "successThreshold", 4);
            AssertConditionalParameter(Authoring.BuiltInNodeManifests.ParallelTypeId, "failureThreshold", 8);
            AssertParameter(
                Authoring.BuiltInNodeManifests.ParallelTypeId,
                "tieBreak",
                Authoring.NodeParameterType.StringEnum,
                false,
                null,
                new[] { "failure-first", "success-first" },
                12, 1, 1,
                "policy", "threshold");
            AssertParameter(Authoring.BuiltInNodeManifests.RepeaterTypeId, "count", Authoring.NodeParameterType.UInt32, true, 1, null, 0, 4, 4);
            AssertParameter(Authoring.BuiltInNodeManifests.RepeaterTypeId, "stopOnFailure", Authoring.NodeParameterType.Boolean, true, null, null, 4, 1, 1);
            AssertParameter(Authoring.BuiltInNodeManifests.TimeoutTypeId, "durationMicroseconds", Authoring.NodeParameterType.UInt64, true, 1, null, 0, 8, 8);
            AssertParameter(Authoring.BuiltInNodeManifests.TimeoutTypeId, "terminalResult", Authoring.NodeParameterType.StringEnum, true, null, new[] { "failure", "success" }, 8, 1, 1);
            AssertParameter(Authoring.BuiltInNodeManifests.CooldownTypeId, "durationMicroseconds", Authoring.NodeParameterType.UInt64, true, 1, null, 0, 8, 8);
            AssertParameter(Authoring.BuiltInNodeManifests.CooldownTypeId, "blockedResult", Authoring.NodeParameterType.StringEnum, true, null, new[] { "failure", "success" }, 8, 1, 1);
            AssertParameter(Authoring.BuiltInNodeManifests.CooldownTypeId, "startPolicy", Authoring.NodeParameterType.StringEnum, true, null, new[] { "on-enter", "on-successful-exit" }, 9, 1, 1);
        }

        [Test]
        public void BuiltInMemoryConfigurationAndStatusContracts_AreExact()
        {
            AssertLayout(Authoring.BuiltInNodeManifests.MemorySequenceTypeId, 4, 4, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.ReactiveSequenceTypeId, 4, 4, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.MemorySelectorTypeId, 4, 4, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.ReactiveSelectorTypeId, 4, 4, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.ParallelTypeId, 8, 4, NodeMemoryLifetime.Activation, 16, 4, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.InverterTypeId, 0, 1, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.SucceederTypeId, 0, 1, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.FailerTypeId, 0, 1, NodeMemoryLifetime.Activation, 0, 1, NodeStatus.Failure, NodeStatus.Running);
            AssertLayout(Authoring.BuiltInNodeManifests.RepeaterTypeId, 4, 4, NodeMemoryLifetime.Activation, 8, 4, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.TimeoutTypeId, 8, 8, NodeMemoryLifetime.Activation, 16, 8, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
            AssertLayout(Authoring.BuiltInNodeManifests.CooldownTypeId, 8, 8, NodeMemoryLifetime.Instance, 16, 8, NodeStatus.Failure, NodeStatus.Running, NodeStatus.Success);
        }

        [Test]
        public void BuiltInChildPolicies_MatchCompositeAndDecoratorSemantics()
        {
            var parallel = Find(Authoring.BuiltInNodeManifests.ParallelTypeId);
            Assert.That(parallel.ChildPolicy.Minimum, Is.EqualTo(1));
            Assert.That(parallel.ChildPolicy.Maximum, Is.Null);

            var decorators = new[]
            {
                Authoring.BuiltInNodeManifests.InverterTypeId,
                Authoring.BuiltInNodeManifests.SucceederTypeId,
                Authoring.BuiltInNodeManifests.FailerTypeId,
                Authoring.BuiltInNodeManifests.RepeaterTypeId,
                Authoring.BuiltInNodeManifests.TimeoutTypeId,
                Authoring.BuiltInNodeManifests.CooldownTypeId,
            };
            Assert.That(decorators.Select(Find).All(item => item.ChildPolicy.Minimum == 1 && item.ChildPolicy.Maximum == 1), Is.True);

            var composites = new[]
            {
                Authoring.BuiltInNodeManifests.MemorySequenceTypeId,
                Authoring.BuiltInNodeManifests.ReactiveSequenceTypeId,
                Authoring.BuiltInNodeManifests.MemorySelectorTypeId,
                Authoring.BuiltInNodeManifests.ReactiveSelectorTypeId,
            };
            Assert.That(composites.Select(Find).All(item => item.ChildPolicy.Minimum == 0 && item.ChildPolicy.Maximum == null), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.ChildPolicy.Ordered), Is.True);
        }

        [Test]
        public void BuiltInExecutionDeclarations_AreExplicitAndSideEffectFree()
        {
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Reads.Count == 0), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Writes.Count == 0), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.SideEffects.Count == 0), Is.True);
            Assert.That(Authoring.BuiltInNodeManifests.All.All(item => item.Cancellation == Authoring.NodeCancellationMode.AbortOnly), Is.True);
        }

        [Test]
        public void Manifest_SortsSetLikeFieldsAndExposesDeclaredAccesses()
        {
            var manifest = NodeManifestTestFactory.Create(
                "example.nodes.access",
                reads: new[] { "target.z", "target.a" },
                writes: new[] { "result.z", "result.a" },
                sideEffects: new[] { "z-effect", "a-effect" });

            Assert.That(manifest.Reads, Is.EqualTo(new[] { "target.a", "target.z" }));
            Assert.That(manifest.Writes, Is.EqualTo(new[] { "result.a", "result.z" }));
            Assert.That(manifest.SideEffects, Is.EqualTo(new[] { "a-effect", "z-effect" }));
            Assert.That(manifest.Accesses.Select(item => item.Key), Is.EqualTo(new[] { "result.a", "result.z", "target.a", "target.z" }));
        }

        [Test]
        public void ConfigurationPacking_RejectsOverlapAndMisalignment()
        {
            Assert.Throws<ArgumentException>(() => new Authoring.NodeConfigurationDescriptor(8, 4, new[]
            {
                new Authoring.NodeConfigurationField("first", 0, 4, 4),
                new Authoring.NodeConfigurationField("second", 2, 4, 2),
            }));
            Assert.Throws<ArgumentException>(() => new Authoring.NodeConfigurationField("misaligned", 2, 4, 4));
        }

        [Test]
        public void MemoryDescriptor_RequiresAValidExplicitLifetime()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Authoring.NodeMemoryDescriptor(
                0,
                1,
                (NodeMemoryLifetime)byte.MaxValue));
        }

        [Test]
        public void Manifest_RejectsConfigurationWithoutOneFieldPerParameter()
        {
            Assert.Throws<ArgumentException>(() => NodeManifestTestFactory.Create("example.nodes.bad-packing", omitPacking: true));
        }

        [TestCase("Example.Nodes.Upper")]
        [TestCase("example..nodes")]
        [TestCase("example.nodes-")]
        [TestCase("unqualified")]
        public void Manifest_RejectsNonCanonicalTypeId(string typeId)
        {
            Assert.Throws<ArgumentException>(() => NodeManifestTestFactory.Create(typeId));
        }

        private static void AssertParameters(string typeId, params string[] names)
        {
            Assert.That(Find(typeId).Parameters.Select(item => item.Name), Is.EqualTo(names));
        }

        private static Authoring.NodeManifest Find(string typeId)
        {
            return Authoring.BuiltInNodeManifests.All.Single(item => item.TypeId == typeId);
        }

        private static void AssertConditionalParameter(string typeId, string name, uint offset)
        {
            AssertParameter(typeId, name, Authoring.NodeParameterType.UInt32, false, 1, null, offset, 4, 4, "policy", "threshold");
        }

        private static void AssertParameter(
            string typeId,
            string name,
            Authoring.NodeParameterType type,
            bool required,
            ulong? minimum,
            string[] allowedValues,
            uint offset,
            uint size,
            byte alignment,
            string conditionParameter = null,
            string conditionValue = null)
        {
            var manifest = Find(typeId);
            var parameter = manifest.Parameters.Single(item => item.Name == name);
            var field = manifest.Configuration.Fields.Single(item => item.ParameterName == name);
            Assert.That(parameter.Type, Is.EqualTo(type), typeId + "." + name);
            Assert.That(parameter.Required, Is.EqualTo(required), typeId + "." + name);
            Assert.That(parameter.Minimum, Is.EqualTo(minimum), typeId + "." + name);
            Assert.That(parameter.AllowedValues, Is.EqualTo(allowedValues ?? Array.Empty<string>()), typeId + "." + name);
            Assert.That(field.Offset, Is.EqualTo(offset), typeId + "." + name);
            Assert.That(field.Size, Is.EqualTo(size), typeId + "." + name);
            Assert.That(field.Alignment, Is.EqualTo(alignment), typeId + "." + name);
            if (conditionParameter == null)
            {
                Assert.That(parameter.RequiredWhen, Is.Null, typeId + "." + name);
                Assert.That(parameter.ForbiddenUnless, Is.Null, typeId + "." + name);
            }
            else
            {
                Assert.That(parameter.RequiredWhen.ParameterName, Is.EqualTo(conditionParameter));
                Assert.That(parameter.RequiredWhen.RequiredValue, Is.EqualTo(conditionValue));
                Assert.That(parameter.ForbiddenUnless.ParameterName, Is.EqualTo(conditionParameter));
                Assert.That(parameter.ForbiddenUnless.RequiredValue, Is.EqualTo(conditionValue));
            }
        }

        private static void AssertLayout(
            string typeId,
            uint memorySize,
            byte memoryAlignment,
            NodeMemoryLifetime memoryLifetime,
            uint configurationSize,
            byte configurationAlignment,
            params NodeStatus[] statuses)
        {
            var manifest = Find(typeId);
            Assert.That(manifest.Memory.Size, Is.EqualTo(memorySize), typeId);
            Assert.That(manifest.Memory.Alignment, Is.EqualTo(memoryAlignment), typeId);
            Assert.That(manifest.Memory.Lifetime, Is.EqualTo(memoryLifetime), typeId);
            Assert.That(manifest.Configuration.Size, Is.EqualTo(configurationSize), typeId);
            Assert.That(manifest.Configuration.Alignment, Is.EqualTo(configurationAlignment), typeId);
            Assert.That(manifest.PossibleStatuses, Is.EqualTo(statuses), typeId);
        }
    }
}
