using System.Collections.Generic;
using AIBT.Authoring;
using AIBT.Mcp.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Authoring
{
    /// <summary>
    /// A direct unit test on <see cref="McpAuthoringJson.WriteNode"/>/<see cref="McpAuthoringJson.ReadNode"/>
    /// -- the exact JSON mapping layer that previously dropped <see cref="NodeDocument.Observer"/>
    /// and <see cref="NodeDocument.Bindings"/> on every extract/inline subtree round trip. Not
    /// exercised via the full dispatcher/apply_domain_patch path: the production built-in registry
    /// <see cref="AIBT.Mcp.McpAuthoringToolDispatcher"/> uses has no Condition-kind node type at
    /// all (checked <see cref="BuiltInNodeManifests"/> directly), so no tree carrying a valid
    /// Observer can be built and accepted through that path today -- see this session's P6-006
    /// evidence addendum for the disclosed reachability gap this test works around.
    /// </summary>
    public sealed class McpAuthoringJsonTests
    {
        [Test]
        public void WriteNodeThenReadNodeRoundTripsObserverAndBindings()
        {
            var node = new NodeDocument(
                new NodeId("cond"),
                "aibt.test.condition",
                1,
                new List<NodeId>(),
                SemanticObject.Empty,
                new NodeObserver("self", new[] { "health" }),
                "Health Check",
                "Watches health.",
                new TagSet(new[] { "combat" }),
                new NodeBindingMap(new[] { new KeyValuePair<string, string>("speed", "agent.speed") }));

            var json = McpAuthoringJson.WriteNode(node);
            var roundTripped = McpAuthoringJson.ReadNode(json);

            Assert.That(roundTripped, Is.EqualTo(node), "WriteNode/ReadNode must round-trip Observer and Bindings, not just parameters/tags/children.");
            Assert.That(roundTripped.Observer, Is.Not.Null);
            Assert.That(roundTripped.Observer.Mode, Is.EqualTo("self"));
            Assert.That(roundTripped.Observer.WatchedKeys, Is.EqualTo(new[] { "health" }));
            Assert.That(roundTripped.Bindings, Is.Not.Null);
            Assert.That(roundTripped.Bindings.Values["speed"], Is.EqualTo("agent.speed"));
        }

        [Test]
        public void WriteNodeThenReadNodeLeavesObserverAndBindingsNullWhenAbsent()
        {
            // SemanticObject.Empty/TagSet.Empty/an empty child list, not the constructor's own
            // null defaults: WriteNode/ReadNode already normalize parameters/tags/children to
            // their non-null empty forms regardless of observer/bindings (pre-existing behavior,
            // unrelated to this fix), so a null-vs-empty NodeDocument.Equals mismatch on those
            // fields would be a false failure here.
            var node = new NodeDocument(
                new NodeId("plain"),
                BuiltInNodeManifests.MemorySequenceTypeId,
                1,
                new List<NodeId>(),
                SemanticObject.Empty,
                observer: null,
                displayName: null,
                description: null,
                tags: TagSet.Empty);

            var json = McpAuthoringJson.WriteNode(node);
            Assert.That(json["observer"], Is.Null);
            Assert.That(json["bindings"], Is.Null);

            var roundTripped = McpAuthoringJson.ReadNode(json);
            Assert.That(roundTripped.Observer, Is.Null);
            Assert.That(roundTripped.Bindings, Is.Null);
            Assert.That(roundTripped, Is.EqualTo(node));
        }
    }
}
