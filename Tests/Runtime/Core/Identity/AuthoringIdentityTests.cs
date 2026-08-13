using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.Core.Identity
{
    public sealed class AuthoringIdentityTests
    {
        [Test]
        public void DefaultIdsAreInvalidAndFormatAsEmptyText()
        {
            Assert.That(default(TreeId).IsValid, Is.False);
            Assert.That(default(TreeId).ToString(), Is.Empty);
            Assert.That(default(NodeId).IsValid, Is.False);
            Assert.That(default(NodeId).ToString(), Is.Empty);
        }

        [TestCase("guard")]
        [TestCase("Combat.Guard_01:root-node")]
        public void TreeIdRoundTripsValidTextExactly(string text)
        {
            Assert.That(TreeId.TryParse(text, out var parsed), Is.True);
            Assert.That(parsed.IsValid, Is.True);
            Assert.That(parsed.Value, Is.EqualTo(text));
            Assert.That(TreeId.Parse(parsed.ToString()), Is.EqualTo(parsed));
        }

        [Test]
        public void NodeIdIsCaseSensitive()
        {
            var upper = new NodeId("Attack");
            var lower = new NodeId("attack");

            Assert.That(upper, Is.Not.EqualTo(lower));
            Assert.That(upper == lower, Is.False);
        }

        [TestCase("")]
        [TestCase("_root")]
        [TestCase("has space")]
        [TestCase("non-ascii-ж")]
        [TestCase("slash/root")]
        public void AuthoringIdsRejectTextOutsideTheCanonicalGrammar(string text)
        {
            Assert.That(TreeId.TryParse(text, out var treeId), Is.False);
            Assert.That(treeId, Is.EqualTo(default(TreeId)));
            Assert.That(NodeId.TryParse(text, out var nodeId), Is.False);
            Assert.That(nodeId, Is.EqualTo(default(NodeId)));
            Assert.Throws<FormatException>(() => TreeId.Parse(text));
            Assert.Throws<FormatException>(() => NodeId.Parse(text));
        }

        [Test]
        public void AuthoringIdsEnforceTheMaximumLength()
        {
            var maximum = new string('a', 128);
            var tooLong = new string('a', 129);

            Assert.That(TreeId.TryParse(maximum, out var parsed), Is.True);
            Assert.That(parsed.Value, Has.Length.EqualTo(128));
            Assert.That(TreeId.TryParse(tooLong, out _), Is.False);
        }

        [Test]
        public void ParseRejectsNullAuthoringId()
        {
            Assert.Throws<ArgumentNullException>(() => TreeId.Parse(null));
            Assert.Throws<ArgumentNullException>(() => NodeId.Parse(null));
        }
    }
}
