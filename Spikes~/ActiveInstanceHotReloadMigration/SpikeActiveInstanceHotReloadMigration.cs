using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.ActiveInstanceHotReloadMigrationSpike
{
    /// <summary>
    /// P6-018 disposable spike. Tests whether full active-frame-stack migration (not just idle
    /// per-node state) is achievable, against a real, actively-executing (mid-tick, not idle)
    /// <see cref="ReferenceExecutionMachine"/>.
    /// <para>
    /// Real, corrected finding: <c>ADR-P5-001</c>'s own implementation addendum named
    /// <c>ReferenceFrame</c>'s "read-only <c>NodeIndex</c> and extensive per-decorator-type fields"
    /// as the blocker. Reading <c>Runtime/State/Reference/ReferenceFrame.cs</c> directly shows this
    /// is not accurate: <c>NodeIndex</c> is the *only* read-only field (set once at construction,
    /// which migration needs anyway -- a fresh frame always needs the NEW compiled index); every
    /// other field (all 30+ decorator/parallel/repeater/cooldown/reactive/abort fields) already has
    /// a normal settable property. The actual gap is structural, not a field-mutability problem:
    /// <see cref="ReferenceExecutionMachine"/> exposes no accessor for its own frame stack at all
    /// (<c>_frames</c> is <c>private</c>, unlike the per-node state <c>CaptureNodeState</c>/
    /// <c>SeedNodeState</c> already expose as <c>internal</c>).
    /// </para>
    /// <para>
    /// Per this card's own Forbidden-changes clause, no production file may gain the missing
    /// accessor -- so this spike uses reflection <b>only</b> to reach the private <c>_frames</c>
    /// field, standing in for the future implementation card's real
    /// <c>CaptureFrameStack</c>/<c>SeedFrameStack</c> methods (mirroring <c>CaptureNodeState</c>/
    /// <c>SeedNodeState</c>'s own already-accepted shape and precondition: valid only before the
    /// fresh instance's first update). Every other type involved (<c>ReferenceFrame</c>) is used
    /// directly, with real internal-visible access -- no reflection needed there, since this test
    /// assembly already has <c>InternalsVisibleTo</c> from <c>AIBT.Runtime</c>.
    /// </para>
    /// Archived to <c>Spikes~/ActiveInstanceHotReloadMigration/</c> once proven.
    /// </summary>
    public sealed class SpikeActiveInstanceHotReloadMigration
    {
        [Test]
        public void FrameStackFieldsAreAllSettable_ExceptNodeIndex_WhichMigrationNeedsFreshAnyway()
        {
            var properties = typeof(ReferenceFrame).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var readOnly = properties.Where(p => !p.CanWrite).Select(p => p.Name).ToArray();

            Assert.That(readOnly, Is.EqualTo(new[] { "NodeIndex" }),
                "ReferenceFrame's ONLY read-only property must be NodeIndex -- if this fails, a newer field was added that " +
                "changes the picture and the ADR's own claim needs re-verification, not silent acceptance.");
        }

        [Test]
        public void ActiveMidTickFrameStack_MigratesFieldForField_AndTheMigratedInstanceKeepsRunningCorrectly()
        {
            // Root Repeater(count: 3) directly wrapping a perpetually-Running leaf -- ticking once
            // leaves 2 real, nested active frames with genuine decorator-specific state
            // (RepeaterCount/RepeaterCompleted), exactly the "extensive per-decorator-type fields"
            // scenario ADR-P5-001's own addendum worried about, not a trivial single-leaf case.
            var oldProgram = Compile(RepeaterOverRunningLeafTree(count: 3));
            var oldMachine = CreateMachine(oldProgram);
            var active = oldMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(active.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                "precondition: the old instance must be genuinely active (mid-tick), not idle");

            var oldFrames = GetFrames(oldMachine);
            Assert.That(oldFrames.Count, Is.EqualTo(2), "precondition: repeater and leaf frames must both be active");
            Assert.That(oldFrames[0].RepeaterCount, Is.EqualTo(3u));

            // A genuine parameter edit (count 3 -> 5) -- already-accepted Migrate category per
            // HotReloadStateMigrationTests' own idle-instance precedent -- touching none of the
            // active path's node types, so identity/compiled indices are unchanged here.
            var newProgram = Compile(RepeaterOverRunningLeafTree(count: 5));
            var oldMap = HotReloadProgramIdentityMap.Build(oldProgram);
            var newMap = HotReloadProgramIdentityMap.Build(newProgram);
            var classification = HotReloadCompatibilityClassifier.Classify(oldProgram, newProgram);

            Assert.That(classification.RequiresFullRestart, Is.False);
            foreach (var nodeId in oldMap.NodeIds)
            {
                Assert.That(classification.NodeVerdicts[nodeId].Category, Is.EqualTo(HotReloadNodeVerdictCategory.Migrate),
                    $"precondition: every active-path node ('{nodeId}') must classify Migrate for this test to prove what it claims");
            }

            var freshMachine = CreateMachine(newProgram);

            // Per-node state: the EXISTING, already-accepted CaptureNodeState/SeedNodeState pair
            // (P5-006), unmodified -- proves this decision is additive to, not a replacement of, the
            // idle-instance mechanism.
            foreach (var nodeId in oldMap.NodeIds)
            {
                if (!oldMap.TryGetRuntimeIndex(nodeId, out var oldIndex)) continue;
                if (!newMap.TryGetRuntimeIndex(nodeId, out var newIndex)) continue;
                var snapshot = oldMachine.CaptureNodeState(new RuntimeNodeIndex(oldIndex));
                freshMachine.SeedNodeState(new RuntimeNodeIndex(newIndex), snapshot);
            }

            // The genuinely new piece: the frame stack itself. Reflection stands in for the future
            // implementation card's real internal accessor -- see this class's own doc comment.
            var reverseOldIndex = BuildReverseIndex(oldProgram);
            var migratedFrames = new List<ReferenceFrame>(oldFrames.Count);
            foreach (var oldFrame in oldFrames)
            {
                var nodeId = reverseOldIndex[oldFrame.NodeIndex.Value];
                Assert.That(newMap.TryGetRuntimeIndex(nodeId, out var newIndex), Is.True,
                    $"every active-path node ('{nodeId}') must resolve in the new program for a Migrate-only path");
                migratedFrames.Add(CopyFrameExceptNodeIndex(oldFrame, new RuntimeNodeIndex(newIndex)));
            }
            SetFrames(freshMachine, migratedFrames);

            // Decisive proof, part A: every field transferred exactly (not reset), field for field,
            // via reflection over every property ReferenceFrame declares except NodeIndex.
            var newFrames = GetFrames(freshMachine);
            Assert.That(newFrames.Count, Is.EqualTo(oldFrames.Count));
            var frameProperties = typeof(ReferenceFrame)
                .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(p => p.Name != nameof(ReferenceFrame.NodeIndex))
                .ToArray();
            for (var depth = 0; depth < oldFrames.Count; depth++)
            {
                foreach (var property in frameProperties)
                {
                    Assert.That(property.GetValue(newFrames[depth]), Is.EqualTo(property.GetValue(oldFrames[depth])),
                        $"depth {depth}, property {property.Name} must match the old instance's own value exactly");
                }
            }
            Assert.That(newFrames[0].RepeaterCount, Is.EqualTo(3u),
                "the migrated repeater's OWN configured count (3) must survive migration even though the new program's default is 5 -- proving real state transfer, not a fresh reactivation under the new program");

            // Decisive proof, part B: the migrated instance is not just superficially seeded -- it
            // keeps running correctly under a real subsequent Update, still mid-repeater-cycle, not
            // rejected/faulted (a machine with incoherent seeded state fails the reference executor's
            // own many internal consistency checks almost immediately).
            var resumed = freshMachine.Update(new ReferenceUpdateContext(1, new Revision(1), 0));
            Assert.That(resumed.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting));
            Assert.That(freshMachine.IsFaulted, Is.False);
        }

        private static Dictionary<uint, NodeId> BuildReverseIndex(CompiledProgram program)
        {
            var map = new Dictionary<uint, NodeId>();
            foreach (var entry in program.DebugMap) map[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            return map;
        }

        private static ReferenceFrame CopyFrameExceptNodeIndex(ReferenceFrame source, RuntimeNodeIndex newNodeIndex)
        {
            var copy = new ReferenceFrame(newNodeIndex);
            foreach (var property in typeof(ReferenceFrame).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (property.Name == nameof(ReferenceFrame.NodeIndex) || !property.CanWrite) continue;
                property.SetValue(copy, property.GetValue(source));
            }
            return copy;
        }

        private static List<ReferenceFrame> GetFrames(ReferenceExecutionMachine machine)
        {
            var field = typeof(ReferenceExecutionMachine).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic);
            return (List<ReferenceFrame>)field.GetValue(machine);
        }

        private static void SetFrames(ReferenceExecutionMachine machine, List<ReferenceFrame> frames)
        {
            var target = GetFrames(machine);
            target.Clear();
            target.AddRange(frames);
        }

        private static ReferenceExecutionMachine CreateMachine(CompiledProgram program)
        {
            return new ReferenceExecutionMachine(
                program, new TreeInstanceId(1),
                ReferenceLeafRegistry.CreatePhase1Fixtures(), null,
                ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                RegisteredBlackboardRegistry.Empty,
                ReferenceObserverConditionRegistry.Empty);
        }

        private static CompiledProgram Compile(TreeDocument document)
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build().Registry;
            var options = new ReferenceCompilerOptions(
                "trees/p6-018-spike.aibt.json", ReferenceCompilationPolicy.Phase1,
                new CompiledCompilerVersion(1, 0, 0, 0));
            var result = ReferenceCompiler.Compile(document, registry, options);
            Assert.That(result.Success, Is.True,
                string.Join(" | ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
            return result.Program;
        }

        private static TreeDocument RepeaterOverRunningLeafTree(uint count)
        {
            var leaf = new NodeDocument(
                new NodeId("leaf"), ReferenceFixtureNodeManifests.RunningTypeId, 1, Array.Empty<NodeId>(),
                parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("stopOnFailure", SemanticValue.FromBoolean(true)),
                new SemanticProperty("count", SemanticValue.FromUInt64(count)),
            });
            var root = new NodeDocument(
                new NodeId("root"), BuiltInNodeManifests.RepeaterTypeId, 1,
                new[] { new NodeId("leaf") }, parameters, tags: TagSet.Empty);
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.p6-018-spike"), "Spec", root.Id, new[] { root, leaf },
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }
    }
}
