using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using AIBT.Tests.Consumer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// P11-005: the graph's blackboard panel shows the live values of a <see cref="NativeTreeAgent"/>,
    /// read from the current run's host by key ID (ADR-P11-004 D7). The tree reuses the P7-039 /
    /// P11-004 fixture nodes: "owned-target" (Int32, written by a node, default -3, then 7),
    /// "live-target" (Float32, a fact), "flag" (Bool) and "spot" (Float3), which no node touches.
    /// Frames are driven through the real scheduler, as in the P11-004 agent tests.
    /// </summary>
    public sealed class GraphNativeLiveBlackboardTests
    {
        private const string CatalogTypeName = "AIBT.Tests.Integration.NativeRuntime.ExternalTreeWriteFixtureCatalog";
        private const string MixedCatalogTypeName = "AIBT.Tests.Integration.NativeRuntime.MixedScopeFixtureCatalog";

        // P11-013: the P11-009 mixed tree (MixedScopeNativeTreeTests): "bump" adds one to the Agent key
        // "agent-counter" (default 40), "report" writes agent * 1000 + tree-in to the Tree key "tree-out".
        private const string MixedTreeJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11013.graph-mixed"", ""name"": ""P11-013 graph mixed"", ""root"": ""root"",
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

        internal const string TreeJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11005.native-live"", ""name"": ""P11-005 native live"", ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""owned-target"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -3 },
    ""spot"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 1.0, ""y"": 2.0, ""z"": 3.0 } },
    ""flag"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": true }
  },
  ""nodes"": {
    ""reader"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""writer"": { ""type"": ""aibt.tests.external-tree-write-writer"", ""typeVersion"": 1, ""parameters"": { ""value"": 7 }, ""bindings"": { ""owned-target"": ""owned-target"" } },
    ""root"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""reader"", ""writer""] }
  }
}";

        // Runs until "live-target" is above zero, so its host stays the same across frames.
        private const string RunningTreeJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11005.native-running"", ""name"": ""P11-005 native running"", ""root"": ""wait"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""owned-target"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -3 }
  },
  ""nodes"": {
    ""wait"": { ""type"": ""aibt.tests.external-tree-effect"", ""typeVersion"": 1, ""parameters"": { ""value"": 5 }, ""bindings"": { ""live-target"": ""live-target"" } }
  }
}";

        // One key of every built-in type; no node touches the extra keys.
        private const string EveryTypeTreeJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11005.every-type"", ""name"": ""P11-005 every type"", ""root"": ""root"",
  ""blackboard"": {
    ""live-target"": { ""type"": ""Float32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.0 },
    ""owned-target"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": -3 },
    ""k-bool"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": true },
    ""k-int64"": { ""type"": ""Int64"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 9000000000 },
    ""k-float64"": { ""type"": ""Float64"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 0.125 },
    ""k-float2"": { ""type"": ""Float2"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 1.5, ""y"": -2.0 } },
    ""k-float3"": { ""type"": ""Float3"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 1.0, ""y"": 2.0, ""z"": 3.0 } },
    ""k-quaternion"": { ""type"": ""Quaternion"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""x"": 0.0, ""y"": 0.0, ""z"": 0.0, ""w"": 1.0 } },
    ""k-enum"": { ""type"": ""Enum32"", ""enumContract"": ""tests.p11005.mode"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""contract"": ""tests.p11005.mode"", ""value"": 2 } },
    ""k-text32"": { ""type"": ""FixedString32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""north gate"" },
    ""k-text64"": { ""type"": ""FixedString64"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""b"" },
    ""k-text128"": { ""type"": ""FixedString128"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""c"" },
    ""k-text512"": { ""type"": ""FixedString512"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""d"" },
    ""k-agent"": { ""type"": ""AgentId"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""42"" },
    ""k-entity"": { ""type"": ""EntityId"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""7"" },
    ""k-operation"": { ""type"": ""OperationId"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": ""1:2:3:4"" },
    ""k-asset"": { ""type"": ""AssetId"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": { ""guid"": ""0123456789abcdef0123456789abcdef"", ""localFileId"": 11400000 } }
  },
  ""nodes"": {
    ""reader"": { ""type"": ""aibt.tests.external-tree-write-reader"", ""typeVersion"": 1, ""bindings"": { ""live-target"": ""live-target"" } },
    ""writer"": { ""type"": ""aibt.tests.external-tree-write-writer"", ""typeVersion"": 1, ""parameters"": { ""value"": 7 }, ""bindings"": { ""owned-target"": ""owned-target"" } },
    ""root"": { ""type"": ""aibt.core.reactive-sequence"", ""typeVersion"": 1, ""children"": [""reader"", ""writer""] }
  }
}";

        private readonly List<Object> _created = new List<Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private readonly List<string> _directories = new List<string>();
        private readonly List<BehaviorTreeGraphWindow> _windowsBefore = new List<BehaviorTreeGraphWindow>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;
        private Type _catalogType;

        [SetUp]
        public void SetUp()
        {
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            _catalogType = FindType(CatalogTypeName);
            _windowsBefore.AddRange(Resources.FindObjectsOfTypeAll<BehaviorTreeGraphWindow>());
            var worldObject = Track(new GameObject("AIBT.Tests.P11005.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<BehaviorTreeGraphWindow>().Except(_windowsBefore).ToArray())
            {
                if (window.hasUnsavedChanges) window.DiscardChanges();
                window.Close();
            }
            _windowsBefore.Clear();
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) Object.DestroyImmediate(_created[index]);
            _created.Clear();
            foreach (var directory in _directories) if (Directory.Exists(directory)) Directory.Delete(directory, true);
            _directories.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        // ---------------------------------------------------------------- the panel

        [UnityTest]
        public IEnumerator NodeWrittenAndFactKeys_ShowTheRunningTreesValues()
        {
            var agent = Spawn(TreeJson, NativeTreeRestartMode.Never);
            var window = OpenWindow(TreeJson);
            yield return null;
            window.ShowLive(agent);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "owned-target"), Is.EqualTo("live: -3"), "Defaults before the first update.");
            Assert.That(Live(window, "live-target"), Is.EqualTo("live: 0"));
            Assert.That(Live(window, "flag"), Is.EqualTo("live: true"));
            Assert.That(Live(window, "spot"), Is.EqualTo("live: (1, 2, 3)"));

            agent.SetFact("live-target", 2.5f);
            Frames(1);
            window.RefreshLiveBlackboard();

            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(Live(window, "owned-target"), Is.EqualTo("live: 7"), "The writer node's value.");
            Assert.That(Live(window, "live-target"), Is.EqualTo("live: 2.5"), "The fact set by game code.");
            Assert.That(Label(window, "owned-target").tooltip, Is.Empty);
        }

        [UnityTest]
        public IEnumerator WithoutARun_EveryKeyShowsADash_AndANewRunShowsTheDefaultsAgain()
        {
            var agent = Spawn(TreeJson, NativeTreeRestartMode.Never);
            agent.SetFact("live-target", 1f);
            var window = OpenWindow(TreeJson);
            yield return null;
            window.ShowLive(agent);
            Frames(1);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "owned-target"), Is.EqualTo("live: 7"));

            agent.Stop();
            Assert.That(agent.Host, Is.Null);
            window.RefreshLiveBlackboard();
            foreach (var key in new[] { "owned-target", "live-target", "flag", "spot" })
            {
                Assert.That(Live(window, key), Is.EqualTo("live: —"), key);
                Assert.That(Label(window, key).tooltip, Is.EqualTo("The agent has no run now."), key);
            }

            agent.Restart();
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "owned-target"), Is.EqualTo("live: -3"), "A new run starts from the defaults; that is what shows.");
            Assert.That(Live(window, "live-target"), Is.EqualTo("live: 0"));
        }

        [UnityTest]
        public IEnumerator APipelinedHostThatExecutes_KeepsTheLastText()
        {
            _scheduler.SetJobsCapabilities(JobsCapabilities(pipelined: true));
            var agent = Spawn(RunningTreeJson, NativeTreeRestartMode.Never, Forced(SchedulingPolicy.PipelinedJobs, true));
            var window = OpenWindow(RunningTreeJson);
            yield return null;
            window.ShowLive(agent);
            var host = agent.Host;
            Assert.That(host.IsExecuting, Is.False);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "owned-target"), Is.EqualTo("live: -3"));
            var executingFrames = 0;
            for (var frame = 0; frame < 6; frame++)
            {
                Frames(1);
                Assert.That(agent.Host, Is.SameAs(host), "The tree keeps running on the same host.");
                window.RefreshLiveBlackboard();
                if (!host.IsExecuting) continue;
                executingFrames++;
                Assert.That(Live(window, "owned-target"), Is.EqualTo("live: -3"), "The last text stays; no flicker to —.");
                Assert.That(Label(window, "owned-target").tooltip, Does.Contain("PipelinedJobs"));
            }
            Assert.That(executingFrames, Is.GreaterThan(0), "A PipelinedJobs host holds its drive lock between frames.");
        }

        [UnityTest]
        public IEnumerator AKeyTheRunningProgramDoesNotHave_ShowsADash_WithTheReason()
        {
            var agent = Spawn(TreeJson, NativeTreeRestartMode.Never);
            var window = OpenWindow(TreeJson);
            yield return null;
            window.ShowLive(agent);
            Assert.That(window.LiveDebugger.TryFormatValue("no-such-key", out var text, out var reason), Is.False);
            Assert.That(text, Is.Null);
            Assert.That(reason, Is.EqualTo("The running program has no key 'no-such-key'."));
        }

        [UnityTest]
        public IEnumerator ARenamedKey_IsANewId_WhichTheOldProgramDoesNotHave()
        {
            // A key's name cannot differ from its ID in a tree that compiles: the JSON parser sets
            // Name = Id and the compiler refuses a document where they differ (AIBT1007). So the ID
            // is the only stable key, and renaming a key gives it a new ID.
            var parsed = CanonicalTreeJson.Parse(TreeJson, "tests/p11005/renamed.aibt.json");
            Assert.That(parsed.Success, Is.True);
            var key = parsed.Document.Blackboard.Single(candidate => candidate.Id == "owned-target");
            var named = SemanticEditOperations.SetBlackboardKey(parsed.Document,
                new BlackboardKeyDefinition(key.Id, "Owned target (renamed)", key.Type, key.Scope, key.DefaultValue, key.Description, key.Reduction));
            Assert.That(ProductionTreeAssetBaker.Create(named, "tests/p11005/renamed.aibt.json", _catalogType, out var diagnostics), Is.Null);
            Assert.That(diagnostics.Select(d => d.Code.Value), Does.Contain("AIBT1007"));

            var agent = Spawn(TreeJson, NativeTreeRestartMode.Never);
            var renamedJson = TreeJson.Replace("\"owned-target\": { \"type\"", "\"ammo-left\": { \"type\"")
                .Replace("{ \"owned-target\": \"owned-target\" }", "{ \"owned-target\": \"ammo-left\" }");
            Assert.That(renamedJson, Does.Not.Contain(": \"owned-target\""));
            var window = OpenWindow(renamedJson);
            yield return null;
            window.ShowLive(agent);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "ammo-left"), Is.EqualTo("live: —"), "The running program was compiled before the rename.");
            Assert.That(Label(window, "ammo-left").tooltip, Is.EqualTo("The running program has no key 'ammo-left'."));
            Assert.That(window.MatchesProgram(agent.Program), Is.False, "The graph would not attach to it on its own.");

            var current = Spawn(renamedJson, NativeTreeRestartMode.Never);
            window.ShowLive(current);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "ammo-left"), Is.EqualTo("live: -3"), "A program prepared after the rename reads the new ID.");
        }

        [UnityTest]
        public IEnumerator EveryBuiltInKeyType_IsReadFromARealHost_AndPrintedReadably()
        {
            var agent = Spawn(EveryTypeTreeJson, NativeTreeRestartMode.Never);
            var window = OpenWindow(EveryTypeTreeJson);
            yield return null;
            window.ShowLive(agent);
            window.RefreshLiveBlackboard();
            var expected = new Dictionary<string, string>
            {
                ["k-bool"] = "true",
                ["k-int64"] = "9000000000",
                ["k-float64"] = "0.13",
                ["k-float2"] = "(1.5, -2)",
                ["k-float3"] = "(1, 2, 3)",
                ["k-quaternion"] = "(0, 0, 0, 1)",
                ["k-enum"] = "2",
                ["k-text32"] = "\"north gate\"",
                ["k-text64"] = "\"b\"",
                ["k-text128"] = "\"c\"",
                ["k-text512"] = "\"d\"",
                ["k-agent"] = "agent 42",
                ["k-entity"] = "entity 7",
                ["k-operation"] = "operation 1:2:3:4",
                ["k-asset"] = "asset 0123456789abcdef0123456789abcdef:11400000",
            };
            foreach (var pair in expected)
                Assert.That(Live(window, pair.Key), Is.EqualTo("live: " + pair.Value), pair.Key + ": " + Label(window, pair.Key).tooltip);
        }

        [UnityTest]
        public IEnumerator ASubtreeWindow_ShowsItsInstancesPrivateKey_AndTheParentKeyItsInterfaceKeyIsBoundTo()
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-p11005-subtree", Guid.NewGuid().ToString("N")).Replace('\\', '/');
            Directory.CreateDirectory(directory);
            _directories.Add(directory);
            var childPath = directory + "/child.aibt.json";
            var parentPath = directory + "/parent.aibt.json";
            File.WriteAllText(childPath, SubtreeChildJson.Replace('\'', '"'));
            File.WriteAllText(parentPath, SubtreeParentJson.Replace('\'', '"'));
            // The compiler takes logical source IDs; the window resolves real files.
            var logical = new InMemorySubtreeResolver();
            logical.Add(CanonicalTreeJson.Parse(File.ReadAllBytes(childPath), "trees/child.aibt.json").Document, "trees/child.aibt.json");
            var resolver = new InMemorySubtreeResolver();
            resolver.Add(Parse(childPath).Document, childPath);
            var parent = Parse(parentPath).Document;
            var asset = ProductionTreeAssetBaker.Create(parent, "trees/parent.aibt.json", typeof(ConsumerCatalog), logical, out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            var agent = Spawn(Track(asset), NativeTreeRestartMode.Never);

            var window = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            window.SubtreeResolver = resolver;
            window.Show();
            window.OpenFromPath(parentPath, ProjectNodeCatalog.ForCatalog(typeof(ConsumerCatalog)).Registry);
            yield return null;
            Assert.That(window.MatchesProgram(agent.Program), Is.True);
            window.ShowLive(agent);
            var child = window.OpenSubtree(new NodeId("use"));
            Assert.That(child, Is.Not.Null);
            yield return null;
            Assert.That(child.InstancePrefix, Is.EqualTo("use:"));
            Assert.That(child.LiveDebugger.IsAttached, Is.True);

            window.RefreshLiveBlackboard();
            child.RefreshLiveBlackboard();
            Assert.That(Live(window, "distance"), Is.EqualTo("live: 1.25"));
            Assert.That(Live(window, "range"), Is.EqualTo("live: 9"), "The parent's own key of the same name.");
            Assert.That(child.LiveKeyName("range"), Is.EqualTo("use:range"));
            Assert.That(Live(child, "range"), Is.EqualTo("live: 3"), "The instance's private key, not the parent's.");
            Assert.That(child.LiveKeyName("distance"), Is.EqualTo("distance"));
            Assert.That(Live(child, "distance"), Is.EqualTo("live: 1.25"), "The bound interface key reads the parent's key.");
        }

        [UnityTest]
        public IEnumerator AnAgentScopeKey_ShowsItsValue_WhichARestartKeeps()
        {
            var mixedCatalog = FindType(MixedCatalogTypeName);
            var agent = Spawn(MixedTreeJson, NativeTreeRestartMode.Never, catalogType: mixedCatalog);
            var window = OpenWindow(MixedTreeJson, mixedCatalog);
            yield return null;
            window.ShowLive(agent);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "agent-counter"), Is.EqualTo("live: 40"), "The Agent default: " + Label(window, "agent-counter").tooltip);
            Assert.That(window.LiveDebugger.TryFormatValue("agent-counter", out var text, out var reason), Is.True, reason);
            Assert.That(text, Is.EqualTo("40"));

            Frames(1);
            window.RefreshLiveBlackboard();
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(Live(window, "agent-counter"), Is.EqualTo("live: 41"));
            Assert.That(Live(window, "tree-out"), Is.EqualTo("live: 41007"));

            agent.Restart();
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "agent-counter"), Is.EqualTo("live: 41"), "The new run keeps the agent's Agent values.");
            Assert.That(Live(window, "tree-out"), Is.EqualTo("live: 3"), "Tree keys start from their defaults.");
            Frames(1);
            window.RefreshLiveBlackboard();
            Assert.That(Live(window, "agent-counter"), Is.EqualTo("live: 42"));
        }

        // ---------------------------------------------------------------- pure rules

        [Test]
        public void ARefusedRead_KeepsTheLastTextOnlyWhileTheHostExecutes()
        {
            Assert.That(GraphLiveDebugger.ExplainRefusal("k", NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, true, "7", out var text, out var reason), Is.True);
            Assert.That(text, Is.EqualTo("7"));
            Assert.That(reason, Does.Contain("last value read"));
            Assert.That(GraphLiveDebugger.ExplainRefusal("k", NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, true, null, out text, out reason), Is.False);
            Assert.That(text, Is.Null);
            Assert.That(reason, Does.Contain("no value was read yet"));
            Assert.That(GraphLiveDebugger.ExplainRefusal("k", NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid, false, "7", out text, out reason), Is.False,
                "A stopped host shows nothing, even with an old text.");
            Assert.That(reason, Is.EqualTo("The host is stopped."));
        }

        [TestCase(NativeRuntimeDiagnosticCodeV1.BlackboardUndeclaredAccess, "The running program has no key 'k'.")]
        [TestCase(NativeRuntimeDiagnosticCodeV1.BlackboardUnsupportedScope, "Only Tree- and Agent-scope keys are read from a native agent.")]
        [TestCase(NativeRuntimeDiagnosticCodeV1.BlackboardTypeMismatch, "Keys of a registered type are not shown for a native agent.")]
        [TestCase(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid, "The host was not started from a generated catalog.")]
        [TestCase(NativeRuntimeDiagnosticCodeV1.BlackboardInvalidValue, "The host refused the read: BlackboardInvalidValue.")]
        public void OtherRefusals_ShowNothing_EvenWhileExecuting(NativeRuntimeDiagnosticCodeV1 code, string expected)
        {
            Assert.That(GraphLiveDebugger.ExplainRefusal("k", code, true, "7", out var text, out var reason), Is.False);
            Assert.That(text, Is.Null);
            Assert.That(reason, Is.EqualTo(expected));
        }

        [Test]
        public void Format_PrintsEveryBuiltInType()
        {
            var cases = new (BlackboardValue Value, string Text)[]
            {
                (BlackboardValue.FromBool(false), "false"),
                (BlackboardValue.FromInt32(-12), "-12"),
                (BlackboardValue.FromInt64(1234567890123L), "1234567890123"),
                (BlackboardValue.FromFloat32(1.001f), "1"),
                (BlackboardValue.FromFloat32(-0.25f), "-0.25"),
                (BlackboardValue.FromFloat64(2.5), "2.5"),
                (BlackboardValue.FromFloat2(new Float2Value(0.5f, 1f)), "(0.5, 1)"),
                (BlackboardValue.FromFloat3(new Float3Value(1f, 2f, 3.25f)), "(1, 2, 3.25)"),
                (BlackboardValue.FromQuaternion(new QuaternionValue(0f, 0.71f, 0f, 0.71f)), "(0, 0.71, 0, 0.71)"),
                (BlackboardValue.FromEnum32(new Enum32Value(99UL, 3)), "3"),
                (BlackboardValue.FromString32("gate"), "\"gate\""),
                (BlackboardValue.FromString512(""), "\"\""),
                (BlackboardValue.FromAgentId(new AgentId(5)), "agent 5"),
                (BlackboardValue.FromEntityId(new EntityId(8)), "entity 8"),
                (BlackboardValue.FromOperationId(OperationId.Parse("1:2:3:4")), "operation 1:2:3:4"),
                (BlackboardValue.FromAssetId(AssetId.Parse("0123456789abcdef0123456789abcdef")), "asset 0123456789abcdef0123456789abcdef"),
                (BlackboardValue.FromAssetId(AssetId.Parse("0123456789abcdef0123456789abcdef", 5)), "asset 0123456789abcdef0123456789abcdef:5"),
            };
            foreach (var item in cases)
                Assert.That(GraphLiveDebugger.Format(item.Value), Is.EqualTo(item.Text), item.Value.Type.ToString());
        }

        // ---------------------------------------------------------------- helpers

        private const string SubtreeChildJson = @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11005.child', 'name': 'P11-005 child', 'root': 'root',
  'interface': { 'version': 1, 'keys': ['distance'] },
  'blackboard': {
    'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 },
    'range': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 3.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['near', 'far'] },
    'near': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 1.5 }, 'bindings': { 'distance': 'distance' } },
    'far': { 'type': 'aibt.tests.consumer.radius', 'typeVersion': 1, 'parameters': { 'radius': 2.5 }, 'bindings': { 'distance': 'range' } } } }";

        private const string SubtreeParentJson = @"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11005.parent', 'name': 'P11-005 parent', 'root': 'root',
  'blackboard': {
    'distance': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 1.25 },
    'range': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 9.0 } },
  'nodes': {
    'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
    'use': { 'subtree': { 'treeId': 'tests.p11005.child', 'interfaceVersion': 1, 'keys': { 'distance': 'distance' } } } } }";

        private BehaviorTreeGraphWindow OpenWindow(string json, Type catalogType = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "aibt-p11005-live", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _directories.Add(directory);
            var path = Path.Combine(directory, "tree.aibt.json");
            File.WriteAllText(path, json);
            var window = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            window.Show();
            window.OpenFromPath(path, ProjectNodeCatalog.ForCatalog(catalogType ?? _catalogType).Registry);
            return window;
        }

        private static Label Label(BehaviorTreeGraphWindow window, string key)
        {
            var label = window.rootVisualElement.Q<Label>("aibt-bb-live-" + key);
            Assert.That(label, Is.Not.Null, "No live label for " + key);
            return label;
        }

        private static string Live(BehaviorTreeGraphWindow window, string key) => Label(window, key).text;

        private static TreeJsonReadResult Parse(string path)
        {
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(parsed.Success, Is.True, string.Join("; ", parsed.Diagnostics.Select(d => d.Code.Value + " " + d.Message)));
            return parsed;
        }

        private ProductionTreeAsset Bake(TreeDocument document, string sourceId, Type catalogType = null)
        {
            var asset = ProductionTreeAssetBaker.Create(document, sourceId, catalogType ?? _catalogType, out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return Track(asset);
        }

        private NativeTreeAgent Spawn(string json, NativeTreeRestartMode restart, SchedulingProfile profile = null, Type catalogType = null)
        {
            var parsed = CanonicalTreeJson.Parse(json, "tests/p11005/tree.aibt.json");
            Assert.That(parsed.Success, Is.True, string.Join("; ", parsed.Diagnostics.Select(d => d.Code.Value + " " + d.Message)));
            return Spawn(Bake(parsed.Document, "tests/p11005/tree.aibt.json", catalogType), restart, profile);
        }

        private NativeTreeAgent Spawn(ProductionTreeAsset asset, NativeTreeRestartMode restart, SchedulingProfile profile = null)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11005.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = asset;
            agent.World = _world;
            agent.RestartMode = restart;
            if (profile != null) agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();
            Assert.That(agent.Host, Is.Not.Null, agent.LastError);
            return agent;
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p11005." + policy, 0, 1u, null, null, pipelining, policy,
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

        private T Track<T>(T value) where T : Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static Type FindType(string fullName)
        {
            var type = TypeCache.GetTypesWithAttribute<AIBT.Burst.AibtCatalogSetAttribute>().FirstOrDefault(found => found.FullName == fullName);
            Assert.That(type, Is.Not.Null, fullName);
            return type;
        }
    }
}
