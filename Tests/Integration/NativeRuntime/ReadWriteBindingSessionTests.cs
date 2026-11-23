using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Authoring.BuiltInLeaves;
using AIBT.Burst;
using AIBT.Execution.Burst.Dispatch;
using AIBT.Tests.CodeGen.GenerationP11010;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Integration.NativeRuntime
{
    [AibtCatalogSet("aibt.tests.p11010.read-write-set", 1u, typeof(ReadWriteFixtureShard))]
    public static partial class ReadWriteFixtureCatalog { }

    // The fixture shard next to the built-in leaves (no bindings), so rw-increment stays the largest case.
    [AibtCatalogSet("aibt.tests.p11010.read-write-hold-set", 1u, typeof(BuiltInLeafShard), typeof(ReadWriteFixtureShard))]
    public static partial class ReadWriteHoldCatalog { }

    /// <summary>
    /// P11-010: a native node that reads and then writes one key through a single ReadWrite binding opens
    /// two value sessions in one tick (every TryRead and every TryWrite opens one). The catalog sized the
    /// per-frame session pool and staging bytes from its largest case at one session and one value per
    /// binding, so when that node was the largest case (here: the only one) the write was refused with
    /// CapacityExceeded and the host stopped with NativeLifetimeStateInvalid. The library's increment-int,
    /// increment-float and toggle-bool have this shape; the standard shard's larger cases hid it.
    /// </summary>
    public sealed class ReadWriteBindingSessionTests
    {
        private const string CounterTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11010.read-write"", ""name"": ""P11-010 read-write"",
  ""root"": ""bump"",
  ""blackboard"": {
    ""counter"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 5 }
  },
  ""nodes"": {
    ""bump"": { ""type"": ""aibt.tests.rw-increment"", ""typeVersion"": 1, ""bindings"": { ""counter"": ""counter"" } }
  }
}";

        // Three increments in one update: each node's snapshot follows the previous node's commit, so the
        // value goes 5 -> 8 only if every read sees the write before it.
        private const string ThreeBumpTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11010.read-write-three"", ""name"": ""P11-010 read-write x3"",
  ""root"": ""root"",
  ""blackboard"": {
    ""counter"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 5 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""b1"", ""b2"", ""b3""] },
    ""b1"": { ""type"": ""aibt.tests.rw-increment"", ""typeVersion"": 1, ""bindings"": { ""counter"": ""counter"" } },
    ""b2"": { ""type"": ""aibt.tests.rw-increment"", ""typeVersion"": 1, ""bindings"": { ""counter"": ""counter"" } },
    ""b3"": { ""type"": ""aibt.tests.rw-increment"", ""typeVersion"": 1, ""bindings"": { ""counter"": ""counter"" } }
  }
}";

        // A reactive sequence re-ticks the increment on every update while the wait holds the run open,
        // so each update reads the value the previous update wrote.
        private const string HoldTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11010.read-write-hold"", ""name"": ""P11-010 read-write hold"",
  ""root"": ""root"",
  ""blackboard"": {
    ""counter"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 5 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""bump"", ""hold""] },
    ""bump"": { ""type"": ""aibt.tests.rw-increment"", ""typeVersion"": 1, ""bindings"": { ""counter"": ""counter"" } },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 4000000000 } }
  }
}";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;

        [SetUp]
        public void SetUp()
        {
            var worldObject = Track(new GameObject("AIBT.Tests.P11010.World"));
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
            {
                if (_created[index] is GameObject gameObject)
                {
                    var host = gameObject.GetComponent<ProductionTreeHost>();
                    if (host != null && gameObject.GetComponent<NativeTreeAgent>() == null)
                        InvokePrivate(host, "OnDestroy");
                }
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            }
            _created.Clear();
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
        }

        /// <summary>
        /// The rule the fix states: every case's read-then-write demand fits the catalog's per-frame room,
        /// in the fixture catalog (one ReadWrite node) and in the standard shard's catalog.
        /// </summary>
        [Test]
        public void CatalogCapacity_CoversAReadAndAWriteOfEveryReadWriteBinding()
        {
            AssertDemandFits(ReadWriteFixtureCatalog.TryCreateRuntimeCatalog, "fixture", expectedSessions: 2u, expectedStaging: 8u);
            AssertDemandFits(ReadWriteHoldCatalog.TryCreateRuntimeCatalog, "fixture with built-in leaves", expectedSessions: 2u, expectedStaging: 8u);
            AssertDemandFits(NativeLibraryCatalog.TryCreateRuntimeCatalog, "standard library", expectedSessions: null, expectedStaging: null);
        }

        [TestCase("single", 6)]
        [TestCase("three", 8)]
        public void ProductionTreeHost_ReadsAndWritesOneKeyThroughOneReadWriteBinding(string tree, int expected)
        {
            var compiled = Compile(tree == "single" ? CounterTree : ThreeBumpTree);
            var host = StartHost(compiled);
            Assert.That(host.TryResolveExternalTreeRead<int>("counter", NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                out var read, out var failure), Is.True, failure.Code.ToString());

            host.DriveOneUpdate();

            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None),
                host.LastFailure.Code + " " + host.LastFailure.ResourceKind + " " + host.LastFailure.Requested + "/" + host.LastFailure.Capacity);
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(host.TryReadExternalTreeValue(read, out var counter, out failure), Is.True, failure.Code.ToString());
            Assert.That(counter, Is.EqualTo(expected), "Every increment reads the value the one before it wrote.");
        }

        [Test]
        public void ProductionTreeHost_EveryUpdateReadsThePreviousWrite_AndAWarmedUpdateAllocatesNothing()
        {
            var compiled = Compile(HoldTree);
            var host = StartHost(compiled, ReadWriteHoldCatalog.TryCreateRuntimeCatalog, ReadWriteHoldCatalog.Fingerprint);
            Assert.That(host.TryResolveExternalTreeRead<int>("counter", NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                out var read, out var failure), Is.True, failure.Code.ToString());

            for (var update = 1; update <= 3; update++)
            {
                host.DriveOneUpdate();
                Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), "update " + update);
                Assert.That(host.TryReadExternalTreeValue(read, out var counter, out failure), Is.True, failure.Code.ToString());
                Assert.That(counter, Is.EqualTo(5 + update), "update " + update + " reads the previous update's write.");
            }

            Assert.That(() => host.DriveOneUpdate(), GcAllocIs.Not.AllocatingGCMemory(),
                "A warmed update with a ReadWrite read and write allocates no managed memory.");
            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            Assert.That(host.TryReadExternalTreeValue(read, out var last, out failure), Is.True, failure.Code.ToString());
            Assert.That(last, Is.EqualTo(9), "The measured update ran the increment.");
        }

        [TestCase("interactive")]
        [TestCase("background")]
        [TestCase("forced-immediate")]
        [TestCase("forced-budgeted")]
        [TestCase("forced-batched-jobs")]
        [TestCase("forced-pipelined-jobs")]
        [TestCase("forced-agent-frame")]
        public void NativeTreeAgent_ReadsAndWritesOneKeyThroughOneReadWriteBinding_InEverySchedulingProfile(string kind)
        {
            var profile = ProfileFor(kind);
            var compiled = Compile(ThreeBumpTree);
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(RegisteredTypes()),
                ReadWriteFixtureCatalog.Fingerprint));
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(ReadWriteFixtureCatalog).FullName + ", " + typeof(ReadWriteFixtureCatalog).Assembly.GetName().Name);
            var agents = new List<NativeTreeAgent>();
            var finals = new List<string>[3];
            for (var index = 0; index < 3; index++)
            {
                var agent = Spawn(asset, profile);
                var slot = index;
                finals[slot] = new List<string>();
                agent.Completed += result =>
                {
                    var ok = agent.TryGetValue<int>("counter", out var counter);
                    finals[slot].Add(result + " " + ok + " counter=" + counter);
                };
                agents.Add(agent);
            }

            var pipelined = kind == "forced-pipelined-jobs";
            for (var frame = 0; frame < (pipelined ? 40 : 12); frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in agents) agent.ProcessRootResult();
            }
            for (var frame = 0; pipelined && frame < 40 && agents.Any(agent => agent.Host != null && !agent.Host.CanReadTreeValues); frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in agents) agent.ProcessRootResult();
            }

            var minimumRuns = pipelined ? 2 : 6;
            for (var index = 0; index < agents.Count; index++)
            {
                var agent = agents[index];
                Assert.That(agent.LastError, Is.Null, kind);
                Assert.That(agent.CompletedRuns, Is.GreaterThanOrEqualTo(minimumRuns), kind);
                Assert.That(finals[index].Count, Is.EqualTo(agent.CompletedRuns), kind);
                // Each run starts on a fresh host from the default 5 and increments three times. (A fact
                // cannot seed the key: NativeTreeAgent refuses facts on keys a node writes.)
                Assert.That(finals[index], Is.All.EqualTo("Success True counter=8"), kind + "\n" + string.Join("\n", finals[index]));
            }
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), kind);
        }

        // ------------------------------------------------------------------ helpers

        private delegate bool CatalogFactory(Allocator allocator, out GeneratedBurstCatalogV2 catalog, out BurstContextResult failure);

        private static void AssertDemandFits(CatalogFactory create, string label, uint? expectedSessions, uint? expectedStaging)
        {
            Assert.That(create(Allocator.Persistent, out var catalog, out var failure), Is.True, label + ": " + failure);
            using (catalog)
            {
                var capacity = catalog.CreateWorkspaceCapacity().BindingCapacity;
                var readWriteCases = 0;
                for (var index = 0; index < catalog.Cases.Length; index++)
                {
                    var dispatchCase = catalog.Cases[index];
                    uint sessions = 0, staging = 0, readWrite = 0;
                    for (uint local = 0; local < dispatchCase.BindingCount; local++)
                    {
                        var binding = catalog.Bindings[(int)(dispatchCase.FirstBinding + local)];
                        var async = binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation;
                        var twice = binding.Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
                        sessions += async || twice ? 2u : 1u;
                        staging += binding.PrimaryValueSize * (twice ? 2u : 1u) + (async ? binding.SecondaryValueSize : 0u);
                        if (twice) readWrite++;
                    }
                    if (readWrite > 0) readWriteCases++;
                    Assert.That(sessions, Is.LessThanOrEqualTo(capacity.MaxValueSessionsPerFrame), label + " case " + index + " sessions");
                    Assert.That(staging, Is.LessThanOrEqualTo(capacity.MaxValueStagingBytesPerFrame), label + " case " + index + " staging");
                }
                Assert.That(readWriteCases, Is.GreaterThan(0), label + " has a ReadWrite case.");
                if (expectedSessions.HasValue)
                    Assert.That(capacity.MaxValueSessionsPerFrame, Is.EqualTo(expectedSessions.Value), label + " sessions");
                if (expectedStaging.HasValue)
                    Assert.That(capacity.MaxValueStagingBytesPerFrame, Is.EqualTo(expectedStaging.Value), label + " staging");
            }
        }

        private static GeneratedCompiledProgramV2 Compile(string json)
        {
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11010/read-write.aibt.json");
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics));
            var nodes = json == HoldTree
                ? NativeLibraryKit.Materialize(typeof(BuiltInLeafShard)).Nodes.Concat(Artifact().Nodes).ToArray()
                : Artifact().Nodes.ToArray();
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, RegisteredTypes(),
                new ReferenceCompilerOptions("Tests/P11010/read-write.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

        private static GeneratedShardMetadataArtifact Artifact() => NativeLibraryKit.Materialize(typeof(ReadWriteFixtureShard));

        private static RegisteredBlackboardTypeCatalog RegisteredTypes() => Artifact().RegisteredTypes;

        private ProductionTreeHost StartHost(GeneratedCompiledProgramV2 compiled)
            => StartHost(compiled, ReadWriteFixtureCatalog.TryCreateRuntimeCatalog, ReadWriteFixtureCatalog.Fingerprint);

        private ProductionTreeHost StartHost(GeneratedCompiledProgramV2 compiled, ProductionTreeCatalogFactory factory, BurstCatalogFingerprint fingerprint)
        {
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(RegisteredTypes()), fingerprint));
            Assert.That(asset.TryCreateProgram(factory, out var program, out var failure),
                Is.True, failure.ToString());
            _programs.Add(program);
            var gameObject = Track(new GameObject("AIBT.Tests.P11010.Host"));
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(program.TryBootstrap(host, ProductionTreeHost.DefaultRandomSeed, out var bootstrapFailure, 0, () => 0L),
                Is.True, bootstrapFailure.Code.ToString());
            return host;
        }

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, SchedulingProfile profile)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11010.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Always;
            agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();
            return agent;
        }

        private SchedulingProfile ProfileFor(string kind)
        {
            switch (kind)
            {
                case "interactive": return SchedulingProfile.Interactive;
                case "background": return SchedulingProfile.Background;
                case "forced-immediate": return Forced(SchedulingPolicy.Immediate, false);
                case "forced-budgeted": return Forced(SchedulingPolicy.Budgeted, false);
                case "forced-batched-jobs":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(pipelined: false));
                    return Forced(SchedulingPolicy.BatchedJobsSameFrame, false);
                case "forced-pipelined-jobs":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(pipelined: true));
                    return Forced(SchedulingPolicy.PipelinedJobs, true);
                case "forced-agent-frame":
                    _scheduler.SetJobsCapabilities(JobsCapabilities(pipelined: false));
                    return Forced(SchedulingPolicy.JobsAgentFrame, false);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p11010." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities JobsCapabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private static string Describe(IEnumerable<Diagnostic> diagnostics)
            => string.Join("\n", diagnostics.Select(d => d.Severity + " " + d.Code.Value + ": " + d.Message));

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
