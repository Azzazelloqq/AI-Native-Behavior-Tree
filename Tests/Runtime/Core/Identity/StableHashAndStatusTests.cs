using System;
using System.Text;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.Core.Identity
{
    public sealed class StableHashAndStatusTests
    {
        [TestCase("", 14695981039346656037UL)]
        [TestCase("a", 12638187200555641996UL)]
        [TestCase("hello", 11831194018420276491UL)]
        [TestCase("foobar", 9625390261332436968UL)]
        public void Fnv1A64MatchesPublishedFixedVectors(string text, ulong expected)
        {
            Assert.That(StableHash.Fnv1A64(text), Is.EqualTo(expected));
            Assert.That(StableHash.Fnv1A64(Encoding.UTF8.GetBytes(text)), Is.EqualTo(expected));
        }

        [TestCase("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
        [TestCase("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
        [TestCase("hello", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
        public void Sha256MatchesPublishedFixedVectorsAsLowercaseHex(string text, string expected)
        {
            var actual = StableHash.Sha256Hex(text);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(StableHash.Sha256Hex(Encoding.UTF8.GetBytes(text)), Is.EqualTo(expected));
            Assert.That(actual, Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public void HashHelpersRejectNullInput()
        {
            Assert.Throws<ArgumentNullException>(() => StableHash.Fnv1A64((string)null));
            Assert.Throws<ArgumentNullException>(() => StableHash.Fnv1A64((byte[])null));
            Assert.Throws<ArgumentNullException>(() => StableHash.Sha256Hex((string)null));
            Assert.Throws<ArgumentNullException>(() => StableHash.Sha256Hex((byte[])null));
        }

        [Test]
        public void PublicNodeStatusContainsOnlySemanticResults()
        {
            CollectionAssert.AreEquivalent(
                new[] { "Success", "Failure", "Running" },
                Enum.GetNames(typeof(NodeStatus)));
            Assert.That(Enum.GetValues(typeof(NodeStatus)), Has.Length.EqualTo(3));
        }

        [Test]
        public void InternalExecutionStateKeepsSuspensionAndInactivityOutOfNodeStatus()
        {
            CollectionAssert.Contains(Enum.GetNames(typeof(NodeExecutionState)), "Inactive");
            CollectionAssert.Contains(Enum.GetNames(typeof(NodeExecutionState)), "BudgetYielded");
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(NodeStatus)), "Inactive");
            CollectionAssert.DoesNotContain(Enum.GetNames(typeof(NodeStatus)), "BudgetYielded");
        }

        [Test]
        public void InternalLifecycleReasonsMatchTheAcceptedV1Contract()
        {
            CollectionAssert.AreEquivalent(
                new[] { "Success", "Failure", "Aborted" },
                Enum.GetNames(typeof(NodeExitReason)));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Explicit",
                    "ObserverSelf",
                    "ObserverLowerPriority",
                    "TreeStopped",
                    "HotReload",
                    "Timeout"
                },
                Enum.GetNames(typeof(NodeAbortReason)));
        }
    }
}
