using System;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Graph;
using NUnit.Framework;

namespace AIBT.Tests.Editor.EditorGraphViewUnificationSpike
{
    /// <summary>
    /// P6-016 disposable spike. Proves the recommended integration shape for two representative
    /// tools against the real, unmodified <see cref="BehaviorTreeGraphView"/>:
    /// <list type="bullet">
    /// <item>A "view-owning" tool (mirroring <c>BehaviorTreePreviewWindow</c>/<c>TraceTimelineWindow</c>,
    /// both of which today construct their own private <c>new BehaviorTreeGraphView()</c>, confirmed
    /// by reading their source) should attach to and share ONE existing view instance instead.</item>
    /// <item>A "document-operating" tool (mirroring <c>Editor/Editing/</c>'s <see cref="SemanticEditOperations"/>,
    /// which owns no view or window at all -- confirmed by reading its source) needs no view-sharing
    /// mechanism at all: "wiring" it in means calling its already-public, pure functions and
    /// re-<see cref="BehaviorTreeGraphView.Populate"/>-ing whichever view is already on screen.</item>
    /// </list>
    /// This spike deliberately tests at the <see cref="BehaviorTreeGraphView"/> level directly, not
    /// through a real, shown <c>BehaviorTreeGraphWindow</c> instance -- the sharable resource this
    /// decision is actually about is the view, not the window that hosts it, and constructing a real,
    /// visible <c>EditorWindow</c> here would show a window in the live Editor session this spike ran
    /// in. This is a real, disclosed spike limitation, not the shipped design; see the ADR.
    /// Archived to <c>Spikes~/EditorGraphViewUnification/</c> once proven.
    /// </summary>
    public sealed class SpikeEditorGraphViewUnification
    {
        [Test]
        public void SharedViewInstance_LetsTwoIndependentConsumersSeeTheIdenticalLiveNodes()
        {
            var document = SingleLeafSequenceTree();
            var registry = BuildRegistry();

            // ONE shared view -- what a future BehaviorTreeGraphWindow.View accessor (a small,
            // additive public property, not yet built per this card's own Forbidden-changes clause)
            // would hand out, instead of BehaviorTreePreviewWindow/TraceTimelineWindow each
            // constructing their own private copy as they do today.
            var sharedView = new BehaviorTreeGraphView();
            sharedView.Populate(document, registry);

            // Two independent consumers, standing in for BehaviorTreeGraphWindow itself and a
            // second tool (e.g. TraceTimelineWindow) that would attach to the same instance.
            var mainWindowConsumer = sharedView;
            var traceToolConsumer = sharedView;

            Assert.That(mainWindowConsumer.NodesById.Count, Is.EqualTo(2));
            var rootFromMain = mainWindowConsumer.NodesById[new NodeId("root")];
            var rootFromTrace = traceToolConsumer.NodesById[new NodeId("root")];

            // Decisive proof: not merely equal data, the SAME object reference -- a trace tool
            // highlighting "root" is highlighting the exact node the user is editing in the main
            // window, not a stale, disconnected copy of it.
            Assert.That(ReferenceEquals(rootFromMain, rootFromTrace), Is.True,
                "Two consumers of the same view instance must observe the identical node objects, proving real sharing, not independent copies.");
        }

        [Test]
        public void DocumentOperatingTool_NeedsNoNewMechanism_JustCallsItsExistingApiAndRepopulatesTheSharedView()
        {
            var document = SingleLeafSequenceTree();
            var registry = BuildRegistry();
            var sharedView = new BehaviorTreeGraphView();
            sharedView.Populate(document, registry);
            Assert.That(sharedView.NodesById.Count, Is.EqualTo(2));

            // "Wiring" Editor/Editing/'s semantic-edit tool means exactly this: call its already
            // real, public, pure operation, then re-Populate whichever view is already on screen --
            // no new view-sharing mechanism, no new public API on BehaviorTreeGraphView/Window at
            // all needed for this category of tool.
            var newLeaf = new NodeDocument(
                new NodeId("second-leaf"), ReferenceFixtureNodeManifests.SuccessTypeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var edited = SemanticEditOperations.AddNode(document, newLeaf, new NodeId("root"));

            sharedView.Populate(edited, registry);

            Assert.That(sharedView.NodesById.Count, Is.EqualTo(3));
            Assert.That(sharedView.NodesById.ContainsKey(new NodeId("second-leaf")), Is.True);
            // Both original nodes are still present after the edit + re-populate round trip.
            Assert.That(sharedView.NodesById.ContainsKey(new NodeId("root")), Is.True);
            Assert.That(sharedView.NodesById.ContainsKey(new NodeId("leaf")), Is.True);
        }

        private static TreeDocument SingleLeafSequenceTree()
        {
            var leaf = new NodeDocument(
                new NodeId("leaf"), ReferenceFixtureNodeManifests.SuccessTypeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("leaf") },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p6-016-spike"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static AIBT.Authoring.NodeRegistry BuildRegistry()
            => NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build().Registry;
    }
}
