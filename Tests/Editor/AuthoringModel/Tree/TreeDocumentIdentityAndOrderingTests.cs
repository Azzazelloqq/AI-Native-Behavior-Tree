using NUnit.Framework;

namespace AIBT.Tests.Editor.AuthoringModel.Tree
{
    public sealed class TreeDocumentIdentityAndOrderingTests
    {
        [Test]
        public void IdentityAndLookups_AreOrdinalAndCaseSensitive()
        {
            var properties = new Authoring.SemanticObject(new[]
            {
                new Authoring.SemanticProperty("Target", Authoring.SemanticValue.FromBoolean(true)),
                new Authoring.SemanticProperty("target", Authoring.SemanticValue.FromBoolean(false)),
            });

            Assert.That(new NodeId("Node"), Is.Not.EqualTo(new NodeId("node")));
            Assert.That(properties.TryGetValue("Target", out var upper), Is.True);
            Assert.That(properties.TryGetValue("target", out var lower), Is.True);
            Assert.That(upper.TryGetBoolean(out var upperValue) && upperValue, Is.True);
            Assert.That(lower.TryGetBoolean(out var lowerValue) && !lowerValue, Is.True);
            Assert.That(properties.TryGetValue("TARGET", out _), Is.False);
        }

        [Test]
        public void TagSet_HasDeterministicOrdinalSetSemantics()
        {
            var tags = new Authoring.TagSet(new[] { "z", "A", "a", "z", string.Empty });

            Assert.That(tags.Values, Is.EqualTo(new[] { string.Empty, "A", "a", "z" }));
            Assert.That(tags.HasDuplicateValues, Is.True, "invalid duplicate input must not be silently forgotten");
            Assert.That(tags.Contains("A"), Is.True);
            Assert.That(tags.Contains("a"), Is.True);
            Assert.That(tags.Contains("Z"), Is.False);
        }

        [Test]
        public void ObserverWatchedKeys_PreserveDeclaredArrayOrder()
        {
            var observer = new Authoring.NodeObserver("both", new[] { "last", "first" });

            Assert.That(observer.WatchedKeys, Is.EqualTo(new[] { "last", "first" }));
        }
    }
}
