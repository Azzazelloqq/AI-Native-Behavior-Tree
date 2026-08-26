using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using AuthoringNodeRegistry = AIBT.Authoring.NodeRegistry;

namespace AIBT.Tests.Editor.HotReloadSpike
{
    // Disposable P5-001 spike (OQ-007). Proves the compatibility-classification model against
    // real CompiledProgram pairs produced by the real ReferenceCompiler -- not synthetic-toy data.
    // Archived to Spikes~/HotReloadCompatibilityModel/ and Planning~/Evidence/P5-001/ after this
    // session; this file is deleted from Tests/ once archived (never shipped as production test
    // surface, per this card's own Forbidden changes).
    public sealed class SpikeHotReloadCompatibilityModel
    {
        private enum Verdict
        {
            CompatibleMigrate,
            CompatibleNew,
            CompatibleDropped,
            IncompatibleTypeOrVersionChange,
            IncompatibleLayoutChange,
        }

        private readonly struct IdentitySignature
        {
            internal IdentitySignature(CompiledNodeRecord record)
            {
                TypeId = record.NodeTypeId;
                TypeVersion = record.NodeTypeVersion;
                InstanceMemorySize = record.InstanceMemorySize;
                InstanceMemoryAlignment = record.InstanceMemoryAlignment;
                Lifetime = record.MemoryLifetime;
            }

            internal ulong TypeId { get; }
            internal uint TypeVersion { get; }
            internal uint InstanceMemorySize { get; }
            internal uint InstanceMemoryAlignment { get; }
            internal NodeMemoryLifetime Lifetime { get; }

            internal bool SameTypeAndVersion(IdentitySignature other) =>
                TypeId == other.TypeId && TypeVersion == other.TypeVersion;

            internal bool LayoutCompatibleWith(IdentitySignature other) =>
                InstanceMemorySize == other.InstanceMemorySize
                && InstanceMemoryAlignment == other.InstanceMemoryAlignment
                && Lifetime == other.Lifetime;
        }

        // The model under test: given two CompiledPrograms compiled from before/after authoring
        // documents, classify every node present in either by its real, stable authoring NodeId
        // (via each program's own DebugMap) -- never by compiled index, which is a pure DFS-recompute
        // artifact with zero stability guarantee across recompiles (confirmed against
        // ReferenceCompiler.OrderNodes/IndexNodes).
        private static IReadOnlyDictionary<NodeId, Verdict> Classify(CompiledProgram oldProgram, CompiledProgram newProgram)
        {
            var oldMap = BuildIdentityMap(oldProgram);
            var newMap = BuildIdentityMap(newProgram);
            var verdicts = new Dictionary<NodeId, Verdict>();

            foreach (var pair in oldMap)
            {
                if (!newMap.TryGetValue(pair.Key, out var newSig))
                {
                    verdicts[pair.Key] = Verdict.CompatibleDropped;
                    continue;
                }

                if (!pair.Value.SameTypeAndVersion(newSig))
                {
                    verdicts[pair.Key] = Verdict.IncompatibleTypeOrVersionChange;
                    continue;
                }

                verdicts[pair.Key] = pair.Value.LayoutCompatibleWith(newSig)
                    ? Verdict.CompatibleMigrate
                    : Verdict.IncompatibleLayoutChange;
            }

            foreach (var id in newMap.Keys)
            {
                if (!oldMap.ContainsKey(id)) verdicts[id] = Verdict.CompatibleNew;
            }

            return verdicts;
        }

        private static Dictionary<NodeId, IdentitySignature> BuildIdentityMap(CompiledProgram program)
        {
            var map = new Dictionary<NodeId, IdentitySignature>();
            foreach (var entry in program.DebugMap)
            {
                map[entry.AuthoringNodeId] = new IdentitySignature(program.Nodes[(int)entry.RuntimeNodeIndex]);
            }

            return map;
        }

        // --- Category 1: parameter edit ---
        [Test]
        public void ParameterEdit_ClassifiesCompatibleMigrateForEveryNode()
        {
            var before = Compile(RepeaterTree(count: 3));
            var after = Compile(RepeaterTree(count: 5));

            var verdicts = Classify(before, after);

            Assert.That(verdicts[new NodeId("root")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("leaf")], Is.EqualTo(Verdict.CompatibleMigrate));
            // The config bytes genuinely differ even though the verdict is compatible -- proves
            // classification is layout-based, not a same-bytes check.
            Assert.That(before.ConfigBlob, Is.Not.EqualTo(after.ConfigBlob));
        }

