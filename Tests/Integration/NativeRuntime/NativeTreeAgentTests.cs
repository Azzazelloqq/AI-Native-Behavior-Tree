using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using AIBT.Authoring;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P9-004: <see cref="NativeTreeAgent"/> facts reach the native tree, named effects reach their
    /// handlers, the restart policy is applied, agents share one program and teardown releases
    /// every host and the shared native catalog. Frames are driven through the real scheduler.
    /// </summary>
    public sealed partial class NativeTreeAgentTests
    {
        private const string ReaderTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p9004.reader"", ""name"": ""P9-004 reader"",
  ""root"": ""check"",
  ""blackboard"": { ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 } },
  ""nodes"": { ""check"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } } }
}";

        private const string EffectTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p9004.effect"", ""name"": ""P9-004 effect"",
  ""root"": ""emit"",
  ""blackboard"": { ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 } },
  ""nodes"": { ""emit"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 17 }, ""bindings"": { ""live-target"": ""live-target"" } } }
}";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P9004.World"));
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
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void SetFact_ReachesTheTree_AndIsKeptAcrossRestarts()
        {
            var asset = Prepare(ReaderTree);
            var agent = Spawn(asset, NativeTreeRestartMode.Never);

            agent.SetFact("live-target", 1f);
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success), "The fact was written before the condition ran.");

            agent.SetFact("live-target", 0f);
            agent.Restart();
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Failure));

            agent.Restart();
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Failure),
                "A fresh host starts from the tree default but the agent re-applies its last fact (0), not the default.");
            agent.SetFact("live-target", 2.5f);
            agent.Restart();
            Frames(1);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(agent.CompletedRuns, Is.EqualTo(4));
        }

        [Test]
        public void OnEffect_DeliversTypedPayloadByName_AndIgnoresUnhandledEffects()
        {
            var asset = Prepare(EffectTree);
            var agent = Spawn(asset, NativeTreeRestartMode.Never);
            var hits = new List<int>();
            agent.OnEffect("hit", (int value) => hits.Add(value));

            Frames(3);
            Assert.That(hits, Is.Empty, "The node only emits once the fact is positive.");

            agent.SetFact("live-target", 1f);
            Frames(3);
            Assert.That(hits, Is.EqualTo(new[] { 17, 17, 17 }), "One committed effect per update, delivered to the named handler.");

            Assert.That(agent.RemoveEffect("hit"), Is.True);
            Frames(2);
            Assert.That(hits, Has.Count.EqualTo(3), "Without a handler the effect is dropped, not queued.");
            Assert.That(agent.IsRunning, Is.True);
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
        }

        [Test]
        public void ThrowingEffectHandler_IsLogged_AndDoesNotBlockLaterEffects()
        {
            var agent = Spawn(Prepare(EffectTree), NativeTreeRestartMode.Never);
            var calls = 0;
            agent.OnEffect("hit", (int value) =>
            {
                if (++calls == 1) throw new InvalidOperationException("handler-failure");
            });
            agent.SetFact("live-target", 1f);

            LogAssert.Expect(LogType.Exception, new Regex("handler-failure"));
            Frames(3);
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(agent.IsRunning, Is.True);
        }

        [Test]
        public void RestartPolicy_ControlsWhatHappensAfterTheRootFinishes()
        {
            var asset = Prepare(ReaderTree);
            var always = Spawn(asset, NativeTreeRestartMode.Always);
            var never = Spawn(asset, NativeTreeRestartMode.Never);
            var onSuccess = Spawn(asset, NativeTreeRestartMode.OnSuccess);
            var completions = new List<NodeStatus>();
            always.Completed += completions.Add;
            foreach (var agent in new[] { always, never, onSuccess }) agent.SetFact("live-target", -1f);

            var firstHost = always.Host;
            Frames(3);

            Assert.That(always.CompletedRuns, Is.EqualTo(3));
            Assert.That(completions, Is.EqualTo(new[] { NodeStatus.Failure, NodeStatus.Failure, NodeStatus.Failure }));
            Assert.That(always.Host, Is.Not.Null.And.Not.SameAs(firstHost), "Always starts a fresh host after every completion.");
            Assert.That(always.IsRunning, Is.True);

            Assert.That(never.CompletedRuns, Is.EqualTo(1));
            Assert.That(never.IsRunning, Is.False);
            Assert.That(never.Host.LastRootResult, Is.EqualTo(NodeStatus.Failure), "Never keeps the finished host for inspection.");

            Assert.That(onSuccess.CompletedRuns, Is.EqualTo(1), "OnSuccess does not restart after Failure.");
            onSuccess.SetFact("live-target", 1f);
            onSuccess.Restart();
            Frames(2);
            Assert.That(onSuccess.CompletedRuns, Is.EqualTo(3), "After Success it restarts and runs again.");
        }

        [Test]
        public void AgentsOnTheSameAsset_ShareOneProgram_AndKeepSeparateState()
        {
            var asset = Prepare(EffectTree);
            var first = Spawn(asset, NativeTreeRestartMode.Never);
            var second = Spawn(asset, NativeTreeRestartMode.Never);
            var firstHits = 0;
            var secondHits = 0;
            first.OnEffect("hit", (int _) => firstHits++);
            second.OnEffect("hit", (int _) => secondHits++);

            Assert.That(first.Program, Is.Not.Null.And.SameAs(second.Program));
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms + 1));
            Assert.That(first.Host, Is.Not.SameAs(second.Host));

            first.SetFact("live-target", 1f);
            Frames(2);
            Assert.That(firstHits, Is.EqualTo(2));
            Assert.That(secondHits, Is.Zero, "A fact set on one agent never reaches another agent's tree.");
            Assert.That(_world.RegisteredParticipantCount, Is.EqualTo(2));
        }

        [Test]
        public void Deactivate_StopsHosts_LeavesTheWorld_AndDisposesTheSharedProgramWithTheLastAgent()
        {
            var asset = Prepare(EffectTree);
            var first = Spawn(asset, NativeTreeRestartMode.Always);
            var second = Spawn(asset, NativeTreeRestartMode.Always);
            first.SetFact("live-target", 1f);
            Frames(2);

            first.Deactivate();
            Assert.That(first.Host, Is.Null);
            Assert.That(first.GetComponent<ProductionTreeHost>(), Is.Null, "The host component is destroyed.");
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms + 1), "The second agent still holds the program.");
            Frames(1);
            Assert.That(second.IsRunning, Is.True);

            second.Deactivate();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms),
                "The last release disposes the program and its native catalog (an error is logged if a host still held it).");
            Assert.That(_world.RegisteredParticipantCount, Is.Zero);
            Assert.That(_scheduler.RegisteredCount, Is.Zero);

            second.Activate();
            Frames(1);
            Assert.That(second.IsRunning, Is.True, "Re-enabling (also what a domain reload does) starts cleanly.");
        }

        [Test]
        public void FactOfTheWrongType_IsReportedOnce_AndTheTreeKeepsRunning()
        {
            var agent = Spawn(Prepare(EffectTree), NativeTreeRestartMode.Never);
            agent.SetFact("live-target", 3);

            LogAssert.Expect(LogType.Error, new Regex("fact 'live-target' \\(Int32\\) cannot be written"));
            Frames(3);
            Assert.That(agent.IsRunning, Is.True);
            Assert.Throws<InvalidOperationException>(() => agent.SetFact("live-target", 1f), "A key keeps the type it was first set with.");
        }

        [Test]
        public void ExplicitCatalogFactory_IsUsed_WhenTheAssetRecordsNoCatalog()
        {
            var asset = Prepare(ReaderTree, recordCatalog: false);
            var missing = Spawn(asset, NativeTreeRestartMode.Never, activate: false);
            string fault = null;
            missing.Faulted += message => fault = message;
            LogAssert.Expect(LogType.Error, new Regex("does not record its node catalog"));
            missing.Activate();
            Assert.That(fault, Does.Contain("does not record its node catalog"));
            Assert.That(missing.Host, Is.Null);

            var explicitAgent = Spawn(asset, NativeTreeRestartMode.Never, activate: false);
            explicitAgent.CatalogFactory = ExternalTreeWriteFixtureCatalog.TryCreateRuntimeCatalog;
            explicitAgent.Activate();
            explicitAgent.SetFact("live-target", 1f);
            Frames(1);
            Assert.That(explicitAgent.LastResult, Is.EqualTo(NodeStatus.Success));
        }

        [TestCase("interactive")]
        [TestCase("background")]
        [TestCase("forced-immediate")]
        [TestCase("forced-budgeted")]
        [TestCase("forced-batched-jobs")]
        [TestCase("forced-pipelined-jobs")]
        public void EverySchedulingProfile_RunsRestartingAndEmittingAgentsThroughTheFacade(string kind)
        {
            var profile = ProfileFor(kind);
            var reader = Prepare(ReaderTree);
            var effect = Prepare(EffectTree);
            var restarting = new List<NativeTreeAgent>();
            var emitting = new List<NativeTreeAgent>();
            var hits = new int[3];
            for (var index = 0; index < 3; index++)
            {
                var agent = Spawn(reader, NativeTreeRestartMode.Always, profile: profile);
                agent.SetFact("live-target", 1f);
                restarting.Add(agent);
                var emitter = Spawn(effect, NativeTreeRestartMode.Never, profile: profile);
                var slot = index;
                emitter.OnEffect("hit", (int value) => hits[slot] += value == 17 ? 1 : 1000);
                emitter.SetFact("live-target", 1f);
                emitting.Add(emitter);
            }

            Frames(12);

            // A PipelinedJobs round spans scheduler frames, so at cadence 1 a tree advances one
            // update about every third frame; every other policy advances one update per frame.
            var pipelined = kind == "forced-pipelined-jobs";
            var minimumRuns = pipelined ? 2 : 6;
            var minimumHits = pipelined ? 3 : 6;
            foreach (var agent in restarting)
            {
                Assert.That(agent.LastError, Is.Null, kind);
                Assert.That(agent.CompletedRuns, Is.GreaterThanOrEqualTo(minimumRuns), kind + ": the restarting tree completes and restarts.");
                Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success), kind);
            }
            for (var index = 0; index < emitting.Count; index++)
            {
                Assert.That(emitting[index].LastError, Is.Null, kind);
                Assert.That(emitting[index].IsRunning, Is.True, kind);
                Assert.That(hits[index], Is.InRange(minimumHits, 12), kind + ": one effect per update, delivered by name.");
            }
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None), kind);
        }

        [Test]
        public void ForcedJobsProfileWithoutJobsCapabilities_FaultsWithAnExplanation()
        {
            // P10-001 root cause of the "NativeCapacityPlanInvalid" fault seen through the facade:
            // the world's scheduler had no SchedulerJobsCapabilities, which is never defaulted.
            var agent = Spawn(Prepare(ReaderTree), NativeTreeRestartMode.Always,
                profile: Forced(SchedulingPolicy.BatchedJobsSameFrame, false));
            string fault = null;
            agent.Faulted += message => fault = message;

            LogAssert.Expect(LogType.Error, new Regex("Forced scheduling policy is not supported"));
            LogAssert.Expect(LogType.Error, new Regex("SetJobsCapabilities"));
            Frames(1);

            Assert.That(fault, Does.StartWith("The native host stopped: NativeCapacityPlanInvalid."));
            Assert.That(fault, Does.Contain("BatchedJobsSameFrame").And.Contain("SetJobsCapabilities"));
            Assert.That(agent.Host, Is.Null);
        }

        [Test]
        public void LeavingAndJoiningTheWorld_KeepsTheRegistrationOrderOfEffectDelivery()
        {
            // P10-001: unregistering leaves a vacant slot that is compacted later (O(1) amortized);
            // effects are still delivered in registration order, survivors first, newcomers last.
            var asset = Prepare(EffectTree);
            var order = new List<int>();
            var agents = new List<NativeTreeAgent>();
            NativeTreeAgent SpawnEmitter(int label)
            {
                var agent = Spawn(asset, NativeTreeRestartMode.Never);
                agent.OnEffect("hit", (int _) => order.Add(label));
                agent.SetFact("live-target", 1f);
                return agent;
            }
            for (var label = 0; label < 12; label++) agents.Add(SpawnEmitter(label));
            Frames(1);
            Assert.That(order, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }));

            foreach (var leaving in new[] { 1, 2, 4, 5, 7, 8, 10 }) agents[leaving].Deactivate();
            Assert.That(_world.RegisteredParticipantCount, Is.EqualTo(5));
            Assert.That(_scheduler.RegisteredCount, Is.EqualTo(5));
            agents.Add(SpawnEmitter(12));
            agents.Add(SpawnEmitter(13));
            agents[4].Activate();
            agents[4].SetFact("live-target", 1f);
            order.Clear();
            Frames(1);
            Assert.That(order, Is.EqualTo(new[] { 0, 3, 6, 9, 11, 12, 13, 4 }), "Survivors keep their order; rejoiners go last.");

            foreach (var agent in agents) agent.Deactivate();
            Assert.That(_world.RegisteredParticipantCount, Is.Zero);
            Assert.That(_scheduler.RegisteredCount, Is.Zero);
            agents[0].Activate();
            agents[0].SetFact("live-target", 1f);
            order.Clear();
            Frames(1);
            Assert.That(order, Is.EqualTo(new[] { 0 }));
        }

        // ------------------------------------------------------------------ helpers

        private NativeTreeAgent Spawn(
            ProductionTreeAsset asset, NativeTreeRestartMode restart, bool activate = true, SchedulingProfile profile = null)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P9004.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = restart;
            if (profile != null) agent.Profile = profile;
            _agents.Add(agent);
            if (activate) agent.Activate();
            return agent;
        }

        /// <summary>
        /// P10-001: the non-default profiles. A forced Jobs policy needs the coordinator's own
        /// <see cref="SchedulerJobsCapabilities"/> (never a hidden default), so those cases set it.
        /// </summary>
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
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p10001." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities JobsCapabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private ProductionTreeAsset Prepare(string json, bool recordCatalog = true)
        {
            var artifact = GeneratedShardMetadataMaterializer.MaterializeArtifact(
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardId,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ShardVersion,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.CanonicalDescriptorJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.DescriptorHash,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.ManifestRegistryJson,
                ExternalTreeWriteFixtureShard.AibtGeneratedMetadata.NodeRegistryHash);
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P9004/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("\n", parsed.Diagnostics));
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, artifact.Nodes, artifact.RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P9004/tree.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
            var asset = ProductionTreeAsset.Create(result.Program.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes),
                ExternalTreeWriteFixtureCatalog.Fingerprint);
            if (recordCatalog)
                asset.RecordPreparation(string.Empty, string.Empty,
                    typeof(ExternalTreeWriteFixtureCatalog).FullName + ", " + typeof(ExternalTreeWriteFixtureCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
