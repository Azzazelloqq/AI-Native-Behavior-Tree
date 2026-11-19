using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Burst;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 1 (ADR-P11-002 §7): managed/native parity. One tree written with the managed
    /// aibt.unity.* nodes runs on a <see cref="BehaviorTreeAgent"/>; the same tree with the matching
    /// aibt.native.* nodes runs on a ProductionTreeHost. Over a scripted sequence of facts both give
    /// the same root result and the same value in every key. Each step starts both trees from the
    /// keys' defaults (a native host does not keep values between runs), writes the step's facts and
    /// runs one update. Ints and bools must be equal; floats within <see cref="Tolerance"/> (relative,
    /// with an absolute floor), see determinism-v1.md:12: managed float code is not bit-pinned.
    /// </summary>
    public sealed class NativeLibraryParityTests
    {
        private const float Tolerance = 1e-5f;

        private readonly struct Pair
        {
            internal Pair(string id, string nativeType, string nativeParameters, string nativeBindings, string managedType, string managedParameters)
            {
                Id = id; NativeType = nativeType; NativeParameters = nativeParameters; NativeBindings = nativeBindings;
                ManagedType = managedType; ManagedParameters = managedParameters;
            }

            internal string Id { get; }
            internal string NativeType { get; }
            internal string NativeParameters { get; }
            internal string NativeBindings { get; }
            internal string ManagedType { get; }
            internal string ManagedParameters { get; }
        }

        private sealed class Step
        {
            internal bool Fa;
            internal int Ia, Ib;
            internal float Fa1, Fb1;
            internal Vector3 Pa, Pb;
        }

        private static readonly Step[] Steps =
        {
            new Step(),
            new Step { Fa = true, Ia = 3, Ib = -2, Fa1 = 0.5f, Fb1 = 0.5f, Pa = Vector3.zero, Pb = new Vector3(3f, 4f, 0f) },
            new Step { Fa = false, Ia = 4, Ib = 4, Fa1 = -1.25f, Fb1 = 2f, Pa = new Vector3(1f, 1f, 1f), Pb = new Vector3(4f, 1f, 5f) },
            new Step { Fa = true, Ia = -7, Ib = 9, Fa1 = 3.75f, Fb1 = -0.5f, Pa = new Vector3(0f, 10f, 0f), Pb = new Vector3(3f, 0f, 4f) },
            new Step { Fa = false, Ia = int.MaxValue, Ib = int.MaxValue, Fa1 = 1e-3f, Fb1 = 1e-3f, Pa = new Vector3(-2.5f, 0.5f, 7f), Pb = new Vector3(-2.5f, 0.5f, 7f) },
            new Step { Fa = true, Ia = int.MinValue, Ib = 0, Fa1 = 1234.5f, Fb1 = -1234.5f, Pa = new Vector3(100f, -3f, 0.25f), Pb = Vector3.zero },
            new Step { Fa = true, Ia = 2, Ib = 3, Fa1 = 0.4999f, Fb1 = 0.5f, Pa = new Vector3(0.1f, 0.2f, 0.3f), Pb = new Vector3(-0.3f, 0.2f, -0.1f) },
        };

        private static readonly (string Key, string Type)[] Facts =
        {
            ("fa", "Bool"), ("ia", "Int32"), ("ib", "Int32"), ("fa1", "Float32"), ("fb1", "Float32"), ("pa", "Float3"), ("pb", "Float3"),
        };

        private static readonly (string Key, string Type)[] Written =
        {
            ("r-int-at-least", "Bool"), ("r-int-equals", "Bool"), ("r-int-keys", "Bool"), ("r-float-keys", "Bool"),
            ("r-within", "Bool"), ("r-within-flat", "Bool"), ("r-is-set", "Bool"), ("r-bool-is-true", "Bool"), ("r-float-at-least", "Bool"),
            ("r-succeed", "Bool"), ("r-fail", "Bool"),
            ("w-bool", "Bool"), ("w-int", "Int32"), ("w-float", "Float32"), ("w-vec", "Float3"),
            ("c-bool", "Bool"), ("c-int", "Int32"), ("c-float", "Float32"), ("c-vec", "Float3"),
            ("n-int", "Int32"), ("n-float", "Float32"), ("t-bool", "Bool"), ("d", "Float32"), ("d-flat", "Float32"),
        };

        // Each probe condition is recorded in a key: succeeder(memory-sequence(condition, set-bool key true)).
        private static readonly (string Id, string Record, Pair Condition)[] Conditions =
        {
            ("int-at-least", "r-int-at-least", new Pair("int-at-least", "aibt.native.int-at-least", "\"threshold\": 3", "\"key\": \"ia\"",
                "aibt.unity.compare-int", "\"key\": \"ia\", \"comparison\": \"greater-or-equal\", \"value\": 3")),
            ("int-equals", "r-int-equals", new Pair("int-equals", "aibt.native.int-equals", "\"value\": 4", "\"key\": \"ia\"",
                "aibt.unity.compare-int", "\"key\": \"ia\", \"comparison\": \"equal\", \"value\": 4")),
            ("int-keys", "r-int-keys", new Pair("int-keys", "aibt.native.int-keys-at-least", null, "\"left\": \"ia\", \"right\": \"ib\"",
                "aibt.unity.compare-int-keys", "\"left\": \"ia\", \"comparison\": \"greater-or-equal\", \"right\": \"ib\"")),
            ("float-keys", "r-float-keys", new Pair("float-keys", "aibt.native.float-keys-at-least", null, "\"left\": \"fa1\", \"right\": \"fb1\"",
                "aibt.unity.compare-float-keys", "\"left\": \"fa1\", \"comparison\": \"greater-or-equal\", \"right\": \"fb1\"")),
            // The managed node measures from the agent's transform, which each step places at 'pa'.
            ("within", "r-within", new Pair("within", "aibt.native.float3-within-distance", "\"distance\": 5.0, \"flat\": false", "\"a\": \"pa\", \"b\": \"pb\"",
                "aibt.unity.is-within-distance", "\"target\": \"pb\", \"distance\": 5.0, \"keep-height\": false")),
            ("within-flat", "r-within-flat", new Pair("within-flat", "aibt.native.float3-within-distance", "\"distance\": 5.0, \"flat\": true", "\"a\": \"pa\", \"b\": \"pb\"",
                "aibt.unity.is-within-distance", "\"target\": \"pb\", \"distance\": 5.0, \"keep-height\": true")),
            ("is-set", "r-is-set", new Pair("is-set", "aibt.native.float3-is-set", null, "\"key\": \"pb\"",
                "aibt.unity.is-vector3-set", "\"key\": \"pb\"")),
            ("bool-is-true", "r-bool-is-true", new Pair("bool-is-true", "aibt.native.bool-is-true", null, "\"key\": \"fa\"",
                "aibt.unity.is-true", "\"key\": \"fa\"")),
            ("float-at-least", "r-float-at-least", new Pair("float-at-least", "aibt.native.float-at-least", "\"threshold\": 0.5", "\"key\": \"fa1\"",
                "aibt.unity.compare-float", "\"key\": \"fa1\", \"comparison\": \"greater-or-equal\", \"value\": 0.5")),
            ("succeed", "r-succeed", new Pair("succeed", "aibt.native.succeed", null, null, "aibt.unity.succeed", null)),
            ("fail", "r-fail", new Pair("fail", "aibt.native.fail", null, null, "aibt.unity.fail", null)),
        };

        private static readonly Pair[] Actions =
        {
            new Pair("set-bool", "aibt.native.set-bool", "\"value\": true", "\"key\": \"w-bool\"", "aibt.unity.set-bool", "\"key\": \"w-bool\", \"value\": true"),
            new Pair("set-int", "aibt.native.set-int", "\"value\": 42", "\"key\": \"w-int\"", "aibt.unity.set-int", "\"key\": \"w-int\", \"value\": 42"),
            new Pair("set-float", "aibt.native.set-float", "\"value\": 2.5", "\"key\": \"w-float\"", "aibt.unity.set-float", "\"key\": \"w-float\", \"value\": 2.5"),
            new Pair("set-float3", "aibt.native.set-float3", "\"x\": 1.5, \"y\": -2.0, \"z\": 3.0", "\"key\": \"w-vec\"",
                "aibt.unity.set-vector3", "\"key\": \"w-vec\", \"x\": 1.5, \"y\": -2.0, \"z\": 3.0"),
            new Pair("copy-bool", "aibt.native.copy-bool", null, "\"from\": \"fa\", \"to\": \"c-bool\"", "aibt.unity.copy-bool", "\"from\": \"fa\", \"to\": \"c-bool\""),
            new Pair("copy-int", "aibt.native.copy-int", null, "\"from\": \"ia\", \"to\": \"c-int\"", "aibt.unity.copy-int", "\"from\": \"ia\", \"to\": \"c-int\""),
            new Pair("copy-float", "aibt.native.copy-float", null, "\"from\": \"fa1\", \"to\": \"c-float\"", "aibt.unity.copy-float", "\"from\": \"fa1\", \"to\": \"c-float\""),
            new Pair("copy-float3", "aibt.native.copy-float3", null, "\"from\": \"pa\", \"to\": \"c-vec\"", "aibt.unity.copy-vector3", "\"from\": \"pa\", \"to\": \"c-vec\""),
            new Pair("copy-to-counter", "aibt.native.copy-int", null, "\"from\": \"ib\", \"to\": \"n-int\"", "aibt.unity.copy-int", "\"from\": \"ib\", \"to\": \"n-int\""),
            new Pair("increment-int", "aibt.native.increment-int", "\"amount\": 5", "\"key\": \"n-int\"", "aibt.unity.increment-int", "\"key\": \"n-int\", \"amount\": 5"),
            new Pair("copy-to-meter", "aibt.native.copy-float", null, "\"from\": \"fb1\", \"to\": \"n-float\"", "aibt.unity.copy-float", "\"from\": \"fb1\", \"to\": \"n-float\""),
            new Pair("increment-float", "aibt.native.increment-float", "\"amount\": -0.75", "\"key\": \"n-float\"", "aibt.unity.increment-float", "\"key\": \"n-float\", \"amount\": -0.75"),
            new Pair("copy-to-toggle", "aibt.native.copy-bool", null, "\"from\": \"fa\", \"to\": \"t-bool\"", "aibt.unity.copy-bool", "\"from\": \"fa\", \"to\": \"t-bool\""),
            new Pair("toggle-bool", "aibt.native.toggle-bool", null, "\"key\": \"t-bool\"", "aibt.unity.toggle-bool", "\"key\": \"t-bool\""),
            new Pair("distance", "aibt.native.float3-distance", "\"flat\": false", "\"a\": \"pa\", \"b\": \"pb\", \"result\": \"d\"",
                "aibt.unity.distance", "\"from\": \"pa\", \"to\": \"pb\", \"result\": \"d\", \"flat\": false"),
            new Pair("distance-flat", "aibt.native.float3-distance", "\"flat\": true", "\"a\": \"pa\", \"b\": \"pb\", \"result\": \"d-flat\"",
                "aibt.unity.distance", "\"from\": \"pa\", \"to\": \"pb\", \"result\": \"d-flat\", \"flat\": true"),
        };

        // The root result depends on the facts: after every probe and action, ia >= ib decides.
        private static readonly Pair Gate = new Pair("gate", "aibt.native.int-keys-at-least", null, "\"left\": \"ia\", \"right\": \"ib\"",
            "aibt.unity.compare-int-keys", "\"left\": \"ia\", \"comparison\": \"greater-or-equal\", \"right\": \"ib\"");

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown()
        {
            _kit.Dispose();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            _created.Clear();
        }

        private static string Leaf(Pair pair, bool native)
            => native
                ? LibraryTrees.Leaf(pair.Id, pair.NativeType, pair.NativeParameters, pair.NativeBindings)
                : LibraryTrees.Leaf(pair.Id, pair.ManagedType, pair.ManagedParameters);

        private static string Mark(string id, string key, bool native)
            => native
                ? LibraryTrees.Leaf(id, "aibt.native.set-bool", "\"value\": true", "\"key\": \"" + key + "\"")
                : LibraryTrees.Leaf(id, "aibt.unity.set-bool", "\"key\": \"" + key + "\", \"value\": true");

        private static IEnumerable<string> Keys()
            => Facts.Concat(Written).Select(item =>
                item.Type == "Bool" ? Bool(item.Key)
                : item.Type == "Int32" ? Int(item.Key)
                : item.Type == "Float32" ? Float(item.Key)
                : Float3(item.Key));

        private static string ProbeTree(bool native)
        {
            var nodes = new List<string>();
            var children = new List<string>();
            foreach (var (id, record, condition) in Conditions)
            {
                children.Add("probe-" + id);
                nodes.Add(Composite("probe-" + id, "aibt.core.succeeder", "seq-" + id));
                nodes.Add(Composite("seq-" + id, "aibt.core.memory-sequence", condition.Id, "mark-" + id));
                nodes.Add(Leaf(condition, native));
                nodes.Add(Mark("mark-" + id, record, native));
            }
            foreach (var action in Actions)
            {
                children.Add(action.Id);
                nodes.Add(Leaf(action, native));
            }
            children.Add(Gate.Id);
            nodes.Add(Leaf(Gate, native));
            nodes.Add(Composite("root", "aibt.core.memory-sequence", children.ToArray()));
            return Tree(native ? "tests.p11002.parity.native" : "tests.p11002.parity.managed", "root", Keys(), nodes.ToArray());
        }

        private static string IdleTree(bool native)
            => Tree(native ? "tests.p11002.parity-idle.native" : "tests.p11002.parity-idle.managed", "root", new[] { Bool("fa"), Bool("w-bool") },
                Composite("root", "aibt.core.reactive-selector", "go", "idle"),
                Composite("go", "aibt.core.reactive-sequence", "go-check", "go-mark"),
                native ? LibraryTrees.Leaf("go-check", "aibt.native.bool-is-true", null, "\"key\": \"fa\"") : LibraryTrees.Leaf("go-check", "aibt.unity.is-true", "\"key\": \"fa\""),
                Mark("go-mark", "w-bool", native),
                native ? LibraryTrees.Leaf("idle", "aibt.native.idle") : LibraryTrees.Leaf("idle", "aibt.unity.idle"));

        [Test]
        public void ManagedAndNativeTreesGiveTheSameValuesAndRootResults_OverAScriptedFactSequence()
        {
            var native = _kit.Prepare(ProbeTree(native: true));
            var agent = ManagedAgent(ProbeTree(native: false));
            var roots = new List<NodeStatus?>();
            var conditionOutcomes = new HashSet<string>();
            for (var index = 0; index < Steps.Length; index++)
            {
                var step = Steps[index];
                var host = _kit.Start(native);
                ApplyNative(host, step);
                var nativeRoot = host.Update();
                var managedRoot = RunManaged(agent, step);
                var context = "step " + index;

                Assert.That(nativeRoot, Is.EqualTo(managedRoot), context + ": root result");
                roots.Add(nativeRoot);
                foreach (var (key, type) in Facts.Concat(Written))
                    AssertSame(host, agent.Blackboard, key, type, context);
                foreach (var (_, record, _) in Conditions)
                    conditionOutcomes.Add(record + "=" + host.ReadBool(record));
            }

            // The scenario must exercise what it compares: both root results, and every condition both ways.
            Assert.That(roots, Does.Contain((NodeStatus?)NodeStatus.Success), "some step succeeds");
            Assert.That(roots, Does.Contain((NodeStatus?)NodeStatus.Failure), "some step fails");
            foreach (var (_, record, condition) in Conditions)
            {
                if (condition.Id == "succeed" || condition.Id == "fail") continue;
                Assert.That(conditionOutcomes, Does.Contain(record + "=True"), record + " is seen true");
                Assert.That(conditionOutcomes, Does.Contain(record + "=False"), record + " is seen false");
            }
        }

        [Test]
        public void IdleKeepsBothTreesRunningUntilTheHigherPriorityBranchApplies()
        {
            var native = _kit.Prepare(IdleTree(native: true));
            foreach (var go in new[] { false, true, false, true })
            {
                var host = _kit.Start(native);
                host.Fact("fa", go);
                var nativeRoot = host.Update();
                // A fresh agent per step, like the fresh native host: restarting a BehaviorTreeAgent whose
                // tree is still running idle faults with AIBT4001 (observed in P11-002, outside its scope).
                var agent = ManagedAgent(IdleTree(native: false));
                agent.Blackboard.Set("fa", go);
                agent.Tick(0.1f);
                Assert.That(agent.HasFaulted, Is.False, agent.LastError);
                var managedRoot = agent.IsRunning ? (NodeStatus?)null : agent.LastResult;

                Assert.That(nativeRoot, Is.EqualTo(managedRoot), "fa = " + go);
                Assert.That(nativeRoot, Is.EqualTo(go ? (NodeStatus?)NodeStatus.Success : null), "fa = " + go);
                Assert.That(host.ReadBool("w-bool"), Is.EqualTo(agent.Blackboard.Get<bool>("w-bool")), "fa = " + go);
            }
        }

        private BehaviorTreeAgent ManagedAgent(string json)
        {
            var gameObject = new GameObject("AIBT.Tests.P11002.ManagedParityAgent");
            _created.Add(gameObject);
            var agent = gameObject.AddComponent<BehaviorTreeAgent>();
            agent.TickMode = AgentTickMode.Manual;
            agent.RestartOnComplete = false;
            var source = BehaviorTreeSource.FromJson(json, "p11002-parity");
            _created.Add(source);
            agent.Tree = source;
            Assert.That(agent.Blackboard, Is.Not.Null, agent.LastError);
            return agent;
        }

        private static NodeStatus? RunManaged(BehaviorTreeAgent agent, Step step)
        {
            if (agent.IsRunning) agent.Stop();
            agent.Restart(true);
            agent.transform.position = step.Pa;
            agent.Blackboard.Set("fa", step.Fa);
            agent.Blackboard.Set("ia", step.Ia);
            agent.Blackboard.Set("ib", step.Ib);
            agent.Blackboard.Set("fa1", step.Fa1);
            agent.Blackboard.Set("fb1", step.Fb1);
            agent.Blackboard.Set("pa", step.Pa);
            agent.Blackboard.Set("pb", step.Pb);
            agent.Tick(0.1f);
            Assert.That(agent.HasFaulted, Is.False, agent.LastError);
            return agent.IsRunning ? (NodeStatus?)null : agent.LastResult;
        }

        private static void ApplyNative(LibraryHost host, Step step)
        {
            host.Fact("fa", step.Fa);
            host.Fact("ia", step.Ia);
            host.Fact("ib", step.Ib);
            host.Fact("fa1", step.Fa1);
            host.Fact("fb1", step.Fb1);
            host.Fact("pa", new Float3Value(step.Pa.x, step.Pa.y, step.Pa.z));
            host.Fact("pb", new Float3Value(step.Pb.x, step.Pb.y, step.Pb.z));
        }

        private static void AssertSame(LibraryHost host, AgentBlackboard managed, string key, string type, string context)
        {
            context += ", key " + key;
            switch (type)
            {
                case "Bool": Assert.That(host.ReadBool(key), Is.EqualTo(managed.Get<bool>(key)), context); break;
                case "Int32": Assert.That(host.ReadInt(key), Is.EqualTo(managed.Get<int>(key)), context); break;
                case "Float32": AssertClose(host.ReadFloat(key), managed.Get<float>(key), context); break;
                default:
                    var vector = host.ReadFloat3(key);
                    var expected = managed.Get<Vector3>(key);
                    AssertClose(vector.X, expected.x, context + ".x");
                    AssertClose(vector.Y, expected.y, context + ".y");
                    AssertClose(vector.Z, expected.z, context + ".z");
                    break;
            }
        }

        private static void AssertClose(float native, float managed, string context)
            => Assert.That(native, Is.EqualTo(managed).Within(Math.Max(Tolerance, Math.Abs(managed) * Tolerance)), context);
    }
}
