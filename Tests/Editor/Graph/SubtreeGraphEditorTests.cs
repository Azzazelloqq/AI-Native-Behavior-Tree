using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Trace;
using AIBT.Tests.Editor.Agents;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// ADR-P11-001 §7 Stage 3: subtrees in the graph editor. Every edit goes through the window's
    /// one undo history; the live debugger maps expanded node IDs to the reference card in the
    /// parent and to one instance in a subtree window.
    /// </summary>
    public sealed class SubtreeGraphEditorTests
    {
        private const string AttackJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.attack"", ""name"": ""Attack"", ""root"": ""seq"",
  ""interface"": { ""version"": 1, ""keys"": [""target-hp""] },
  ""blackboard"": {
    ""cooldown"": { ""type"": ""Int32"", ""default"": 2 },
    ""target-hp"": { ""type"": ""Int32"", ""default"": 7 }
  },
  ""nodes"": {
    ""seq"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""hold""] },
    ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } }
  }
}";

        private const string BossJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.boss"", ""name"": ""Boss"", ""root"": ""root"",
  ""blackboard"": { ""target-hp"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""a"", ""b""] },
    ""a"": { ""displayName"": ""First"", ""subtree"": { ""treeId"": ""tests.p11.attack"", ""interfaceVersion"": 1, ""keys"": { ""target-hp"": ""target-hp"" } } },
    ""b"": { ""displayName"": ""Second"", ""subtree"": { ""treeId"": ""tests.p11.attack"", ""interfaceVersion"": 1, ""keys"": { ""target-hp"": ""target-hp"" } } }
  }
}";

        private const string PlainJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11.plain"", ""name"": ""Plain"", ""root"": ""root"",
  ""blackboard"": { ""target-hp"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""x"", ""group""] },
    ""x"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 1 } },
    ""group"": { ""displayName"": ""Combo"", ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""y"", ""z""] },
    ""y"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""z"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } }
  }
}";

        // A parent whose references fail in three ways: unknown tree ID, a key the interface lacks, and recursion.
        private const string BrokenJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.broken"", ""name"": ""Broken"", ""root"": ""root"",
  ""blackboard"": { ""target-hp"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""missing"", ""badkey"", ""loop""] },
    ""missing"": { ""subtree"": { ""treeId"": ""tests.p11.nowhere"", ""interfaceVersion"": 1, ""keys"": {} } },
    ""badkey"": { ""subtree"": { ""treeId"": ""tests.p11.attack"", ""interfaceVersion"": 1, ""keys"": { ""nope"": ""target-hp"" } } },
    ""loop"": { ""subtree"": { ""treeId"": ""tests.p11.loop"", ""interfaceVersion"": 1, ""keys"": {} } }
  }
}";

        private const string LoopJson = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.loop"", ""name"": ""Loop"", ""root"": ""again"",
  ""nodes"": {
    ""again"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""back""] },
    ""back"": { ""subtree"": { ""treeId"": ""tests.p11.broken"", ""interfaceVersion"": 1, ""keys"": {} } }
  }
}";

        private static AIBT.Authoring.NodeRegistry Registry() => NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;

        private static NodeId Id(string value) => new NodeId(value);

        private static IEnumerable<string> ChildIds(TreeDocument document, string parent)
            => GraphDocumentEdits.Find(document, Id(parent)).Children.Select(id => id.Value);

        private static NodeDocument Node(BehaviorTreeGraphWindow window, string id) => GraphDocumentEdits.Find(window.Document, Id(id));

        private static string Problems(BehaviorTreeGraphWindow window) => string.Join("; ", window.Problems.Select(item => item.Node?.Value + ": " + item.Text));

        // ---------------------------------------------------------------- palette and picker

        [Test]
        public void Palette_ListsSubtreeFirstOnlyWhenAsked()
        {
            var with = NodeSearchProvider.BuildTree(Registry(), "Create Node", true);
            Assert.That(with[1].name, Is.EqualTo(NodeSearchProvider.SubtreeEntryLabel));
            Assert.That(with[1].userData, Is.InstanceOf<NodeSearchProvider.SubtreeEntry>());
            var without = NodeSearchProvider.BuildTree(Registry(), "Create Node");
            Assert.That(without.Any(entry => entry.userData is NodeSearchProvider.SubtreeEntry), Is.False, "The node list itself is unchanged.");
            Assert.That(with.Count, Is.EqualTo(without.Count + 1));
        }

        [Test]
        public void Picker_ListsProjectTreesWithoutTheOpenTree()
        {
            using (var files = new TreeFiles())
            {
                var entries = new[] { TreeCatalogEntry.Read(files.Attack), TreeCatalogEntry.Read(files.Boss), TreeCatalogEntry.Read(files.Plain) };
                var tree = SubtreePickerProvider.BuildTree(entries, files.Boss, "tests.p11.boss");
                var names = tree.Skip(1).Select(entry => entry.name).ToList();
                Assert.That(names, Is.EqualTo(new[] { "Attack  (tests.p11.attack)", "Plain  (tests.p11.plain)" }));
                Assert.That(tree.Skip(1).All(entry => entry.userData is TreeCatalogEntry), Is.True);
            }
        }

        // ---------------------------------------------------------------- card

        [UnityTest]
        public IEnumerator ReferenceCard_ShowsSubtreeNameBadgeAndOneRowPerInterfaceKey()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Boss, Registry());
                yield return null;
                Assert.That(window.Problems.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty, Problems(window));
                var card = window.View.NodesById[Id("a")];
                Assert.That(card.IsSubtreeReference, Is.True);
                Assert.That(card.ShowsSubtreeBadge, Is.True);
                Assert.That(card.title, Is.EqualTo("First"));
                Assert.That(card.KindText, Does.Contain("tests.p11.attack"));
                Assert.That(card.SubtreeRows, Is.EqualTo(new[] { "target-hp → target-hp" }));
                Assert.That(card.OutputPort, Is.Null, "A reference has no children.");
                Assert.That(window.View.NodesById[Id("root")].ShowsSubtreeBadge, Is.False);
            }
            return WithWindow(Check);
        }

        // ---------------------------------------------------------------- edits with undo/redo

        [UnityTest]
        public IEnumerator AddReference_UsesAShortIdConnectsSameNamedKeysAndUndoes()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Plain, Registry());
                yield return null;
                var before = window.Document;
                var added = window.AddSubtreeReference(new TreeId("tests.p11.attack"), Id("root"));
                Assert.That(added, Is.EqualTo(Id("attack")));
                var reference = Node(window, "attack");
                Assert.That(reference.Subtree.TreeId.Value, Is.EqualTo("tests.p11.attack"));
                Assert.That(reference.Subtree.InterfaceVersion, Is.EqualTo(1u));
                Assert.That(reference.Subtree.TryGetBinding("target-hp", out var binding) && binding.ParentKey == "target-hp", Is.True,
                    "The same-named parent key is connected.");
                Assert.That(ChildIds(window.Document, "root").Last(), Is.EqualTo("attack"));
                Assert.That(window.Document.FormatVersion, Is.EqualTo(TreeDocument.SubtreeFormatVersion));
                Assert.That(window.Problems.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty, Problems(window));

                Assert.That(window.AddSubtreeReference(new TreeId("tests.p11.attack"), null), Is.EqualTo(Id("attack-2")), "IDs stay unique.");
                window.UndoEdit();
                window.UndoEdit();
                yield return null;
                Assert.That(window.Document.FormatVersion, Is.EqualTo(before.FormatVersion));
                Assert.That(Node(window, "attack"), Is.Null);
                Assert.That(window.View.NodesById.ContainsKey(Id("attack")), Is.False);
                window.RedoEdit();
                Assert.That(Node(window, "attack")?.Subtree, Is.Not.Null);
                Assert.That(window.View.NodesById[Id("attack")].IsSubtreeReference, Is.True);
                Assert.That(window.AddSubtreeReference(new TreeId("tests.p11.plain"), null), Is.Null, "A tree cannot use itself.");
            }
            return WithWindow(Check);
        }

        [UnityTest]
        public IEnumerator KeyMap_ConstantAndDisconnectEditsAreUndoable()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Boss, Registry());
                yield return null;
                Assert.That(window.SetSubtreeKeyBinding(Id("a"), "target-hp", SubtreeKeyBinding.ToConstant("target-hp", SemanticValue.FromInt64(5))), Is.True);
                Assert.That(Node(window, "a").Subtree.TryGetBinding("target-hp", out var constant) && constant.IsConstant, Is.True);
                Assert.That(window.View.NodesById[Id("a")].SubtreeRows, Is.EqualTo(new[] { "target-hp = 5" }));
                Assert.That(window.Problems.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty, Problems(window));

                Assert.That(window.SetSubtreeKeyBinding(Id("a"), "target-hp", null), Is.True);
                Assert.That(Node(window, "a").Subtree.Keys, Is.Empty);
                Assert.That(window.View.NodesById[Id("a")].SubtreeRows, Is.EqualTo(new[] { "target-hp: not connected" }));

                window.UndoEdit();
                Assert.That(window.View.NodesById[Id("a")].SubtreeRows, Is.EqualTo(new[] { "target-hp = 5" }));
                window.UndoEdit();
                yield return null;
                Assert.That(window.View.NodesById[Id("a")].SubtreeRows, Is.EqualTo(new[] { "target-hp → target-hp" }));
                Assert.That(window.HasPendingChanges, Is.False);
                window.RedoEdit();
                Assert.That(Node(window, "a").Subtree.TryGetBinding("target-hp", out constant) && constant.IsConstant, Is.True);
            }
            return WithWindow(Check);
        }

        [UnityTest]
        public IEnumerator CreateMissingParentKey_AddsTheKeyAndConnectsItInOneStep()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Unbound, Registry());
                yield return null;
                Assert.That(window.Document.Blackboard, Is.Empty);
                Assert.That(window.CreateParentKeyFor(Id("a"), "target-hp"), Is.True);
                var key = window.Document.Blackboard.Single();
                Assert.That(key.Id, Is.EqualTo("target-hp"));
                Assert.That(key.Type.ValueType, Is.EqualTo(BlackboardValueType.Int32));
                Assert.That(key.Scope, Is.EqualTo(BlackboardScope.Tree));
                Assert.That(Node(window, "a").Subtree.TryGetBinding("target-hp", out var binding) && binding.ParentKey == "target-hp", Is.True);

                window.UndoEdit();
                yield return null;
                Assert.That(window.Document.Blackboard, Is.Empty, "One undo removes the key and the binding.");
                Assert.That(Node(window, "a").Subtree.Keys, Is.Empty);
                window.RedoEdit();
                Assert.That(window.Document.Blackboard.Count, Is.EqualTo(1));

                // With the key present, "connect same-named keys" fills the second reference.
                Assert.That(Node(window, "b").Subtree.Keys, Is.Empty);
                Assert.That(window.ConnectSameNamedSubtreeKeys(Id("b")), Is.True);
                Assert.That(Node(window, "b").Subtree.TryGetBinding("target-hp", out binding) && binding.ParentKey == "target-hp", Is.True);
                window.UndoEdit();
                Assert.That(Node(window, "b").Subtree.Keys, Is.Empty);
            }
            return WithWindow(Check);
        }

        [UnityTest]
        public IEnumerator Extract_ReplacesTheNodesByAReference_UndoLeavesNoFile_SaveWritesTheSubtree()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Plain, Registry());
                yield return null;
                var target = Path.Combine(files.Directory, "combo.aibt.json").Replace('\\', '/');
                var reference = window.ExtractToSubtree(Id("group"), target);
                Assert.That(reference, Is.EqualTo(Id("combo")), Problems(window));
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "x", "combo" }));
                Assert.That(Node(window, "group"), Is.Null);
                Assert.That(Node(window, "y"), Is.Null);
                var subtreeRef = Node(window, "combo").Subtree;
                Assert.That(subtreeRef.TreeId.Value, Is.EqualTo("tests.p11.plain.combo"));
                Assert.That(window.PendingSubtreePaths, Is.EqualTo(new[] { target }));
                Assert.That(File.Exists(target), Is.False, "The new file is written on Save, not before.");
                Assert.That(window.Problems.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty, Problems(window));
                Assert.That(window.View.NodesById[Id("combo")].IsSubtreeReference, Is.True);

                window.UndoEdit();
                yield return null;
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "x", "group" }));
                Assert.That(window.HasPendingChanges, Is.False);
                Assert.That(window.SaveAll(), Is.True);
                Assert.That(File.Exists(target), Is.False, "An undone extraction writes nothing.");

                window.RedoEdit();
                Assert.That(Node(window, "combo")?.Subtree, Is.Not.Null);
                Assert.That(window.SaveAll(), Is.True, window.BannerText);
                Assert.That(File.Exists(target), Is.True);
                Assert.That(window.PendingSubtreePaths, Is.Empty);
                var written = CanonicalTreeJson.Parse(File.ReadAllBytes(target), target);
                Assert.That(written.Success, Is.True);
                Assert.That(written.Document.TreeId.Value, Is.EqualTo("tests.p11.plain.combo"));
                Assert.That(written.Document.Root, Is.EqualTo(Id("group")));
                Assert.That(written.Document.Nodes.Select(node => node.Id.Value).OrderBy(id => id, StringComparer.Ordinal), Is.EqualTo(new[] { "group", "y", "z" }));
                var parent = CanonicalTreeJson.Parse(File.ReadAllBytes(files.Plain), files.Plain);
                Assert.That(parent.Document.FormatVersion, Is.EqualTo(TreeDocument.SubtreeFormatVersion));
                Assert.That(GraphDocumentEdits.Find(parent.Document, Id("combo")).Subtree.TreeId.Value, Is.EqualTo("tests.p11.plain.combo"));

                Assert.That(window.ExtractToSubtree(Id("root"), Path.Combine(files.Directory, "root.aibt.json")), Is.Null, "The root cannot be extracted.");
                Assert.That(window.ExtractToSubtree(Id("x"), target), Is.Null, "Existing files are not overwritten.");
            }
            return WithWindow(Check, resolverIncludesDirectory: true);
        }

        [UnityTest]
        public IEnumerator Unpack_ReplacesTheReferenceByItsExpansion_AndUndoes()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Boss, Registry());
                yield return null;
                Assert.That(window.UnpackSubtree(Id("a")), Is.True, window.LastSubtreeCommandProblem);
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "a:seq", "b" }));
                Assert.That(Node(window, "a"), Is.Null);
                Assert.That(Node(window, "a:hold").TypeId, Is.EqualTo("aibt.stdlib.wait"));
                Assert.That(Node(window, "a:hold").DisplayName, Is.EqualTo("First / hold"));
                Assert.That(window.Document.Blackboard.Select(key => key.Id), Is.EquivalentTo(new[] { "target-hp", "a:cooldown" }),
                    "The private key comes along under the reference prefix; the bound key stays the parent's.");
                Assert.That(window.View.NodesById.ContainsKey(Id("a:hold")), Is.True);
                Assert.That(window.Problems.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty, Problems(window));

                window.UndoEdit();
                yield return null;
                Assert.That(ChildIds(window.Document, "root"), Is.EqualTo(new[] { "a", "b" }));
                Assert.That(window.Document.Blackboard.Count, Is.EqualTo(1));
                Assert.That(window.View.NodesById.ContainsKey(Id("a:hold")), Is.False);
                window.RedoEdit();
                Assert.That(Node(window, "a:seq"), Is.Not.Null);
            }
            return WithWindow(Check);
        }

        // ---------------------------------------------------------------- diagnostics

        [UnityTest]
        public IEnumerator ReferenceProblems_AreBadgedOnTheReferenceCard()
        {
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Broken, Registry());
                yield return null;
                var all = Problems(window);
                foreach (var id in new[] { "missing", "badkey", "loop" })
                {
                    Assert.That(window.Problems.Any(item => item.Node == Id(id) && item.Severity == DiagnosticSeverity.Error), Is.True,
                        id + " has an error: " + all);
                    Assert.That(window.View.NodesById[Id(id)].ProblemText, Is.Not.Null.And.Not.Empty);
                }
                Assert.That(all, Does.Contain(SubtreeExpansionDiagnosticCodes.SubtreeNotFound.Value));
                Assert.That(all, Does.Contain(SubtreeExpansionDiagnosticCodes.SubtreeCycle.Value));
                Assert.That(window.View.NodesById[Id("missing")].Summary, Is.EqualTo("Subtree not found"));
            }
            return WithWindow(Check);
        }

        // ---------------------------------------------------------------- navigation

        [UnityTest]
        public IEnumerator OpenSubtree_OpensItsOwnWindowWithABreadcrumbBack()
        {
            BehaviorTreeGraphWindow child = null;
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Boss, Registry());
                yield return null;
                child = window.OpenSubtree(Id("b"));
                Assert.That(child, Is.Not.Null);
                Assert.That(child, Is.Not.SameAs(window));
                Assert.That(child.SourcePath, Is.EqualTo(files.Attack));
                Assert.That(child.InstancePrefix, Is.EqualTo("b:"));
                Assert.That(child.BreadcrumbLabels, Is.EqualTo(new[] { "Boss" }));
                Assert.That(child.BreadcrumbPaths, Is.EqualTo(new[] { files.Boss }));
                Assert.That(child.Document.TreeId.Value, Is.EqualTo("tests.p11.attack"));
                Assert.That(window.Document.FormatVersion, Is.EqualTo(3), "Navigation never changes the parent.");
                Assert.That(window.HasPendingChanges, Is.False);
                Assert.That(child.NavigateBack(0), Is.SameAs(window));
                Assert.That(window.OpenSubtree(Id("b")), Is.SameAs(child), "The same instance reuses its window.");
            }
            CloseLater(() => child);
            return WithWindow(Check);
        }

        // ---------------------------------------------------------------- live debugging

        [UnityTest]
        public IEnumerator LiveDebugger_HighlightsTheReferenceInTheParentAndOneInstanceInEachSubtreeWindow()
        {
            var opened = new List<BehaviorTreeGraphWindow>();
            IEnumerator Check(BehaviorTreeGraphWindow window, TreeFiles files)
            {
                window.OpenFromPath(files.Boss, Registry());
                yield return null;
                var program = Compile(BossJson, files.LogicalResolver());
                Assert.That(window.MatchesProgram(program), Is.True, "The graph matches the program compiled from its expansion.");
                var changed = new InMemorySubtreeResolver().Add(
                    CanonicalTreeJson.Parse(AttackJson.Replace("\"ticks\": 3", "\"ticks\": 4"), "trees/attack.aibt.json").Document, "trees/attack.aibt.json");
                Assert.That(window.MatchesProgram(Compile(BossJson, changed)), Is.False, "A changed subtree is another program.");

                using (var scene = new SceneAgent("subtree-live"))
                {
                    scene.Agent.Tree = scene.Track(BehaviorTreeSource.FromJson(BossJson, "boss", files.LogicalResolver()));
                    Assert.That(scene.Agent.Blackboard, Is.Not.Null, scene.Agent.LastError);
                    window.ShowLive(scene.Agent);
                    scene.Agent.Tick(0.1f);
                    window.LiveDebugger.Update(EditorApplication.timeSinceStartup);
                    Assert.That(window.LiveDebugger.RunningNodes, Is.EquivalentTo(new[] { Id("root"), Id("a") }),
                        "Inner nodes a:seq and a:hold light the reference card a.");

                    var first = window.OpenSubtree(Id("a"));
                    var second = window.OpenSubtree(Id("b"));
                    opened.Add(first);
                    opened.Add(second);
                    Assert.That(first, Is.Not.SameAs(second), "Each instance has its own window.");
                    Assert.That(first.LiveDebugger.IsAttached && second.LiveDebugger.IsAttached, Is.True);
                    Assert.That(first.MatchesProgram(program) && second.MatchesProgram(program), Is.True);
                    first.LiveDebugger.Update(EditorApplication.timeSinceStartup);
                    second.LiveDebugger.Update(EditorApplication.timeSinceStartup);
                    Assert.That(first.LiveDebugger.RunningNodes, Is.EquivalentTo(new[] { Id("seq"), Id("hold") }));
                    Assert.That(second.LiveDebugger.RunningNodes, Is.Empty, "Instance b has not started.");
                    Assert.That(first.LiveKeyName("cooldown"), Is.EqualTo("a:cooldown"));
                    Assert.That(first.LiveKeyName("target-hp"), Is.EqualTo("target-hp"), "A bound interface key reads the parent's key.");

                    for (var tick = 0; tick < 3; tick++) scene.Agent.Tick(0.1f);
                    var now = EditorApplication.timeSinceStartup;
                    window.LiveDebugger.Update(now);
                    first.LiveDebugger.Update(now);
                    second.LiveDebugger.Update(now);
                    Assert.That(window.LiveDebugger.RunningNodes, Is.EquivalentTo(new[] { Id("root"), Id("b") }));
                    Assert.That(first.LiveDebugger.RunningNodes, Is.Empty);
                    Assert.That(second.LiveDebugger.RunningNodes, Is.EquivalentTo(new[] { Id("seq"), Id("hold") }));
                    Assert.That(window.LiveDebugger.ActiveEdges, Does.Contain((Id("root"), Id("b"))));
                }
            }
            CloseLater(() => opened.ToArray());
            return WithWindow(Check);
        }

        [Test]
        public void TraceTimeline_MapsExpandedNodesToTheReferenceAndShowsTwoPartLabels()
        {
            using (var files = new TreeFiles())
            {
                var program = Compile(BossJson, files.LogicalResolver());
                var document = CanonicalTreeJson.Parse(BossJson, files.Boss).Document;
                var trace = ScriptableObject.CreateInstance<TraceTimelineWindow>();
                try
                {
                    trace.LoadGraphContext(document, Registry(), program);
                    uint Index(string id) => program.DebugMap.Single(entry => entry.AuthoringNodeId.Value == id).RuntimeNodeIndex;
                    Assert.That(trace.CardOf(Index("a:hold")), Is.EqualTo(Id("a")));
                    Assert.That(trace.CardOf(Index("b:seq")), Is.EqualTo(Id("b")));
                    Assert.That(trace.CardOf(Index("root")), Is.EqualTo(Id("root")));
                    Assert.That(trace.LabelOf(Index("a:hold")), Is.EqualTo("First / hold"));
                    Assert.That(trace.LabelOf(Index("b:seq")), Is.EqualTo("Second / seq"));
                }
                finally
                {
                    Object.DestroyImmediate(trace);
                }
            }
        }

        // ---------------------------------------------------------------- fixtures

        private static CompiledProgram Compile(string json, ISubtreeResolver resolver)
        {
            var document = CanonicalTreeJson.Parse(json, "trees/boss.aibt.json").Document;
            var result = ReferenceCompiler.Compile(document, Registry(), new ReferenceCompilerOptions("trees/boss.aibt.json",
                ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0)), resolver);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Code.Value + ": " + d.Message)));
            return result.Program;
        }

        private static readonly List<Func<object>> s_closeLater = new List<Func<object>>();

        private static void CloseLater(Func<object> windows) => s_closeLater.Add(windows);

        [TearDown]
        public void CloseOpenedWindows()
        {
            foreach (var pending in s_closeLater)
            {
                var value = pending();
                var windows = value as BehaviorTreeGraphWindow[] ?? (value is BehaviorTreeGraphWindow single ? new[] { single } : Array.Empty<BehaviorTreeGraphWindow>());
                foreach (var window in windows.Where(window => window != null))
                {
                    if (window.hasUnsavedChanges) window.DiscardChanges();
                    window.Close();
                }
            }
            s_closeLater.Clear();
        }

        private static IEnumerator WithWindow(Func<BehaviorTreeGraphWindow, TreeFiles, IEnumerator> action, bool resolverIncludesDirectory = false)
        {
            var files = new TreeFiles();
            ISubtreeResolver resolver;
            try
            {
                resolver = resolverIncludesDirectory ? (ISubtreeResolver)new DirectoryResolver(files) : files.Resolver();
            }
            catch
            {
                files.Dispose();
                throw;
            }
            var window = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            try
            {
                window.SubtreeResolver = resolver;
                window.Show();
                yield return action(window, files);
            }
            finally
            {
                if (window.hasUnsavedChanges) window.DiscardChanges();
                window.Close();
                files.Dispose();
            }
        }

        /// <summary>The fixture trees in a temporary folder, with a resolver that maps their tree IDs to those files.</summary>
        private sealed class TreeFiles : IDisposable
        {
            internal TreeFiles()
            {
                Directory = Path.Combine(Path.GetTempPath(), "aibt-subtree-graph-tests", Guid.NewGuid().ToString("N")).Replace('\\', '/');
                System.IO.Directory.CreateDirectory(Directory);
                Attack = Write("attack.aibt.json", AttackJson);
                Boss = Write("boss.aibt.json", BossJson);
                Plain = Write("plain.aibt.json", PlainJson);
                Broken = Write("broken.aibt.json", BrokenJson);
                Loop = Write("loop.aibt.json", LoopJson);
                Unbound = Write("unbound.aibt.json", BossJson.Replace("tests.p11.boss", "tests.p11.unbound")
                    .Replace(@"""blackboard"": { ""target-hp"": { ""type"": ""Int32"", ""default"": 0 } },", string.Empty)
                    .Replace(@"""keys"": { ""target-hp"": ""target-hp"" }", @"""keys"": {}"));
            }

            internal string Directory { get; }
            internal string Attack { get; }
            internal string Boss { get; }
            internal string Plain { get; }
            internal string Broken { get; }
            internal string Loop { get; }
            internal string Unbound { get; }

            internal InMemorySubtreeResolver Resolver()
            {
                var resolver = new InMemorySubtreeResolver();
                foreach (var path in new[] { Attack, Boss, Plain, Broken, Loop, Unbound })
                {
                    var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
                    Assert.That(parsed.Success, Is.True, path + ": " + string.Join("; ", parsed.Diagnostics.Select(d => d.Code.Value + " " + d.Message)));
                    resolver.Add(parsed.Document, path);
                }
                return resolver;
            }

            /// <summary>The same trees under logical source IDs ("trees/attack.aibt.json"), as compiled programs require.</summary>
            internal InMemorySubtreeResolver LogicalResolver()
            {
                var resolver = new InMemorySubtreeResolver();
                foreach (var path in new[] { Attack, Boss, Plain })
                {
                    var logical = "trees/" + Path.GetFileName(path);
                    resolver.Add(CanonicalTreeJson.Parse(File.ReadAllBytes(path), logical).Document, logical);
                }
                return resolver;
            }

            private string Write(string name, string json)
            {
                var path = Directory + "/" + name;
                File.WriteAllText(path, json);
                return path;
            }

            public void Dispose()
            {
                if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
            }
        }

        /// <summary>Reads the fixture folder on every lookup, so files written by Save resolve like project files.</summary>
        private sealed class DirectoryResolver : ISubtreeResolver
        {
            private readonly TreeFiles _files;

            internal DirectoryResolver(TreeFiles files)
            {
                _files = files;
            }

            public SubtreeResolution Resolve(TreeId treeId)
            {
                var found = System.IO.Directory.GetFiles(_files.Directory, "*.aibt.json")
                    .Select(path => path.Replace('\\', '/'))
                    .Select(path => new KeyValuePair<string, TreeDocument>(path, CanonicalTreeJson.Parse(File.ReadAllBytes(path), path).Document))
                    .Where(pair => pair.Value != null && pair.Value.TreeId == treeId)
                    .ToList();
                if (found.Count == 0) return SubtreeResolution.NotFound();
                return found.Count == 1 ? SubtreeResolution.Found(found[0].Value, found[0].Key) : SubtreeResolution.Ambiguous(found.Select(pair => pair.Key));
            }
        }
    }
}
