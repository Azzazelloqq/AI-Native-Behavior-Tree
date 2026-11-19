using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AIBT.Agents;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Documentation
{
    // P9-006: the managed-layer code in README.md is copied from the "guide:" regions below, so it
    // compiles with this assembly; UserGuideSnippetTests checks README against these regions and
    // runs the nodes. The native-layer snippets in README are quoted from Samples~/FirstAgent and
    // checked line by line against those files.

    // guide:begin managed-nodes
    [BtNode("game.heal", Category = "Combat", Summary = "Restores health until it is full.")]
    public sealed class HealNode : BtAction
    {
        public float perSecond = 25f;  // a parameter: the tree author edits it in the graph ("per-second")
        public BbKey<float> health;    // a blackboard key: the tree author picks which one

        protected override NodeStatus OnTick()
        {
            var value = Mathf.Min(100f, Get(health) + perSecond * DeltaTime);
            Set(health, value);
            return value >= 100f ? NodeStatus.Success : NodeStatus.Running;
        }
    }

    [BtNode("game.is-low-health", Category = "Combat", Summary = "Holds while health is below a threshold.")]
    public sealed class IsLowHealthNode : BtCondition
    {
        public float threshold = 30f;
        public BbKey<float> health;

        protected override bool Check() => Get(health) < threshold;
    }
    // guide:end

    // guide:begin managed-gameplay
    [RequireComponent(typeof(BehaviorTreeAgent))]
    public sealed class Fighter : MonoBehaviour
    {
        public Transform enemy;
        private BehaviorTreeAgent _agent;

        public float Health => _agent.Blackboard.Get<float>("health");          // tree -> game

        public void TakeDamage(float amount) =>
            _agent.Blackboard.Set("health", Health - amount);                   // game -> tree

        private void Start()
        {
            _agent = GetComponent<BehaviorTreeAgent>();
            _agent.SetReference("enemy", enemy);                                // nodes use reference "enemy"
            _agent.Events.Subscribe("healed", agent => Debug.Log(agent.name + " is healed"));
            _agent.Completed += result => Debug.Log("Tree finished: " + result);
        }
    }
    // guide:end

    public sealed class UserGuideSnippetTests
    {
        private const string HealTree = "{ \"format\": \"aibt.tree\", \"formatVersion\": 2, \"treeId\": \"guide.heal\", \"name\": \"Heal\", \"root\": \"heal-when-low\", "
            + "\"blackboard\": { \"health\": { \"type\": \"Float32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 100.0 } }, "
            + "\"nodes\": { "
            + "\"heal-when-low\": { \"type\": \"aibt.core.memory-sequence\", \"typeVersion\": 1, \"children\": [\"low\", \"heal\", \"tell\"] }, "
            + "\"low\": { \"type\": \"game.is-low-health\", \"typeVersion\": 1, \"parameters\": { \"health\": \"health\", \"threshold\": 30.0 } }, "
            + "\"heal\": { \"type\": \"game.heal\", \"typeVersion\": 1, \"parameters\": { \"health\": \"health\", \"per-second\": 50.0 } }, "
            + "\"tell\": { \"type\": \"aibt.unity.raise-event\", \"typeVersion\": 1, \"parameters\": { \"event-name\": \"healed\" } } } }";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) if (created != null) UnityEngine.Object.DestroyImmediate(created);
            _created.Clear();
        }

        [Test]
        public void TheGuideNodesAndGameplayComponentRunTogether()
        {
            var types = Agents.StandardNodeKit.StandardTypes.Concat(new[] { typeof(HealNode), typeof(IsLowHealthNode) }).ToArray();
            var nodes = ManagedNodeSet.Create(types);
            Assert.That(nodes.Problems, Is.Empty, string.Join("\n", nodes.Problems));

            var gameObject = new GameObject("guide-fighter");
            _created.Add(gameObject);
            var agent = gameObject.AddComponent<BehaviorTreeAgent>();
            agent.TickMode = AgentTickMode.Manual;
            agent.RestartOnComplete = false;
            var source = BehaviorTreeSource.FromJson(HealTree, "guide-heal", nodes);
            _created.Add(source);
            agent.Tree = source;
            Assert.That(agent.Blackboard, Is.Not.Null, agent.LastError);

            var fighter = gameObject.AddComponent<Fighter>();
            typeof(Fighter).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(fighter, null);
            var healed = 0;
            agent.Events.Subscribe("healed", _ => healed++);

            fighter.TakeDamage(90f);
            Assert.That(fighter.Health, Is.EqualTo(10f));
            for (var tick = 0; tick < 100 && agent.LastResult == null; tick++) agent.Tick(0.1f);

            Assert.That(agent.HasFaulted, Is.False, agent.LastError);
            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(fighter.Health, Is.EqualTo(100f));
            Assert.That(healed, Is.EqualTo(1));
        }

        [Test]
        public void ReadmeManagedSnippetsAreCopiedFromThisFile()
        {
            var readme = Normalize(File.ReadAllText(Path.Combine(PackageRoot(), "README.md")));
            var source = File.ReadAllText(Path.Combine(PackageRoot(), "Tests", "Editor", "Documentation", "UserGuideSnippetTests.cs"));
            var regions = Regex.Matches(Normalize(source), @"// guide:begin (?<name>[a-z-]+)\n(?<body>.*?)\n\s*// guide:end", RegexOptions.Singleline);
            Assert.That(regions.Count, Is.EqualTo(2));
            foreach (Match region in regions)
            {
                var body = string.Join("\n", region.Groups["body"].Value.Split('\n').Select(line => line.StartsWith("    ", StringComparison.Ordinal) ? line.Substring(4) : line));
                Assert.That(readme, Does.Contain(body), "README.md must quote the '" + region.Groups["name"].Value + "' region verbatim.");
            }
        }

        [Test]
        public void ReadmeNativeSnippetsAreQuotedFromTheFirstAgentSample()
        {
            var readme = Normalize(File.ReadAllText(Path.Combine(PackageRoot(), "README.md")));
            var blocks = Regex.Matches(readme, "```csharp\n(?<body>.*?)```", RegexOptions.Singleline)
                .Cast<Match>().Select(match => match.Groups["body"].Value).ToList();
            AssertQuoted(blocks, Path.Combine("Samples~", "FirstAgent", "Nodes", "MoveNode.cs"), "AibtAction");
            AssertQuoted(blocks, Path.Combine("Samples~", "FirstAgent", "Catalog", "FirstAgentCatalog.cs"), "AibtCatalogSet");
            AssertQuoted(blocks, Path.Combine("Samples~", "FirstAgent", "Runtime", "FirstAgent.cs"), "SetFact");
            Assert.That(blocks.Count, Is.EqualTo(5), "Every C# block in README.md is either a guide region or a First Agent quote.");
        }

        // P11-002 stage 2: the "Native node library" page quotes its C# from Samples~/NativeGuard, and
        // its JSON nodes that share an id with the sample tree have the sample's node type.
        [Test]
        public void NativeLibraryGuideSnippetsAreQuotedFromTheNativeGuardSample()
        {
            var guide = Normalize(File.ReadAllText(Path.Combine(PackageRoot(), "Documentation~", "native-node-library.md")));
            var blocks = Regex.Matches(guide, "```csharp\n(?<body>.*?)```", RegexOptions.Singleline)
                .Cast<Match>().Select(match => match.Groups["body"].Value).ToList();
            var sample = Path.Combine("Samples~", "NativeGuard");
            AssertQuoted(blocks, Path.Combine(sample, "Catalog", "NativeGuardCatalog.cs"), "AibtCatalogSet");
            AssertQuoted(blocks, Path.Combine(sample, "Runtime", "NativeGuard.cs"), "OnEffect");
            AssertQuoted(blocks, Path.Combine(sample, "Runtime", "NativeGuard.cs"), "SetFact");
            Assert.That(blocks.Count, Is.EqualTo(3), "Every C# block in native-node-library.md quotes the Native Guard sample.");

            var tree = Normalize(File.ReadAllText(Path.Combine(PackageRoot(), sample, "native-guard.aibt.json")));
            var sampleTypes = Regex.Matches(tree, "\"(?<id>[a-z-]+)\": \\{\\s*\"type\": \"(?<type>[a-z.-]+)\"")
                .Cast<Match>().ToDictionary(match => match.Groups["id"].Value, match => match.Groups["type"].Value);
            var jsonBlocks = Regex.Matches(guide, "```json\n(?<body>.*?)```", RegexOptions.Singleline).Cast<Match>().Select(match => match.Groups["body"].Value);
            var shared = 0;
            foreach (Match node in Regex.Matches(string.Join("\n", jsonBlocks), "\"(?<id>[a-z-]+)\": \\{ \"type\": \"(?<type>[a-z.-]+)\""))
            {
                if (!sampleTypes.TryGetValue(node.Groups["id"].Value, out var type)) continue;
                shared++;
                Assert.That(node.Groups["type"].Value, Is.EqualTo(type), "Guide node '" + node.Groups["id"].Value + "' differs from the sample tree.");
            }
            Assert.That(shared, Is.GreaterThanOrEqualTo(4), "The shared-cooldown recipe quotes the sample tree.");
        }

        private static void AssertQuoted(IReadOnlyList<string> blocks, string relativePath, string marker)
        {
            var block = blocks.FirstOrDefault(candidate => candidate.Contains(marker) && !candidate.Contains("guide:"));
            Assert.That(block, Is.Not.Null, "README.md has no C# block quoting " + relativePath);
            var sourceLines = new HashSet<string>(Normalize(File.ReadAllText(Path.Combine(PackageRoot(), relativePath)))
                .Split('\n').Select(line => line.Trim()));
            foreach (var line in block.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && line != "..."))
                Assert.That(sourceLines, Does.Contain(line), "README line not found in " + relativePath + ": " + line);
        }

        private static string Normalize(string text) => text.Replace("\r\n", "\n");

        private static string PackageRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BtNode).Assembly);
            return packageInfo != null ? packageInfo.resolvedPath : Path.Combine(Application.dataPath, "AIBT");
        }
    }
}
