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
    public sealed class NativeTreeAgentTests
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

        // ------------------------------------------------------------------ helpers

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, NativeTreeRestartMode restart, bool activate = true)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P9004.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = restart;
            _agents.Add(agent);
            if (activate) agent.Activate();
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
