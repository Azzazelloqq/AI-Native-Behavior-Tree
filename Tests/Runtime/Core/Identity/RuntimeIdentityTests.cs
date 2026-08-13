using System;
using System.Globalization;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.Core.Identity
{
    public sealed class RuntimeIdentityTests
    {
        [Test]
        public void ZeroRuntimeIdentitiesAreInvalid()
        {
            Assert.That(default(TreeInstanceId).IsValid, Is.False);
            Assert.That(default(AgentId).IsValid, Is.False);
            Assert.That(default(EntityId).IsValid, Is.False);
            Assert.That(default(Revision).IsValid, Is.False);
        }

        [Test]
        public void RuntimeIdentitiesRoundTripUnsignedDecimalValues()
        {
            Assert.That(TreeInstanceId.Parse(ulong.MaxValue.ToString(CultureInfo.InvariantCulture)).Value,
                Is.EqualTo(ulong.MaxValue));
            Assert.That(AgentId.Parse("42"), Is.EqualTo(new AgentId(42)));
            Assert.That(EntityId.Parse("11"), Is.EqualTo(new EntityId(11)));
            Assert.That(Revision.Parse("9"), Is.EqualTo(new Revision(9)));
        }

        [TestCase("-1")]
        [TestCase("+1")]
        [TestCase("1.0")]
        [TestCase(" 1")]
        [TestCase("18446744073709551616")]
        public void RuntimeIdentitiesRejectNonCanonicalOrOverflowingDecimalText(string text)
        {
            Assert.That(TreeInstanceId.TryParse(text, out _), Is.False);
            Assert.That(AgentId.TryParse(text, out _), Is.False);
            Assert.That(EntityId.TryParse(text, out _), Is.False);
            Assert.That(Revision.TryParse(text, out _), Is.False);
        }

        [Test]
        public void TreeInstanceOrderingUsesTheUnsignedNumericValue()
        {
            var lower = new TreeInstanceId(2);
            var higher = new TreeInstanceId(10);

            Assert.That(lower.CompareTo(higher), Is.LessThan(0));
            Assert.That(lower < higher, Is.True);
            Assert.That(higher > lower, Is.True);
        }

        [Test]
        public void RuntimeNodeIndexUsesOnlyUnsignedMaxAsInvalidSentinel()
        {
            Assert.That(default(RuntimeNodeIndex).IsValid, Is.True);
            Assert.That(RuntimeNodeIndex.Invalid.IsValid, Is.False);
            Assert.That(RuntimeNodeIndex.Invalid.Value, Is.EqualTo(uint.MaxValue));
            Assert.That(RuntimeNodeIndex.Parse("0"), Is.EqualTo(new RuntimeNodeIndex(0)));
            Assert.That(RuntimeNodeIndex.Parse(uint.MaxValue.ToString(CultureInfo.InvariantCulture)),
                Is.EqualTo(RuntimeNodeIndex.Invalid));
        }

        [Test]
        public void OperationIdRoundTripsEveryField()
        {
            var operation = new OperationId(
                new TreeInstanceId(18446744073709551615UL),
                new RuntimeNodeIndex(4294967294U),
                4294967295U,
                18446744073709551615UL);

            var text = operation.ToString();
            var parsed = OperationId.Parse(text);

            Assert.That(text,
                Is.EqualTo("18446744073709551615:4294967294:4294967295:18446744073709551615"));
            Assert.That(parsed, Is.EqualTo(operation));
            Assert.That(parsed.TreeInstanceId, Is.EqualTo(operation.TreeInstanceId));
            Assert.That(parsed.NodeIndex, Is.EqualTo(operation.NodeIndex));
            Assert.That(parsed.ActivationGeneration, Is.EqualTo(operation.ActivationGeneration));
            Assert.That(parsed.Sequence, Is.EqualTo(operation.Sequence));
            Assert.That(parsed.IsValid, Is.True);
        }

        [Test]
        public void OperationValidityDependsOnTreeInstanceAndNodeIndex()
        {
            var valid = new OperationId(new TreeInstanceId(1), new RuntimeNodeIndex(0), 0, 0);
            var invalidTree = new OperationId(default, new RuntimeNodeIndex(0), 0, 0);
            var invalidNode = new OperationId(new TreeInstanceId(1), RuntimeNodeIndex.Invalid, 0, 0);

            Assert.That(valid.IsValid, Is.True);
            Assert.That(invalidTree.IsValid, Is.False);
            Assert.That(invalidNode.IsValid, Is.False);
            Assert.That(default(OperationId).IsValid, Is.False);
        }

        [TestCase("")]
        [TestCase("1:2:3")]
        [TestCase("1:2:3:4:5")]
        [TestCase("1:-2:3:4")]
        [TestCase("1:2:x:4")]
        public void OperationIdRejectsMalformedText(string text)
        {
            Assert.That(OperationId.TryParse(text, out var parsed), Is.False);
            Assert.That(parsed, Is.EqualTo(default(OperationId)));
            Assert.Throws<FormatException>(() => OperationId.Parse(text));
        }
    }
}
