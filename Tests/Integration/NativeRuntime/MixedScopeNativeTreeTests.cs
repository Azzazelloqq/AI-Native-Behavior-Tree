using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Tests.CodeGen.GenerationP11009;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    [AibtCatalogSet("aibt.tests.p11009.mixed-scope-set", 1u, typeof(MixedScopeFixtureShard))]
    public static partial class MixedScopeFixtureCatalog { }

    /// <summary>
    /// P11-009 (review F9): a native tree with Tree-scope keys and Agent- or Shared-scope keys used to
    /// fail to compile with AIBT3017 "Tree-scope blackboard memory ranges overlap", because the
    /// semantic projection labels every key Tree and kept each scope's own zero-based offsets. The
    /// mixed tree here also orders its keys so that the global key order (agent-counter, ready,
    /// tree-in, tree-out) differs from the scope order (ready, tree-in, tree-out | agent-counter),
    /// and gives every key a distinct default, so a default read from the wrong place shows.
    /// </summary>
    public sealed partial class MixedScopeNativeTreeTests
    {
        private const string MixedTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11009.mixed-scope"", ""name"": ""P11-009 mixed scope"",
  ""root"": ""root"",
  ""blackboardContracts"": { ""agent"": { ""contractId"": ""tests.p11009.agent"", ""contractVersion"": 1 } },
  ""blackboard"": {
    ""agent-counter"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": 40 },
    ""ready"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": true },
    ""tree-in"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 7 },
    ""tree-out"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 3 }
  },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""bump"", ""report""] },
    ""bump"": { ""type"": ""aibt.tests.agent-bump"", ""typeVersion"": 1, ""bindings"": { ""agent-counter"": ""agent-counter"" } },
    ""report"": { ""type"": ""aibt.tests.mixed-report"", ""typeVersion"": 1,
      ""bindings"": { ""ready"": ""ready"", ""tree-in"": ""tree-in"", ""agent-counter"": ""agent-counter"", ""tree-out"": ""tree-out"" } }
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
            var worldObject = Track(new GameObject("AIBT.Tests.P11009.World"));
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

        [TestCase("agent")]
        [TestCase("shared")]
        public void ReviewRepro_NativeGuardWithAnExtraUnboundKey_Compiles(string scope)
        {
            var contract = scope == "agent"
                ? @"""blackboardContracts"": { ""agent"": { ""contractId"": ""probe.agent"", ""contractVersion"": 1 } },"
                : @"""blackboardContracts"": { ""shared"": { ""contractId"": ""probe.shared"", ""contractVersion"": 1 } },";
            var key = scope == "agent"
                ? @"""probe-agent"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": 0 },"
                : @"""probe-shared"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""shared"", ""default"": 0, ""reduction"": { ""kind"": ""first"" } },";
            var json = NativeGuardJson()
                .Replace(@"""root"": ""guard"",", @"""root"": ""guard"", " + contract)
                .Replace(@"""blackboard"": {", @"""blackboard"": { " + key);

            var result = NativeLibraryKit.CompileResult(json);

            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            Assert.That(result.Diagnostics.Any(d => d.Code.Value == "AIBT3017"), Is.False, Describe(result.Diagnostics));
            var semantic = result.Program.SemanticProgram.BlackboardSlots;
            Assert.That(semantic.Count, Is.EqualTo(6));
            for (var left = 0; left < semantic.Count; left++)
            for (var right = left + 1; right < semantic.Count; right++)
                Assert.That(semantic[left].Offset + semantic[left].Size <= semantic[right].Offset
                    || semantic[right].Offset + semantic[right].Size <= semantic[left].Offset, Is.True,
                    "The semantic projection places every key in its own range.");
        }

        /// <summary>
        /// The review's other variant: an Agent key bound to a library node. Library nodes declare
        /// Tree-scope bindings only, so this is a binding scope mismatch (AIBT3018), a refusal of its
        /// own, not the overlap of F9.
        /// </summary>
        [Test]
        public void ReviewVariant_AgentKeyBoundToALibraryNode_IsRefusedAsABindingScopeMismatch()
        {
            var json = NativeGuardJson()
                .Replace(@"""gate-open"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree""",
                    @"""gate-open"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""agent""")
                .Replace(@"""root"": ""guard"",",
                    @"""root"": ""guard"", ""blackboardContracts"": { ""agent"": { ""contractId"": ""probe.agent"", ""contractVersion"": 1 } },");

            var result = NativeLibraryKit.CompileResult(json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code.Value == "AIBT3018"), Is.True, Describe(result.Diagnostics));
            Assert.That(result.Diagnostics.Any(d => d.Code.Value == "AIBT3017"), Is.False, Describe(result.Diagnostics));
        }

        [Test]
        public void ProductionTreeHost_RunsTheMixedTree_AndEveryScopeHoldsItsValue()
        {
            var compiled = Compile(MixedTree);
            var host = StartHost(compiled);
            Assert.That(ReadInt(host, compiled, "agent-counter"), Is.EqualTo(40), "Agent default, placed past the Tree range.");
            Assert.That(ReadInt(host, compiled, "tree-in"), Is.EqualTo(7));
            Assert.That(ReadInt(host, compiled, "tree-out"), Is.EqualTo(3));
            Assert.That(ReadByte(host, compiled, "ready"), Is.EqualTo(1));
            Assert.That(host.TryResolveExternalTreeWrite<int>("tree-in", NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                out var handle, out var failure), Is.True, failure.Code.ToString());
            Assert.That(host.TryWriteExternalTreeValue(handle, 9, out _, out failure), Is.True, failure.Code.ToString());
            Assert.That(host.TryResolveExternalTreeRead<int>("tree-out", NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                out var read, out failure), Is.True, failure.Code.ToString());
            Assert.That(host.TryResolveExternalTreeRead<int>("agent-counter", NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                out _, out failure), Is.False, "External reads stay Tree-scope only.");

            host.DriveOneUpdate();

            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
            // bump: reads the default 40, writes 41; report: reads 41 and tree-in 9, writes 41 * 1000 + 9.
            Assert.That(ReadInt(host, compiled, "agent-counter"), Is.EqualTo(41), "Agent key: default read, written, read again by the next node.");
            Assert.That(host.TryReadExternalTreeValue(read, out var treeOut, out failure), Is.True, failure.Code.ToString());
            Assert.That(treeOut, Is.EqualTo(41_009), "Tree key written from a Tree read and an Agent read.");
            Assert.That(ReadInt(host, compiled, "tree-out"), Is.EqualTo(41_009));
            Assert.That(ReadInt(host, compiled, "tree-in"), Is.EqualTo(9));
            Assert.That(ReadByte(host, compiled, "ready"), Is.EqualTo(1));
        }

        [TestCase("interactive")]
        [TestCase("background")]
        [TestCase("forced-immediate")]
        [TestCase("forced-budgeted")]
        [TestCase("forced-batched-jobs")]
        [TestCase("forced-pipelined-jobs")]
        [TestCase("forced-agent-frame")]
        public void NativeTreeAgent_RunsTheMixedTree_InEverySchedulingProfile(string kind)
        {
            var profile = ProfileFor(kind);
            var compiled = Compile(MixedTree);
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(RegisteredTypes()),
                MixedScopeFixtureCatalog.Fingerprint));
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(MixedScopeFixtureCatalog).FullName + ", " + typeof(MixedScopeFixtureCatalog).Assembly.GetName().Name);
            var agents = new List<NativeTreeAgent>();
            var finals = new List<string>[3];
            for (var index = 0; index < 3; index++)
            {
                var agent = Spawn(asset, profile);
                agent.SetFact("tree-in", 5 + index);
                var slot = index;
                finals[slot] = new List<string>();
                // Tree keys start from their defaults in every run; the Agent key belongs to the agent and keeps
                // counting across the automatic restarts (P11-013). The final values of a run are read here.
                agent.Completed += result =>
                {
                    var ok = agent.TryGetValue<bool>("ready", out var ready) & agent.TryGetValue<int>("tree-in", out var treeIn)
                        & agent.TryGetValue<int>("tree-out", out var treeOut) & agent.TryGetValue<int>("agent-counter", out var counter);
                    finals[slot].Add(result + " " + ok + " ready=" + ready + " tree-in=" + treeIn + " tree-out=" + treeOut
                        + " agent-counter=" + counter);
                };
                agents.Add(agent);
            }

            // A PipelinedJobs round spans scheduler frames (see NativeTreeAgentTests): at cadence 1 this
            // two-leaf tree finishes a run about every 14th frame (P11-009 probe); every other policy
            // finishes a run per frame.
            var pipelined = kind == "forced-pipelined-jobs";
            for (var frame = 0; frame < (pipelined ? 40 : 12); frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in agents) agent.ProcessRootResult();
            }

            // Let an in-flight pipelined round finish, so teardown does not stop a host mid-round.
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
                // The first run may finish before the fact is flushed (tree-in still 7 then); every later run sees it.
                // Run n (1-based) bumps the Agent counter from 40 + n - 1 to 40 + n (P11-013).
                var treeIn = 5 + index;
                var expected = Enumerable.Range(2, finals[index].Count - 1).Select(run =>
                    "Success True ready=True tree-in=" + treeIn + " tree-out=" + ((40 + run) * 1000 + treeIn) + " agent-counter=" + (40 + run));
                Assert.That(finals[index].Skip(1), Is.EqualTo(expected), kind + "\n" + string.Join("\n", finals[index]));
                Assert.That(finals[index][0], Does.EndWith(" agent-counter=41"), kind);
            }
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), kind);
        }


        // ------------------------------------------------------------------ helpers

        private static GeneratedCompiledProgramV2 Compile(string json)
        {
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11009/mixed.aibt.json");
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, Artifact().Nodes, RegisteredTypes(),
                new ReferenceCompilerOptions("Tests/P11009/mixed.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

        private static GeneratedShardMetadataArtifact Artifact() => NativeLibraryKit.Materialize(typeof(MixedScopeFixtureShard));

        private static RegisteredBlackboardTypeCatalog RegisteredTypes() => Artifact().RegisteredTypes;

        private ProductionTreeHost StartHost(GeneratedCompiledProgramV2 compiled)
        {
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(RegisteredTypes()),
                MixedScopeFixtureCatalog.Fingerprint));
            Assert.That(asset.TryCreateProgram(MixedScopeFixtureCatalog.TryCreateRuntimeCatalog, out var program, out var failure),
                Is.True, failure.ToString());
            _programs.Add(program);
            var gameObject = Track(new GameObject("AIBT.Tests.P11009.Host"));
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(program.TryBootstrap(host, ProductionTreeHost.DefaultRandomSeed, out var bootstrapFailure, 0, () => 0L),
                Is.True, bootstrapFailure.Code.ToString());
            return host;
        }

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, SchedulingProfile profile)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11009.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = NativeTreeRestartMode.Always;
            agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();
            return agent;
        }

        /// <summary>Reads a key's bytes from the host's value buffer: Tree keys first, Agent keys past the Tree range.</summary>
        private static int ReadInt(ProductionTreeHost host, GeneratedCompiledProgramV2 compiled, string key)
            => BitConverter.ToInt32(ReadBytes(host, compiled, key, 4), 0);

        private static byte ReadByte(ProductionTreeHost host, GeneratedCompiledProgramV2 compiled, string key)
            => ReadBytes(host, compiled, key, 1)[0];

        private static byte[] ReadBytes(ProductionTreeHost host, GeneratedCompiledProgramV2 compiled, string key, int count)
        {
            Assert.That(host, Is.Not.Null);
            uint treeEnd = 0;
            foreach (var record in compiled.Slots)
                if (record.Slot.Key.Scope == BlackboardScope.Tree)
                    treeEnd = Math.Max(treeEnd, record.Slot.Offset + (uint)record.Slot.Key.Type.RuntimeDescriptor.Size);
            var slot = compiled.Slots.Single(record => record.Slot.Key.Id == key).Slot;
            var start = (slot.Key.Scope == BlackboardScope.Agent ? treeEnd : 0u) + slot.Offset;
            var bytes = new byte[count];
            for (var index = 0; index < count; index++)
                Assert.That(host.GeneratedDispatchAdapter.TryReadTreeValueByte(start + (uint)index, out bytes[index]), Is.True, key);
            return bytes;
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
            Assert.That(SchedulingProfile.TryCreate("tests.p11009." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities JobsCapabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private static string NativeGuardJson()
        {
            var package = PackageInfo.FindForAssembly(typeof(MixedScopeNativeTreeTests).Assembly);
            var root = package == null ? Path.Combine(Application.dataPath, "AIBT") : package.resolvedPath;
            return File.ReadAllText(Path.Combine(root, "Samples~", "NativeGuard", "native-guard.aibt.json"));
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
