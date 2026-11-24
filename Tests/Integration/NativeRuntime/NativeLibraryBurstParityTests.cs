using System;
using System.Collections.Generic;
using System.Reflection;
using AIBT.StandardNodes;
using NUnit.Framework;
using Unity.Burst;
using UnityEngine;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-014 confirmation U3: <c>aibt.native.random-float3-in-radius</c> with Burst compilation off and
    /// on. The node runs inside the batched lifecycle job (<c>NativeBatchedLifecycleOwnerV1.AdvanceJob</c>,
    /// <c>[BurstCompile]</c>) under a forced <c>BatchedJobsSameFrame</c> profile; with Burst off the same
    /// job runs as managed code. The test records every written point of one agent (fixed seed and
    /// stream id) and compares the two runs bit for bit, flat and 3D.
    /// </summary>
    public sealed class NativeLibraryBurstParityTests
    {
        private const string S = "aibt.native.";
        private const int Frames = 240;

        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private NativeLibraryKit _kit;
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private bool _burstWas;
        private bool _syncWas;

        [SetUp]
        public void SetUp()
        {
            _burstWas = BurstCompiler.Options.EnableBurstCompilation;
            _syncWas = BurstCompiler.Options.EnableBurstCompileSynchronously;
            _kit = new NativeLibraryKit();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyWorld();
            _kit.Dispose();
            BurstCompiler.Options.EnableBurstCompilation = _burstWas;
            BurstCompiler.Options.EnableBurstCompileSynchronously = _syncWas;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void U3_RandomFloat3InRadius_IsBitIdentical_WithBurstOffAndOn(bool flat)
        {
            var json = Tree("tests.p11014.u3-radius", "loop",
                new[] { Float3("c", 10f, 1.5f, -4f), Float3("r") },
                CompositeWith("loop", "aibt.core.repeater", "\"count\": 4000000000, \"stopOnFailure\": false", "cycle"),
                Composite("cycle", "aibt.core.memory-sequence", "pace", "roll"),
                Leaf("pace", "aibt.stdlib.wait", "\"ticks\": 2"),
                Leaf("roll", S + "random-float3-in-radius",
                    "\"min-radius\": 0.5, \"radius\": 7.25, \"flat\": " + (flat ? "true" : "false"),
                    "\"center\": \"c\", \"result\": \"r\""));
            var asset = _kit.Prepare(json).Asset;

            BurstCompiler.Options.EnableBurstCompilation = false;
            var managed = Record(asset, "burst off");
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            BurstCompiler.Options.EnableBurstCompilation = true;
            var burst = Record(asset, "burst on");

            Assert.That(burst.Count, Is.EqualTo(managed.Count));
            var differing = 0;
            var first = -1;
            for (var index = 0; index < managed.Count; index++)
            {
                if (managed[index] == burst[index]) continue;
                differing++;
                if (first < 0) first = index;
            }
            var distinct = new HashSet<(int, int, int)>(managed).Count;
            Debug.Log("P11-014 U3 (" + (flat ? "flat" : "3D") + "): " + managed.Count + " frames, " + distinct
                + " distinct points, " + differing + " differ bit for bit" + (first < 0 ? "" : ", first at frame " + first
                    + ": managed " + Text(managed[first]) + ", burst " + Text(burst[first])) + "; Burst enabled in the second run: "
                + BurstCompiler.IsEnabled);
            Assert.That(distinct, Is.GreaterThan(Frames / 4), "the node drew many points");
            Assert.That(differing, Is.Zero, "the Burst-compiled and the managed job write the same bits");
        }

        private List<(int, int, int)> Record(ProductionTreeAsset asset, string label)
        {
            DestroyWorld();
            var worldObject = Track(new GameObject("AIBT.Tests.P11014.U3.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, false, out var capabilities, out var error), Is.True, error.ToString());
            _scheduler.SetJobsCapabilities(capabilities);
            Assert.That(SchedulingProfile.TryCreate("tests.p11014.u3", 0, 1u, null, null, false, SchedulingPolicy.BatchedJobsSameFrame,
                out var profile, out var profileError), Is.True, profileError.ToString());

            var gameObject = Track(new GameObject("AIBT.Tests.P11014.U3.Agent"));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Never;
            agent.RandomSeed = 20261124ul;
            agent.RandomStreamId = 77ul;
            agent.CatalogFactory = NativeLibraryCatalog.TryCreateRuntimeCatalog;
            agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();

            var points = new List<(int, int, int)>();
            for (var frame = 0; frame < Frames; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                agent.ProcessRootResult();
                Assert.That(agent.LastError, Is.Null, label + " frame " + frame);
                Assert.That(agent.TryGetValue("r", out Float3Value r), Is.True, label + " frame " + frame);
                points.Add((BitConverter.SingleToInt32Bits(r.X), BitConverter.SingleToInt32Bits(r.Y), BitConverter.SingleToInt32Bits(r.Z)));
            }
            return points;
        }

        private static string Text((int X, int Y, int Z) bits)
            => "(" + BitConverter.Int32BitsToSingle(bits.X).ToString("R") + ", " + BitConverter.Int32BitsToSingle(bits.Y).ToString("R")
               + ", " + BitConverter.Int32BitsToSingle(bits.Z).ToString("R") + ")";

        private void DestroyWorld()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
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
