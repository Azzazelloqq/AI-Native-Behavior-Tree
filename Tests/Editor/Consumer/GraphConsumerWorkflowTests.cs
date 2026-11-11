using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Preview;
using AIBT.Editor.Trace;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AIBT.Tests.Consumer
{
    public sealed class GraphConsumerWorkflowTests
    {
        [Test]
        public void OpeningAnUnrelatedAssetIsNotClaimedByAibt()
        {
            var unrelated = ScriptableObject.CreateInstance<ProductionTreeAsset>();
            try
            {
#if UNITY_6000_7_OR_NEWER
                object id = unrelated.GetEntityId();
#else
                object id = unrelated.GetInstanceID();
#endif
                var method = typeof(BehaviorTreeImporter).GetMethod("OpenTree", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method.Invoke(null, new[] { id, (object)1 }), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(unrelated); }
        }

        [Test]
        public void PortConnectionsAppendInExplicitOrderAndRejectCyclesAndFullParents()
        {
            var root = new NodeDocument(new NodeId("root"), BuiltInNodeManifests.MemorySequenceTypeId, 1, new[] { new NodeId("a") }, parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var a = new NodeDocument(new NodeId("a"), BuiltInNodeManifests.InverterTypeId, 1, parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var b = new NodeDocument(new NodeId("b"), BuiltInNodeManifests.InverterTypeId, 1, parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var c = new NodeDocument(new NodeId("c"), BuiltInNodeManifests.MemorySequenceTypeId, 1, parameters: SemanticObject.Empty, tags: TagSet.Empty);
            var tree = TreeDocument.CreateVersion2(new TreeId("tree.test.connections"), "Connections", root.Id, new[] { root, a, b, c }, null, null, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var session = new GraphEditSession(tree, registry, new ReferenceCompilerOptions("trees/connections.aibt.json",
                ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)), null);
            Assert.That(session.TryConnect(root.Id, b.Id), Is.True);
            Assert.That(session.Document.Nodes.First(node => node.Id == root.Id).Children, Is.EqualTo(new[] { a.Id, b.Id }));
            Assert.That(session.TryConnect(a.Id, c.Id), Is.True);
            Assert.That(session.CanConnect(a.Id, b.Id), Is.False, "A decorator accepts one child.");
            Assert.That(session.CanConnect(c.Id, a.Id), Is.False, "A descendant must not become its ancestor's parent.");
            Assert.That(session.CanConnect(b.Id, root.Id), Is.False);
            Assert.That(session.TryConnect(c.Id, b.Id), Is.True, "Dragging an existing child reparents it atomically.");
            Assert.That(session.Document.Nodes.First(node => node.Id == root.Id).Children, Is.EqualTo(new[] { a.Id }));
            session.Undo();
            Assert.That(session.Document.Nodes.First(node => node.Id == root.Id).Children, Is.EqualTo(new[] { a.Id, b.Id }));
            session.Redo();
            Assert.That(session.Document.Nodes.First(node => node.Id == c.Id).Children, Is.EqualTo(new[] { b.Id }));
        }

        [Test]
        public void PreviewUsesTheSameGraphNodesAndPreservesLayoutUntilSourceChanges()
        {
            var tree = WaitTree();
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var graph = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            var preview = ScriptableObject.CreateInstance<BehaviorTreePreviewWindow>();
            try
            {
                graph.OpenFromBytes(CanonicalTreeJson.Serialize(tree).Utf8, "trees/preview.aibt.json", registry);
                var node = graph.View.NodesById[tree.Root];
                node.SetPosition(new Rect(123f, 456f, 100f, 100f));
                preview.AttachToGraph(graph, "trees/preview.aibt.json");
                typeof(BehaviorTreePreviewWindow).GetMethod("OnRunTickClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(preview, null);
                Assert.That(graph.View.NodesById[tree.Root], Is.SameAs(node));
                Assert.That(node.style.left.value.value, Is.EqualTo(123f));
                Assert.That(node.style.top.value.value, Is.EqualTo(456f));
                graph.OpenFromBytes(CanonicalTreeJson.Serialize(tree).Utf8, "trees/changed.aibt.json", registry);
                Assert.That(typeof(BehaviorTreePreviewWindow).GetField("_sharedWindow", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(preview); UnityEngine.Object.DestroyImmediate(graph); }
        }

        [Test]
        public void LiveTraceMapsTheExactProgramAndRejectsAChangedSource()
        {
            var tree = WaitTree();
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var compiled = ReferenceCompiler.Compile(tree, registry, new ReferenceCompilerOptions("trees/live.aibt.json",
                ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(compiled.Success, Is.True);
            var graph = ScriptableObject.CreateInstance<BehaviorTreeGraphWindow>();
            var trace = ScriptableObject.CreateInstance<TraceTimelineWindow>();
            var obj = new GameObject("consumer graph trace");
            var host = obj.AddComponent<ProductionTreeHost>();
            try
            {
                graph.OpenFromBytes(CanonicalTreeJson.Serialize(tree).Utf8, "trees/live.aibt.json", registry);
                Assert.That(host.TryBootstrap(compiled.Program, _ => NodeStatus.Running, new NativeTraceChannelCapacityV1(32, 0, 0, 32), out _), Is.True);
                Assert.That(SelectedHostTraceMenu.TryCreateSession(host, out var session, out _), Is.True);
                Assert.That(graph.MatchesProgram(host.Program), Is.True);
                var node = graph.View.NodesById[tree.Root];
                trace.AttachToGraph(graph, session, host.Program);
                trace.Refresh();
                Assert.That(graph.View.NodesById[tree.Root], Is.SameAs(node));
                var changed = tree.Nodes.Single().WithParameters(new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(999)) }));
                var changedTree = TreeDocument.CreateVersion2(tree.TreeId, tree.Name, tree.Root, new[] { changed }, null, null,
                    tags: TagSet.Empty, metadata: SemanticObject.Empty);
                graph.OpenFromBytes(CanonicalTreeJson.Serialize(changedTree).Utf8, "trees/live.aibt.json", registry);
                Assert.That(graph.MatchesProgram(host.Program), Is.False);
                Assert.Throws<ArgumentException>(() => trace.AttachToGraph(graph, session, host.Program));
                Assert.That(host.TraceChannelOwner.State, Is.Not.EqualTo(NativeOwnerStateV1.Disposed));
            }
            finally
            {
                host.Stop();
                UnityEngine.Object.DestroyImmediate(obj);
                UnityEngine.Object.DestroyImmediate(trace);
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static TreeDocument WaitTree()
        {
            var node = new NodeDocument(new NodeId("wait"), "aibt.stdlib.wait", 1,
                parameters: new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(3)) }), tags: TagSet.Empty);
            return TreeDocument.CreateVersion2(new TreeId("tree.test.consumer.wait"), "Wait", node.Id, new[] { node }, null, null,
                tags: TagSet.Empty, metadata: SemanticObject.Empty);
        }
    }
}
