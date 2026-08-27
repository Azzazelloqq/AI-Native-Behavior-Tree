using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Patching;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Patching
{
    public sealed class SemanticPatchTransactionTests
    {
        [Test]
        public void ValidMultiOperationPatchIsAcceptedAndDiffedCorrectly()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var before = ThreeLevelTree();

            var result = SemanticPatchTransaction.Apply(
                before,
                before.Revision.Value,
                new Func<TreeDocument, TreeDocument>[]
                {
                    doc => SemanticEditOperations.AddNode(doc, NewLeaf("leaf2", "aibt.test.failure"), new NodeId("root")),
                    doc => SemanticEditOperations.SetParameter(doc, new NodeId("leaf"), "enabled", SemanticValue.FromBoolean(false)),
                },
                registry, options);

            Assert.That(result.Accepted, Is.True, DiagnosticsText(result.Diagnostics));
            Assert.That(result.Document.Nodes.Count, Is.EqualTo(before.Nodes.Count + 1));
            Assert.That(result.Diff.Entries.Select(e => e.NodeId.Value + ":" + e.Kind),
                Is.EquivalentTo(new[] { "leaf:Changed", "leaf2:Added", "root:Changed" }),
                "root's own Children list changes too when a new node is added under it -- correct, not a bug.");
            Assert.That(result.ResultRevision, Is.EqualTo(before.Revision.Value + 2), "Two composed operations increment revision twice.");
        }

        [Test]
        public void InvalidOperationInsideAMultiOperationPatchPersistsNothing()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var before = ThreeLevelTree();

            // Detaching the inverter's only child violates its ChildPolicy(1, 1).
            var result = SemanticPatchTransaction.Apply(
                before,
                before.Revision.Value,
                new Func<TreeDocument, TreeDocument>[]
                {
                    doc => SemanticEditOperations.SetParameter(doc, new NodeId("leaf"), "enabled", SemanticValue.FromBoolean(false)),
                    doc => SemanticEditOperations.Disconnect(doc, new NodeId("guard"), new NodeId("leaf")),
                },
                registry, options);

            Assert.That(result.Accepted, Is.False);
            Assert.That(ReferenceEquals(result.Document, before), Is.True, "A rejected patch must return the exact input reference, byte-identical.");
            Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(result.Diff.IsEmpty, Is.True);
        }

        [Test]
        public void RevisionMismatchIsRejectedBeforeAnyOperationRuns()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var before = ThreeLevelTree();
            var operationRan = false;

            var result = SemanticPatchTransaction.Apply(
                before,
                before.Revision.Value + 999,
                new Func<TreeDocument, TreeDocument>[]
                {
                    doc => { operationRan = true; return SemanticEditOperations.AddNode(doc, NewLeaf("leaf2", "aibt.test.failure"), new NodeId("root")); },
                },
                registry, options);

            Assert.That(result.Accepted, Is.False);
            Assert.That(operationRan, Is.False, "A revision mismatch must reject before invoking any operation.");
            Assert.That(result.Diagnostics.Single().Code.Value, Is.EqualTo("AIBT9009"));
            Assert.That(ReferenceEquals(result.Document, before), Is.True);
        }

        [Test]
        public void RepeatedCallsWithTheSameInputProduceByteIdenticalResultsAndNeverPersist()
        {
            var (registry, options) = BuildRegistryAndOptions();
            var before = ThreeLevelTree();
            Func<TreeDocument, TreeDocument>[] operations =
            {
                doc => SemanticEditOperations.SetParameter(doc, new NodeId("leaf"), "enabled", SemanticValue.FromBoolean(false)),
            };

            var first = SemanticPatchTransaction.Apply(before, before.Revision.Value, operations, registry, options);
            var second = SemanticPatchTransaction.Apply(before, before.Revision.Value, operations, registry, options);

            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Accepted, Is.True);
            Assert.That(CanonicalTreeJson.Serialize(first.Document).Utf8, Is.EqualTo(CanonicalTreeJson.Serialize(second.Document).Utf8),
                "Calling the transaction twice (the only way to 'dry-run' -- there is no persistence step to skip) must be side-effect-free and repeatable.");
            Assert.That(before.Nodes.Count, Is.EqualTo(3), "The original input document must never be mutated by any call.");
        }

        private static TreeDocument ThreeLevelTree()
        {
            var leaf = NewLeaf("leaf", "aibt.core.test-leaf", OneBooleanParameter(true));
            var guard = new NodeDocument(new NodeId("guard"), "aibt.core.inverter", 1, new[] { new NodeId("leaf") }, SemanticObject.Empty, null, null, null, TagSet.Empty);
            var root = new NodeDocument(new NodeId("root"), "aibt.core.memory-sequence", 1, new[] { new NodeId("guard") }, SemanticObject.Empty, null, null, null, TagSet.Empty);
            return new TreeDocument("aibt.tree", 1, new TreeId("tree.p6-004.test"), "P6-004 Test", new NodeId("root"), new[] { root, guard, leaf },
                blackboard: null, description: null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument NewLeaf(string id, string typeId)
        {
            return new NodeDocument(new NodeId(id), typeId, 1, null, SemanticObject.Empty, null, null, null, TagSet.Empty);
        }

        private static NodeDocument NewLeaf(string id, string typeId, SemanticObject parameters)
        {
            return new NodeDocument(new NodeId(id), typeId, 1, null, parameters, null, null, null, TagSet.Empty);
        }

        private static SemanticObject OneBooleanParameter(bool value)
        {
            return new SemanticObject(new[] { new SemanticProperty("enabled", SemanticValue.FromBoolean(value)) });
        }

        // Mirrors Tests/Editor/Editing/SemanticEditTransactionTests.cs's own BuildRegistryAndOptions:
        // Phase 1's ReferenceCompiler only executes BuiltIn/TestFixture-sourced node types, so a
        // real, bound, parameterized leaf needs AddBuiltInForTest, the same test-only registration
        // path that file already uses.
        private static (AIBT.Authoring.NodeRegistry Registry, ReferenceCompilerOptions Options) BuildRegistryAndOptions()
        {
            var leafManifest = new NodeManifest(
                "aibt.core.test-leaf",
                1,
                "A test leaf action.",
                "Test",
                NodeBehaviorKind.Action,
                "Use in patch-transaction tests.",
                "Do not use in production.",
                NodeExecutionDomain.Burst,
                true,
                new[] { new NodeParameterContract("enabled", NodeParameterType.Boolean, true) },
                new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(1, 1, new[] { new NodeConfigurationField("enabled", 0, 1, 1) }),
                NodeCancellationMode.NotApplicable,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Success", "{\"enabled\":true}", "Returns success.") });

            var buildResult = NodeRegistryBuilder.CreateWithBuiltIns()
                .AddBuiltInForTest(leafManifest, new NodeHandlerBindingContract("aibt.reference.test-leaf", leafManifest.Version, leafManifest.ExecutionDomain))
                .AddTestFixtures()
                .Build();
            Assert.That(buildResult.Success, Is.True, DiagnosticsText(buildResult.Diagnostics));

            var options = new ReferenceCompilerOptions(
                "trees/p6-004-test.aibt.json",
                ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 0));

            return (buildResult.Registry, options);
        }

        private static string DiagnosticsText(DiagnosticCollection diagnostics)
        {
            return diagnostics == null ? string.Empty : string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
        }
    }
}
