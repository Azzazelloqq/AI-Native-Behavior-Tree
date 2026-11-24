using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Burst;
using AIBT.StandardNodes;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-007 (review F1): one Tree key bound to two handles of one library node. Each such node
    /// gives the result its contract describes for the same key on both handles: copy-* leaves the
    /// key as it is, *-keys-at-least succeeds (x &gt;= x), float3-within-distance succeeds (distance 0),
    /// float3-distance writes 0, and random-float3-in-radius with center == result steps from the value
    /// the key held before the tick (reads see the value from before the tick; the write applies after
    /// it). Before the fix every such tree compiled, then each host update failed with
    /// NativeLifetimeStateInvalid. The exact random replay is in
    /// <see cref="NativeLibraryRandomTests"/>; here every node runs on the host's own update and, in one
    /// tree, on NativeTreeAgents under the four scheduling policies.
    /// </summary>
    public sealed class NativeLibraryAliasedKeyTests
    {
        private const string S = "aibt.native.";

        /// <summary>Every library node with two blackboard handles of one value type (so one key can name both).</summary>
        internal static readonly string[] AliasableNodes =
        {
            S + "copy-bool", S + "copy-int", S + "copy-float", S + "copy-float3",
            S + "int-keys-at-least", S + "float-keys-at-least", S + "float3-within-distance", S + "float3-distance",
            S + "random-float3-in-radius",
        };

        private static readonly Float3Value P = new Float3Value(1f, 2f, 3f);
        private static readonly Float3Value Home = new Float3Value(10f, 1.5f, -4f);

        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private NativeLibraryKit _kit;
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _kit = new NativeLibraryKit();
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P11007.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
            _kit.Dispose();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void TheCasesCoverEveryLibraryNodeWithTwoHandlesOfOneValueType()
        {
            var found = new List<string>();
            foreach (var type in typeof(StandardNativeShard).Assembly.GetTypes())
            {
                var node = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType == typeof(AibtBurstNodeAttribute));
                if (node == null) continue;
                var config = (Type)node.ConstructorArguments[3].Value;
                var valueTypes = config.GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .SelectMany(field => field.GetCustomAttributesData())
                    .Where(a => a.AttributeType == typeof(AibtBlackboardBindingAttribute))
                    .Select(a => (string)a.ConstructorArguments[3].Value)
                    .ToList();
                if (valueTypes.GroupBy(id => id).Any(group => group.Count() > 1))
                    found.Add((string)node.ConstructorArguments[0].Value);
            }
            Assert.That(found, Is.EquivalentTo(AliasableNodes));
            Assert.That(Cases().Select(c => (string)c.Arguments[1]).Distinct().Concat(new[] { S + "random-float3-in-radius" }),
                Is.EquivalentTo(AliasableNodes), "one host case per node (the random node has its own test)");
        }

        private static IEnumerable<TestCaseData> Cases()
        {
            TestCaseData Case(string name, string node, string parameters, string bindings, string[] keys, (string, object)[] values)
                => new TestCaseData(Tree("tests.p11007." + name, "n", keys, Leaf("n", node, parameters, bindings)), node, values)
                    .SetName("Host_" + name);

            yield return Case("copy-bool", S + "copy-bool", null, "\"from\": \"b\", \"to\": \"b\"",
                new[] { Bool("b", true) }, new (string, object)[] { ("b", true) });
            yield return Case("copy-int", S + "copy-int", null, "\"from\": \"i\", \"to\": \"i\"",
                new[] { Int("i", -7) }, new (string, object)[] { ("i", -7) });
            yield return Case("copy-float", S + "copy-float", null, "\"from\": \"f\", \"to\": \"f\"",
                new[] { Float("f", 2.5f) }, new (string, object)[] { ("f", 2.5f) });
            yield return Case("copy-float3", S + "copy-float3", null, "\"from\": \"p\", \"to\": \"p\"",
                new[] { Float3("p", P.X, P.Y, P.Z) }, new (string, object)[] { ("p", P) });
            yield return Case("int-keys-at-least", S + "int-keys-at-least", null, "\"left\": \"i\", \"right\": \"i\"",
                new[] { Int("i", int.MinValue) }, new (string, object)[] { ("i", int.MinValue) });
            yield return Case("float-keys-at-least", S + "float-keys-at-least", null, "\"left\": \"f\", \"right\": \"f\"",
                new[] { Float("f", -1.25f) }, new (string, object)[] { ("f", -1.25f) });
            yield return Case("float3-within-distance", S + "float3-within-distance", "\"distance\": 0.0, \"flat\": false", "\"a\": \"p\", \"b\": \"p\"",
                new[] { Float3("p", P.X, P.Y, P.Z) }, new (string, object)[] { ("p", P) });
            yield return Case("float3-within-distance-flat", S + "float3-within-distance", "\"distance\": 0.0, \"flat\": true", "\"a\": \"p\", \"b\": \"p\"",
                new[] { Float3("p", P.X, P.Y, P.Z) }, new (string, object)[] { ("p", P) });
            yield return Case("float3-distance", S + "float3-distance", "\"flat\": false", "\"a\": \"p\", \"b\": \"p\", \"result\": \"d\"",
                new[] { Float3("p", P.X, P.Y, P.Z), Float("d", 9f) }, new (string, object)[] { ("p", P), ("d", 0f) });
        }

        [TestCaseSource(nameof(Cases))]
        public void OneKeyOnBothHandles_GivesTheContractResult_OnProductionTreeHost(string json, string node, (string Key, object Value)[] values)
        {
            var host = _kit.Start(_kit.Prepare(json));
            Assert.That(host.Update(), Is.EqualTo(NodeStatus.Success), node);
            foreach (var (key, value) in values) NativeLibraryNodeTests.AssertValue(host, key, value, node);
        }

        /// <summary>
        /// P11-014 item 9. A self-copy writes the value the key already holds, and a self-compare reads one
        /// value twice, so the cases above give the same result whether the aliased handles see the
        /// current value or a stale one, and whether a write is applied or lost. Here an earlier node
        /// changes the key in the same run first. A self-copy that read or wrote a stale value would undo
        /// that write (the final value would be the old one). A self-compare whose left handle saw the value
        /// from before the raise, or whose right handle saw it from before the drop, fails
        /// (old &gt;= high and low &gt;= high are false). A lost raise or drop leaves a value other than "low".
        /// </summary>
        private static IEnumerable<TestCaseData> AfterAWriteCases()
        {
            TestCaseData Case(string name, string node, string[] keys, string[] nodes, (string, object) final)
                => new TestCaseData(Tree("tests.p11014." + name, "root", keys, nodes), node, final).SetName("Host_AfterAWrite_" + name);
            string Copy(string id, string type, string from, string to) => Leaf(id, S + type, null, "\"from\": \"" + from + "\", \"to\": \"" + to + "\"");
            string AtLeast(string id, string type) => Leaf(id, S + type, null, "\"left\": \"k\", \"right\": \"k\"");
            string[] CopyNodes(string type) => new[]
            {
                Composite("root", "aibt.core.memory-sequence", "up", "self-up", "down", "self-down"),
                Copy("up", type, "high", "k"), Copy("self-up", type, "k", "k"),
                Copy("down", type, "low", "k"), Copy("self-down", type, "k", "k"),
            };

            yield return Case("copy-bool", S + "copy-bool",
                new[] { Bool("k", false), Bool("high", true), Bool("low", true) }, CopyNodes("copy-bool"), ("k", true));
            yield return Case("copy-int", S + "copy-int",
                new[] { Int("k", -7), Int("high", 11), Int("low", -20) }, CopyNodes("copy-int"), ("k", -20));
            yield return Case("copy-float", S + "copy-float",
                new[] { Float("k", -1.25f), Float("high", 2.5f), Float("low", -6f) }, CopyNodes("copy-float"), ("k", -6f));
            yield return Case("copy-float3", S + "copy-float3",
                new[] { Float3("k", P.X, P.Y, P.Z), Float3("high", Home.X, Home.Y, Home.Z), Float3("low", -3f, 0.5f, 8f) },
                CopyNodes("copy-float3"), ("k", new Float3Value(-3f, 0.5f, 8f)));
            yield return Case("int-keys-at-least", S + "int-keys-at-least",
                new[] { Int("k", -7), Int("high", 11), Int("low", -20) },
                new[]
                {
                    Composite("root", "aibt.core.memory-sequence", "up", "after-up", "down", "after-down"),
                    Copy("up", "copy-int", "high", "k"), AtLeast("after-up", "int-keys-at-least"),
                    Copy("down", "copy-int", "low", "k"), AtLeast("after-down", "int-keys-at-least"),
                }, ("k", -20));
            yield return Case("float-keys-at-least", S + "float-keys-at-least",
                new[] { Float("k", -1.25f), Float("high", 2.5f), Float("low", -6f) },
                new[]
                {
                    Composite("root", "aibt.core.memory-sequence", "up", "after-up", "down", "after-down"),
                    Copy("up", "copy-float", "high", "k"), AtLeast("after-up", "float-keys-at-least"),
                    Copy("down", "copy-float", "low", "k"), AtLeast("after-down", "float-keys-at-least"),
                }, ("k", -6f));
        }

        [TestCaseSource(nameof(AfterAWriteCases))]
        public void OneKeyOnBothHandles_AfterAnEarlierWrite_KeepsThatWrite_OnProductionTreeHost(string json, string node, (string Key, object Value) final)
        {
            var host = _kit.Start(_kit.Prepare(json));
            NodeStatus? status = null;
            for (var update = 0; update < 16 && (status == null || status == NodeStatus.Running); update++) status = host.Update();
            Assert.That(status, Is.EqualTo(NodeStatus.Success), node);
            NativeLibraryNodeTests.AssertValue(host, final.Key, final.Value, node);
        }

        /// <summary>Every aliasable node in one tree; three agents per policy so the job paths run groups.</summary>
        private static string EveryAliasTree()
            => Tree("tests.p11007.every-alias", "root",
                new[] { Bool("b", true), Int("i", -7), Float("f", 2.5f), Float3("p", P.X, P.Y, P.Z), Float("d", 9f), Float3("w", Home.X, Home.Y, Home.Z) },
                Composite("root", "aibt.core.memory-sequence", "cb", "ci", "cf", "cv", "ik", "fk", "wd", "dd", "rw"),
                Leaf("cb", S + "copy-bool", null, "\"from\": \"b\", \"to\": \"b\""),
                Leaf("ci", S + "copy-int", null, "\"from\": \"i\", \"to\": \"i\""),
                Leaf("cf", S + "copy-float", null, "\"from\": \"f\", \"to\": \"f\""),
                Leaf("cv", S + "copy-float3", null, "\"from\": \"p\", \"to\": \"p\""),
                Leaf("ik", S + "int-keys-at-least", null, "\"left\": \"i\", \"right\": \"i\""),
                Leaf("fk", S + "float-keys-at-least", null, "\"left\": \"f\", \"right\": \"f\""),
                Leaf("wd", S + "float3-within-distance", "\"distance\": 0.0, \"flat\": false", "\"a\": \"p\", \"b\": \"p\""),
                Leaf("dd", S + "float3-distance", "\"flat\": false", "\"a\": \"p\", \"b\": \"p\", \"result\": \"d\""),
                Leaf("rw", S + "random-float3-in-radius", "\"min-radius\": 2.0, \"radius\": 4.0, \"flat\": true", "\"center\": \"w\", \"result\": \"w\""));

        [TestCase("immediate")]
        [TestCase("agent-frame")]
        [TestCase("batched-jobs")]
        [TestCase("pipelined-jobs")]
        public void EveryAliasedNode_RunsUnderEveryPolicy_OnNativeTreeAgents(string policy)
        {
            const int Agents = 3, FrameCap = 4000;
            var asset = _kit.Prepare(EveryAliasTree()).Asset;
            var profile = ProfileFor(policy);
            var agents = new List<NativeTreeAgent>();
            for (var index = 0; index < Agents; index++) agents.Add(Spawn(asset, (ulong)(index + 1), profile));

            var frames = 0;
            while (frames < FrameCap && agents.Any(agent => agent.LastResult == null && agent.LastError == null))
            {
                Frames(1);
                frames++;
            }
            TestContext.WriteLine(policy + ": " + frames + " frames");
            for (var index = 0; index < Agents; index++)
            {
                var agent = agents[index];
                var what = policy + " agent " + index;
                Assert.That(agent.LastError, Is.Null, what);
                Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success), what);
                Assert.That(agent.TryGetValue("b", out bool b) && b, Is.True, what + ": b");
                Assert.That(agent.TryGetValue("i", out int i) && i == -7, Is.True, what + ": i");
                Assert.That(agent.TryGetValue("f", out float f) && f == 2.5f, Is.True, what + ": f");
                Assert.That(agent.TryGetValue("p", out Float3Value p) && p.Equals(P), Is.True, what + ": p");
                Assert.That(agent.TryGetValue("d", out float d) && d == 0f, Is.True, what + ": d");
                Assert.That(agent.TryGetValue("w", out Float3Value w), Is.True, what + ": w");
                Assert.That(w.Y, Is.EqualTo(Home.Y), what + ": flat keeps the height");
                double dx = w.X - Home.X, dz = w.Z - Home.Z;
                Assert.That(Math.Sqrt(dx * dx + dz * dz), Is.InRange(2.0 - 1e-4, 4.0 + 1e-4), what + ": one step from the old value");
            }
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
        }

        // ------------------------------------------------------------------ helpers

        private SchedulingProfile ProfileFor(string policy)
        {
            switch (policy)
            {
                case "immediate": return Forced(SchedulingPolicy.Immediate, false);
                case "batched-jobs":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(false));
                    return Forced(SchedulingPolicy.BatchedJobsSameFrame, false);
                case "pipelined-jobs":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(true));
                    return Forced(SchedulingPolicy.PipelinedJobs, true);
                case "agent-frame":
                    _scheduler.AgentFrameMinimumBatchSize = 1;
                    return SchedulingProfile.JobsAgentFrame;
                default: throw new ArgumentOutOfRangeException(nameof(policy), policy, null);
            }
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p11007." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities JobsCapabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, ulong seed, SchedulingProfile profile)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11007.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Never;
            agent.RandomSeed = seed;
            agent.CatalogFactory = NativeLibraryCatalog.TryCreateRuntimeCatalog;
            agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();
            return agent;
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
