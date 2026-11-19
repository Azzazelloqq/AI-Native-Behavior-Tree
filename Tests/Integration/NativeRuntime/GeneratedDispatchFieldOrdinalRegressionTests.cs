using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.Simulation;
using NUnit.Framework;
using Unity.Collections;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// Regression (found in P11-002 stage 1, fix allowed by the owner on 2026-11-19):
    /// GeneratedTreeDispatchAdapterV2 must locate a generated blackboard handle by its own case's
    /// configuration field (FirstConfigurationField + the binding's case-local field ordinal), not by
    /// the global field of the same index. With the defect a handle at a case-local field ordinal of 1
    /// or more reads its access ordinal at another field's offset: bootstrap (or TryAddGroup) fails
    /// when that offset holds a parameter, and the first update faults when it holds another handle
    /// of the node. Covers ProductionTreeHost and TreeSimulation (in place and with Jobs).
    /// </summary>
    public sealed class GeneratedDispatchFieldOrdinalRegressionTests
    {
        private const string S = "aibt.native.";
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        // increment-int: fields sort as amount (offset 0), key (offset 4).
        private static string IncrementTree() => Tree("tests.regression.field-ordinal.increment", "n", new[] { Int("i", 5) },
            Leaf("n", S + "increment-int", "\"amount\": 3", "\"key\": \"i\""));

        // copy-int: fields sort as from (offset 0), to (offset 4).
        private static string CopyTree() => Tree("tests.regression.field-ordinal.copy", "n", new[] { Int("a", 7), Int("b") },
            Leaf("n", S + "copy-int", null, "\"from\": \"a\", \"to\": \"b\""));

        // 1 >= 2 fails; reading the left key twice would succeed.
        private static string KeysTree() => Tree("tests.regression.field-ordinal.keys", "n", new[] { Int("left", 1), Int("right", 2) },
            Leaf("n", S + "int-keys-at-least", null, "\"left\": \"left\", \"right\": \"right\""));

        // float3-distance: fields sort as a (0), b (4), flat (8), result (12).
        private static string DistanceTree() => Tree("tests.regression.field-ordinal.distance", "n",
            new[] { Float3("a"), Float3("b", 3f, 4f, 0f), Float("result") },
            Leaf("n", S + "float3-distance", "\"flat\": false", "\"a\": \"a\", \"b\": \"b\", \"result\": \"result\""));

        /// <summary>
        /// Keeps the cases below meaningful: the catalog must contain handles whose case-local field
        /// offset differs from the offset of the global field with the same index.
        /// </summary>
        [Test]
        public void TheCatalogHasHandlesWhoseGlobalFieldOfTheSameIndexSitsAtAnotherOffset()
        {
            Assert.That(NativeLibraryCatalog.TryCreateRuntimeCatalog(Allocator.Persistent, out var catalog, out var failure), Is.True, failure.ToString());
            using (catalog)
            {
                var exposed = 0;
                for (var caseIndex = 0; caseIndex < catalog.Cases.Length; caseIndex++)
                {
                    var dispatchCase = catalog.Cases[caseIndex];
                    for (var local = 0u; local < dispatchCase.BindingCount; local++)
                    {
                        var binding = catalog.Bindings[(int)(dispatchCase.FirstBinding + local)];
                        if (binding.Kind != NativeBurstDispatchBindingKindV2.BlackboardRead
                            && binding.Kind != NativeBurstDispatchBindingKindV2.BlackboardWrite
                            && binding.Kind != NativeBurstDispatchBindingKindV2.BlackboardReadWrite) continue;
                        var own = catalog.ConfigurationFields[(int)(dispatchCase.FirstConfigurationField + binding.ConfigurationFieldOrdinal)];
                        var sameIndex = catalog.ConfigurationFields[(int)binding.ConfigurationFieldOrdinal];
                        Assert.That(own.FieldOrdinal, Is.EqualTo(binding.ConfigurationFieldOrdinal));
                        if (own.ByteOffset != sameIndex.ByteOffset) exposed++;
                    }
                }
                Assert.That(exposed, Is.GreaterThan(0), "No handle would expose a global/local field-index mix-up in this catalog.");
            }
        }

        [Test]
        public void Host_AHandleAfterAParameterBootstrapsAndWritesItsOwnKey()
        {
            var host = _kit.Start(_kit.Prepare(IncrementTree()));
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success));
            Assert.That(host.ReadInt("i"), Is.EqualTo(8));
        }

        [Test]
        public void Host_ASecondHandleWritesItsOwnKey()
        {
            var host = _kit.Start(_kit.Prepare(CopyTree()));
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success));
            Assert.That(host.ReadInt("a"), Is.EqualTo(7));
            Assert.That(host.ReadInt("b"), Is.EqualTo(7));
        }

        [Test]
        public void Host_TwoReadHandlesReadTheirOwnKeys()
        {
            var host = _kit.Start(_kit.Prepare(KeysTree()));
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Failure));
        }

        [Test]
        public void Host_AThirdHandleAfterAParameterWritesItsOwnKey()
        {
            var host = _kit.Start(_kit.Prepare(DistanceTree()));
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success));
            Assert.That(host.ReadFloat("result"), Is.EqualTo(5f).Within(1e-6f));
            Assert.That(host.ReadFloat3("b"), Is.EqualTo(new Float3Value(3f, 4f, 0f)));
        }

        /// <summary>The same four trees as TreeSimulation groups: every group joins, no agent faults, and each root completes with the host's result.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Simulation_EveryHandleResolvesToItsOwnKey(bool useJobs)
        {
            var trees = new[] { IncrementTree(), CopyTree(), KeysTree(), DistanceTree() };
            var expected = new[] { NodeStatus.Success, NodeStatus.Success, NodeStatus.Failure, NodeStatus.Success };
            var settings = TreeSimulationSettings.Default(AIBT.Tests.Integration.Simulation.SimulationTestPrograms.TickMicroseconds);
            settings.UseJobs = useJobs;
            settings.MinimumJobBatch = 1;
            using (var simulation = new TreeSimulation(settings))
            {
                var groups = new TreeGroup[trees.Length];
                for (var index = 0; index < trees.Length; index++)
                {
                    Assert.That(simulation.TryAddGroup(_kit.Prepare(trees[index]).Program, TreeGroupSettings.Default(1), out groups[index]),
                        Is.EqualTo(SimulationResult.Success), "group " + index);
                    Assert.That(groups[index].TryAdd(ThinkSchedule.Every(1u, 0u), out _), Is.EqualTo(SimulationResult.Success));
                }
                Assert.That(simulation.Step(1u), Is.EqualTo(SimulationResult.Success));
                for (var index = 0; index < trees.Length; index++)
                {
                    Assert.That(groups[index].GetFaults().Length, Is.Zero, "group " + index + " faulted");
                    var completions = groups[index].GetRootCompletions();
                    Assert.That(completions.Length, Is.EqualTo(1), "group " + index);
                    Assert.That(completions[0].Result, Is.EqualTo(expected[index]), "group " + index);
                }
            }
        }
    }
}
