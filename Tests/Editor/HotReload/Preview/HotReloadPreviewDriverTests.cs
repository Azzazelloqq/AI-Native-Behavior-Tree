using System;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.HotReload.Preview
{
    public sealed class HotReloadPreviewDriverTests
    {
        [Test]
        public void TryCreate_RejectsInvalidArguments()
        {
            Assert.That(() => HotReloadPreviewDriver.TryCreate(null, "src", out _, out _), Throws.ArgumentNullException);

            var document = SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId);
            Assert.That(() => HotReloadPreviewDriver.TryCreate(document, "", out _, out _), Throws.ArgumentException);
        }

        [Test]
        public void TryReload_CompatibleParameterEdit_MigratesEveryNode()
        {
            Assert.That(HotReloadPreviewDriver.TryCreate(RepeaterTree(5), "trees/before.aibt.json", out var driver, out _), Is.True);
            driver.RunOneTick();

            Assert.That(driver.TryReload(RepeaterTree(5, stopOnFailure: false), "trees/after.aibt.json", out var outcome, out _), Is.True);

            Assert.That(outcome.FellBackToFullRestart, Is.False);
            Assert.That(outcome.RequiredFullRestart, Is.False);
            Assert.That(outcome.MigratedNodeCount, Is.EqualTo(2u));
            Assert.That(outcome.NodeVerdicts[new NodeId("root")], Is.EqualTo("Migrate"));
            Assert.That(outcome.RestartSubtreeRootNodeIds, Is.Empty);
        }

        [Test]
        public void TryReload_IncompatibleTypeChange_ReportsIncompatibleRestart()
        {
            Assert.That(HotReloadPreviewDriver.TryCreate(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId), "trees/before.aibt.json", out var driver, out _), Is.True);

            Assert.That(driver.TryReload(SingleLeafTree(ReferenceFixtureNodeManifests.FailureTypeId), "trees/after.aibt.json", out var outcome, out _), Is.True);

            Assert.That(outcome.NodeVerdicts[new NodeId("leaf")], Is.EqualTo("IncompatibleRestart"));
            Assert.That(outcome.RequiredFullRestart, Is.True, "the incompatible node is the tree root itself");
            Assert.That(outcome.ResetNodeCount, Is.EqualTo(1u));
        }

        [Test]
        public void TryReload_WhileOldInstanceIsActive_FallsBackToFullRestart()
        {
            Assert.That(HotReloadPreviewDriver.TryCreate(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId), "trees/before.aibt.json", out var driver, out _), Is.True);
            driver.RunOneTick();
            Assert.That(driver.ActiveNodeCount, Is.GreaterThan(0u), "precondition: the instance must be active before reload");

            Assert.That(driver.TryReload(SequenceOverLeafTree(ReferenceFixtureNodeManifests.RunningTypeId), "trees/after.aibt.json", out var outcome, out _), Is.True);

            Assert.That(outcome.FellBackToFullRestart, Is.True);
            Assert.That(driver.ActiveNodeCount, Is.EqualTo(0u), "the fresh instance from a full restart has no active nodes until ticked");
        }

        [Test]
        public void TryReload_RejectsAnUncompilableDocument()
        {
            Assert.That(HotReloadPreviewDriver.TryCreate(SingleLeafTree(ReferenceFixtureNodeManifests.SuccessTypeId), "trees/before.aibt.json", out var driver, out _), Is.True);

            var brokenTree = new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.broken"), "Broken", new NodeId("missing-root"), Array.Empty<NodeDocument>(),
                tags: TagSet.Empty, metadata: SemanticObject.Empty);

            Assert.That(driver.TryReload(brokenTree, "trees/broken.aibt.json", out var outcome, out var diagnostics), Is.False);
            Assert.That(outcome, Is.Null);
            Assert.That(diagnostics, Is.Not.Empty);
        }

        // --- helpers ---

        private static TreeDocument SingleLeafTree(string leafTypeId)
        {
            var leaf = Node("leaf", leafTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.workflow-spec"), "Spec", leaf.Id, new[] { leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument SequenceOverLeafTree(string leafTypeId)
        {
            var leaf = Node("leaf", leafTypeId);
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("leaf") },
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.workflow-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument RepeaterTree(uint count, bool stopOnFailure = true)
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(stopOnFailure)),
                new SemanticProperty("count", SemanticValue.FromUInt64(count)),
            });
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.RepeaterTypeId, 1,
                new[] { new NodeId("leaf") }, parameters, tags: TagSet.Empty);
            var leaf = Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.workflow-spec"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
