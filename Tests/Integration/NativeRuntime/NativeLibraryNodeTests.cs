using System.Collections.Generic;
using System.Linq;
using AIBT.Burst;
using AIBT.StandardNodes;
using NUnit.Framework;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 1 (ADR-P11-002 §7): the 20 library nodes of shard aibt.native.standard run on the
    /// real ProductionTreeHost from assets prepared against a project catalog set that lists the
    /// package shard next to a project shard. One case per contract point (results and written
    /// values, edge cases included). Reactive guards: NativeLibraryReactiveGuardTests.
    /// </summary>
    public sealed class NativeLibraryNodeTests
    {
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        private static IEnumerable<TestCaseData> Cases()
            => LibraryCases.All.Select(c => new TestCaseData(c).SetName("Host_" + c.Name.Replace(' ', '_')));

        [TestCaseSource(nameof(Cases))]
        public void EveryLibraryNodeKeepsItsContractOnProductionTreeHost(LibraryCase libraryCase)
        {
            var host = _kit.Start(_kit.Prepare(libraryCase.Json));
            for (var update = 0; update < libraryCase.Updates; update++) host.Update();

            Assert.That(host.Root, Is.EqualTo(libraryCase.Root), libraryCase.Name + ": root result (null = still running)");
            foreach (var (key, value) in libraryCase.Values)
                AssertValue(host, key, value, libraryCase.Name);
        }

        [Test]
        public void EveryStage1NodeHasBehaviourCases()
        {
            var covered = new HashSet<string>(LibraryCases.All.Select(c => c.Node));
            Assert.That(StandardNativeLibraryIds.Stage1.Where(id => !covered.Contains(id)), Is.Empty);
        }

        [Test]
        public void TheGeneratorAcceptsTheShardNextToAProjectShard_AndOneTreeDispatchesEveryLibraryNode()
        {
            Assert.That(NativeLibraryCatalog.IsUsable, Is.True, "The generated catalog set is usable (no AIBT5xxx refusal).");
            var ids = NativeLibraryKit.Materialize(typeof(StandardNativeShard)).Nodes.Select(node => node.Manifest.TypeId).ToArray();
            Assert.That(ids, Is.EquivalentTo(StandardNativeLibraryIds.All));
            foreach (var node in NativeLibraryKit.Materialize(typeof(StandardNativeShard)).Nodes)
            {
                Assert.That(node.Manifest.Deterministic, Is.True, node.Manifest.TypeId);
                Assert.That(node.Manifest.Category, Is.EqualTo("Native/Blackboard").Or.EqualTo("Native/Flow"), node.Manifest.TypeId);
                Assert.That(node.Manifest.Version, Is.EqualTo(1u), node.Manifest.TypeId);
            }

            // A tree with every stage-1 node (and the project shard's node) compiles, prepares and runs.
            const string S = "aibt.native.";
            var json = Tree("tests.p11002.every-node", "root",
                new[]
                {
                    Int("i", 3), Int("j", -1), Float("f", 1f), Float("g", 0.5f), Float("d"), Bool("b"), Bool("c"),
                    Float3("p", 1f, 0f, 0f), Float3("q", 4f, 0f, 4f), Float3("r"), Float("live-target"),
                },
                Composite("root", "aibt.core.memory-sequence", "project", "c1", "c2", "c3", "c4", "c5", "c6",
                    "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9", "a10", "a11", "flow"),
                Leaf("project", "aibt.tests.external-tree-write-reader", null, "\"live-target\": \"live-target\""),
                Leaf("c1", S + "int-at-least", "\"threshold\": 3", "\"key\": \"i\""),
                Composite("c2", "aibt.core.inverter", "c2-inner"),
                Leaf("c2-inner", S + "int-equals", "\"value\": 4", "\"key\": \"i\""),
                Leaf("c3", S + "int-keys-at-least", null, "\"left\": \"i\", \"right\": \"j\""),
                Leaf("c4", S + "float-keys-at-least", null, "\"left\": \"f\", \"right\": \"g\""),
                Leaf("c5", S + "float3-within-distance", "\"distance\": 5.0, \"flat\": true", "\"a\": \"p\", \"b\": \"q\""),
                Leaf("c6", S + "float3-is-set", null, "\"key\": \"p\""),
                Leaf("a1", S + "set-bool", "\"value\": true", "\"key\": \"b\""),
                Leaf("a2", S + "set-int", "\"value\": 10", "\"key\": \"i\""),
                Leaf("a3", S + "set-float3", "\"x\": 1.0, \"y\": 2.0, \"z\": 3.0", "\"key\": \"r\""),
                Leaf("a4", S + "copy-bool", null, "\"from\": \"b\", \"to\": \"c\""),
                Leaf("a5", S + "copy-int", null, "\"from\": \"j\", \"to\": \"i\""),
                Leaf("a6", S + "copy-float", null, "\"from\": \"g\", \"to\": \"f\""),
                Leaf("a7", S + "copy-float3", null, "\"from\": \"q\", \"to\": \"p\""),
                Leaf("a8", S + "increment-int", "\"amount\": 2", "\"key\": \"i\""),
                Leaf("a9", S + "increment-float", "\"amount\": 0.25", "\"key\": \"f\""),
                Leaf("a10", S + "toggle-bool", null, "\"key\": \"c\""),
                Leaf("a11", S + "float3-distance", "\"flat\": false", "\"a\": \"p\", \"b\": \"r\", \"result\": \"d\""),
                Composite("flow", "aibt.core.memory-selector", "fail", "succeed", "idle"),
                Leaf("fail", S + "fail"),
                Leaf("succeed", S + "succeed"),
                Leaf("idle", S + "idle"));
            var host = _kit.Start(_kit.Prepare(json));
            host.Fact("live-target", 1f);
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success));
            Assert.That(host.ReadBool("b"), Is.True);
            Assert.That(host.ReadBool("c"), Is.False, "copied true, then toggled");
            Assert.That(host.ReadInt("i"), Is.EqualTo(1), "set 10, copied -1, incremented by 2");
            Assert.That(host.ReadFloat("f"), Is.EqualTo(0.75f), "copied 0.5, incremented by 0.25");
            Assert.That(host.ReadFloat3("p"), Is.EqualTo(new Float3Value(4f, 0f, 4f)));
            Assert.That(host.ReadFloat("d"), Is.EqualTo((float)System.Math.Sqrt(14.0)).Within(1e-6f), "|(4,0,4) - (1,2,3)| = sqrt(14)");
        }

        [Test]
        public void IdleRunsUntilAReactiveSelectorAbortsItForAHigherPriorityBranch()
        {
            const string S = "aibt.native.";
            var json = Tree("tests.p11002.idle-abort", "root", new[] { Bool("go"), Bool("done") },
                Composite("root", "aibt.core.reactive-selector", "go-branch", "idle"),
                Composite("go-branch", "aibt.core.reactive-sequence", "go-check", "mark"),
                Leaf("go-check", S + "bool-is-true", null, "\"key\": \"go\""),
                Leaf("mark", S + "set-bool", "\"value\": true", "\"key\": \"done\""),
                Leaf("idle", S + "idle"));
            var host = _kit.Start(_kit.Prepare(json));
            for (var update = 0; update < 3; update++)
                Assert.That(host.Update(), Is.Null, "idle keeps the tree running");
            Assert.That(host.ReadBool("done"), Is.False);

            host.Fact("go", true);
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success), "the higher-priority branch aborts idle and succeeds");
            Assert.That(host.ReadBool("done"), Is.True);
        }

        internal static void AssertValue(LibraryHost host, string key, object expected, string context)
        {
            switch (expected)
            {
                case bool flag: Assert.That(host.ReadBool(key), Is.EqualTo(flag), context + ": " + key); break;
                case int number: Assert.That(host.ReadInt(key), Is.EqualTo(number), context + ": " + key); break;
                case float number: Assert.That(host.ReadFloat(key), Is.EqualTo(number), context + ": " + key); break;
                case Float3Value vector: Assert.That(host.ReadFloat3(key), Is.EqualTo(vector), context + ": " + key); break;
                default: Assert.Fail("Unsupported expected value type " + expected?.GetType()); break;
            }
        }
    }
}