        // --- Category 2: insertion ---
        [Test]
        public void Insertion_NewNodeIsCompatibleNew_ExistingNodesStillMigrate()
        {
            var before = Compile(SequenceTree("root", "first"));
            var after = Compile(SequenceTree("root", "first", "second"));

            var verdicts = Classify(before, after);

            Assert.That(verdicts[new NodeId("root")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("first")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("second")], Is.EqualTo(Verdict.CompatibleNew));
        }

        // --- Category 3: removal ---
        [Test]
        public void Removal_RemovedNodeIsCompatibleDropped_SurvivingNodesStillMigrate()
        {
            var before = Compile(SequenceTree("root", "first", "second"));
            var after = Compile(SequenceTree("root", "first"));

            var verdicts = Classify(before, after);

            Assert.That(verdicts[new NodeId("root")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("first")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("second")], Is.EqualTo(Verdict.CompatibleDropped));
        }

        // --- Category 4: reordering ---
        [Test]
        public void Reordering_NodeIdentitiesStillMigrate_ButCompiledIndicesShift()
        {
            var before = Compile(SequenceTree("root", "first", "second"));
            var after = Compile(SequenceTree("root", "second", "first"));

            var verdicts = Classify(before, after);
            Assert.That(verdicts[new NodeId("first")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("second")], Is.EqualTo(Verdict.CompatibleMigrate));

            // The load-bearing fact this category exists to demonstrate: "first" and "second"
            // occupy DIFFERENT compiled indices before vs. after, even though nothing about
            // either node itself changed -- any live-state array indexed by compiled index
            // (ReferenceExecutionMachine's _activationGenerations, node memory slices, etc.)
            // would silently read the WRONG node's state after this reorder unless migration is
            // keyed by stable NodeId, never by compiled index.
            var firstIndexBefore = before.DebugMap.Single(e => e.AuthoringNodeId == new NodeId("first")).RuntimeNodeIndex;
            var firstIndexAfter = after.DebugMap.Single(e => e.AuthoringNodeId == new NodeId("first")).RuntimeNodeIndex;
            Assert.That(firstIndexAfter, Is.Not.EqualTo(firstIndexBefore),
                "Reordering must shift compiled indices, or this category would not exercise the risk it exists to catch.");
        }

        // --- Category 5: type-version change ---
        [Test]
        public void TypeChange_ClassifiesIncompatible_SiblingsStillMigrate()
        {
            var before = new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.spike"), "Spike", new NodeId("root"),
                new[]
                {
                    Node("root", BuiltInNodeManifests.MemorySequenceTypeId, "changing", "stable"),
                    Node("changing", ReferenceFixtureNodeManifests.SuccessTypeId),
                    Node("stable", ReferenceFixtureNodeManifests.FailureTypeId),
                },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var after = new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.spike"), "Spike", new NodeId("root"),
                new[]
                {
                    Node("root", BuiltInNodeManifests.MemorySequenceTypeId, "changing", "stable"),
                    Node("changing", ReferenceFixtureNodeManifests.RunningTypeId), // same NodeId, different type
                    Node("stable", ReferenceFixtureNodeManifests.FailureTypeId),
                },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);

            var verdicts = Classify(Compile(before), Compile(after));

            Assert.That(verdicts[new NodeId("changing")], Is.EqualTo(Verdict.IncompatibleTypeOrVersionChange));
            Assert.That(verdicts[new NodeId("stable")], Is.EqualTo(Verdict.CompatibleMigrate));
            Assert.That(verdicts[new NodeId("root")], Is.EqualTo(Verdict.CompatibleMigrate));
        }

        // --- helpers ---

        private static CompiledProgram Compile(TreeDocument document)
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build().Registry;
            var options = new ReferenceCompilerOptions(
                "trees/spike.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0));
            var result = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Program;
        }

        private static TreeDocument RepeaterTree(uint count)
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(true)),
                new SemanticProperty("count", SemanticValue.FromUInt64(count)),
            });
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.RepeaterTypeId, 1,
                new[] { new NodeId("leaf") }, parameters, tags: TagSet.Empty);
            var leaf = Node("leaf", ReferenceFixtureNodeManifests.SuccessTypeId);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.spike"), "Spike", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static TreeDocument SequenceTree(string rootId, params string[] childIds)
        {
            var nodes = new List<NodeDocument>
            {
                Node(rootId, BuiltInNodeManifests.MemorySequenceTypeId, childIds),
            };
            nodes.AddRange(childIds.Select(id => Node(id, ReferenceFixtureNodeManifests.SuccessTypeId)));
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.spike"), "Spike", new NodeId(rootId), nodes,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }

        private static NodeDocument Node(string id, string typeId, params string[] children) =>
            new NodeDocument(
                new NodeId(id), typeId, 1, children.Select(child => new NodeId(child)),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
    }
}
