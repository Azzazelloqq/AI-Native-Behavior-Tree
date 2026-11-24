using System.Linq;
using System.Text;
using AIBT.Agents;
using AIBT.Authoring;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// ADR-P11-001 Stage 1 in the managed layer: a tree with subtree references compiles through a
    /// resolver, each instance gets its own private keys (here written through a BlackboardKey
    /// parameter, which the expansion renames), and a <see cref="BehaviorTreeSource"/> can carry
    /// expanded content plus the per-node subtree sources for a Player without the subtree files.
    /// </summary>
    public sealed class SubtreeManagedTests
    {
        internal const string CounterSource = "trees/counter.aibt";
        internal const string ParentSource = "trees/parent.aibt";

        internal const string Counter = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.counter"", ""name"": ""Counter"", ""root"": ""main"",
  ""interface"": { ""version"": 1, ""keys"": [""total""] },
  ""blackboard"": {
    ""count"": { ""type"": ""Int32"", ""default"": 0 },
    ""total"": { ""type"": ""Int32"", ""default"": 0 }
  },
  ""nodes"": {
    ""main"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""own"", ""shared""] },
    ""own"": { ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""count"", ""amount"": 1 } },
    ""shared"": { ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""total"", ""amount"": 10 } }
  }
}";

        internal const string Parent = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.counters"", ""name"": ""Two Counters"", ""root"": ""root"",
  ""blackboard"": { ""sum"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""a"", ""b""] },
    ""a"": { ""displayName"": ""First"", ""subtree"": { ""treeId"": ""tests.p11.counter"", ""interfaceVersion"": 1, ""keys"": { ""total"": ""sum"" } } },
    ""b"": { ""displayName"": ""Second"", ""subtree"": { ""treeId"": ""tests.p11.counter"", ""interfaceVersion"": 1, ""keys"": { ""total"": ""sum"" } } }
  }
}";

        internal static InMemorySubtreeResolver Resolver()
            => new InMemorySubtreeResolver().Add(CanonicalTreeJson.Parse(Counter, CounterSource).Document, CounterSource);

        [Test]
        public void ManagedProgram_RunsBothInstancesWithSeparatePrivateKeys()
        {
            Assert.That(ManagedTreeProgram.TryCompile(Parent, ParentSource, StandardNodeKit.Nodes, Resolver(), out var program, out var diagnostics),
                Is.True, ManagedTreeProgram.Describe(diagnostics));
            Assert.That(program.Document.Blackboard.Select(key => key.Id).OrderBy(id => id, System.StringComparer.Ordinal),
                Is.EqualTo(new[] { "a:count", "b:count", "sum" }));
            Assert.That(program.Program.DebugMap.Single(entry => entry.AuthoringNodeId.Value == "a:own").SourcePath, Is.EqualTo(CounterSource));
            Assert.That(program.Program.DebugMap.Single(entry => entry.AuthoringNodeId.Value == "root").SourcePath, Is.EqualTo(ParentSource));

            var instance = new ManagedTreeInstance(program, null);
            Assert.That(StandardNodeKit.RunToEnd(instance), Is.EqualTo(NodeStatus.Success));
            Assert.That(instance.Blackboard.Get<int>("a:count"), Is.EqualTo(1));
            Assert.That(instance.Blackboard.Get<int>("b:count"), Is.EqualTo(1));
            Assert.That(instance.Blackboard.Get<int>("sum"), Is.EqualTo(20), "The bound interface key is the parent's one key.");

            Assert.That(StandardNodeKit.RunAgain(instance), Is.EqualTo(NodeStatus.Success));
            Assert.That(instance.Blackboard.Get<int>("a:count"), Is.EqualTo(2));
            Assert.That(instance.Blackboard.Get<int>("b:count"), Is.EqualTo(2));
            Assert.That(instance.Blackboard.Get<int>("sum"), Is.EqualTo(40));
        }

        [Test]
        public void ManagedProgram_WithoutAResolverReportsTheMissingSubtree()
        {
            Assert.That(ManagedTreeProgram.TryCompile(Parent, ParentSource, StandardNodeKit.Nodes, out _, out var diagnostics), Is.False);
            Assert.That(diagnostics.Select(item => item.Code), Does.Contain(SubtreeExpansionDiagnosticCodes.SubtreeNotFound));
        }

        [Test]
        public void BehaviorTreeAgent_RunsASourceCreatedWithAResolver()
        {
            using (var scene = new SceneAgent("subtree-agent"))
            {
                var source = scene.Track(BehaviorTreeSource.FromJson(Parent, "subtree-parent", Resolver()));
                scene.Agent.Tree = source;
                Assert.That(scene.Agent.Blackboard, Is.Not.Null, scene.Agent.LastError);

                Assert.That(scene.TickToEnd(), Is.EqualTo(NodeStatus.Success));
                Assert.That(scene.Blackboard.Get<int>("a:count"), Is.EqualTo(1));
                Assert.That(scene.Blackboard.Get<int>("b:count"), Is.EqualTo(1));
                Assert.That(scene.Blackboard.Get<int>("sum"), Is.EqualTo(20));
            }
        }

        /// <summary>
        /// P11-014 item 5 (W3): when the import could not expand the subtrees, it stores the source JSON and
        /// its own errors. Compiling that source without a resolver gave only AIBT3020 ("no subtree resolver
        /// was given"); the import's errors now come first.
        /// </summary>
        [Test]
        public void BehaviorTreeSource_AFailedImport_ReportsTheImportsErrorsFirst()
        {
            const string ImportError = "AIBT3023: Subtree 'tests.p11.counter' declares interface version 2, but the reference asks for 1.";
            var stored = ScriptableObject.CreateInstance<BehaviorTreeSource>();
            try
            {
                stored.SetContent(Parent, ParentSource, "tests.p11.counters", "Two Counters", 3, "Errors", ImportError);
                Assert.That(stored.TryGetProgram(out _, out var error), Is.False);
                Assert.That(error, Does.StartWith(ImportError), error);
                Assert.That(error, Does.Contain("AIBT3020"), "the compile's own diagnostics follow");
            }
            finally
            {
                Object.DestroyImmediate(stored);
            }
        }

        [Test]
        public void BehaviorTreeSource_ExpandedContentCompilesLikeTheResolverPath()
        {
            // What an importer would store (ADR-P11-001 §7 Stage 2 wires BehaviorTreeImporter to this).
            var parent = CanonicalTreeJson.Parse(Parent, ParentSource).Document;
            var registry = ManagedNodeCatalog.Default.Registry;
            var expansion = SubtreeExpander.Expand(parent, Resolver(), registry,
                ManagedTreeProgram.CreateOptions(ParentSource).Policy.CreateValidationOptions(ParentSource));
            Assert.That(expansion.Success, Is.True, ManagedTreeProgram.Describe(expansion.Diagnostics));
            var expandedJson = Encoding.UTF8.GetString(CanonicalTreeJson.Serialize(expansion.Document).Utf8);

            var stored = ScriptableObject.CreateInstance<BehaviorTreeSource>();
            BehaviorTreeSource copy = null;
            try
            {
                stored.SetContent(expandedJson, ParentSource, parent.TreeId.Value, parent.Name, expansion.Document.Nodes.Count, null, null,
                    expansion.NodeSourceIds);
                // Instantiate copies only serialized fields: the per-node sources survive serialization.
                copy = Object.Instantiate(stored);
                Assert.That(copy.TryGetProgram(out var fromAsset, out var error), Is.True, error);
                Assert.That(ManagedTreeProgram.TryCompile(Parent, ParentSource, null, Resolver(), out var fromResolver, out var diagnostics),
                    Is.True, ManagedTreeProgram.Describe(diagnostics));

                Assert.That(fromAsset.Program.Header.CompiledContentHash, Is.EqualTo(fromResolver.Program.Header.CompiledContentHash));
                Assert.That(fromAsset.Program.DebugMap.Select(entry => entry.SourcePath),
                    Is.EqualTo(fromResolver.Program.DebugMap.Select(entry => entry.SourcePath)));

                // The plain overload clears the stored sources: the content compiles as one file again.
                copy.SetContent(expandedJson, ParentSource, parent.TreeId.Value, parent.Name, expansion.Document.Nodes.Count, null, null);
                Assert.That(copy.TryGetProgram(out var plain, out error), Is.True, error);
                Assert.That(plain.Program.DebugMap.All(entry => entry.SourcePath == ParentSource), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(stored);
                if (copy != null) Object.DestroyImmediate(copy);
            }
        }
    }
}
