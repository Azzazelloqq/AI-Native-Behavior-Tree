using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Debugger;
using AIBT.Editor.Trace;
using AIBT.Tests.Editor;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.UIElements;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Editor.Trace
{
    /// <summary>End-to-end: a real compiled tree, a real bounded channel, and the actual window class.</summary>
    public sealed class TraceTimelineWindowTests
    {
        [Test]
        public void SelectedHostRequiresALiveChannel()
        {
            Assert.That(SelectedHostTraceMenu.TryCreateSession(null, out _, out var missing), Is.False);
            Assert.That(missing, Does.Contain("Select"));
            var gameObject = new GameObject("trace host");
            try
            {
                var host = gameObject.AddComponent<ProductionTreeHost>();
                Assert.That(SelectedHostTraceMenu.TryCreateSession(host, out _, out var uninitialized), Is.False);
                Assert.That(uninitialized, Does.Contain("bootstrap"));
            }
            finally { Object.DestroyImmediate(gameObject); }
        }

        [Test]
        public void SelectedLiveHostWithoutAChannelSaysSo()
        {
            // P11-016 follow-up A6: a running host bootstrapped without a channel is not told to bootstrap.
            var node = new NodeDocument(new NodeId("wait"), "aibt.stdlib.wait", 1,
                parameters: new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(1)) }), tags: TagSet.Empty);
            var document = new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.selected-trace-none"), "Trace",
                node.Id, new[] { node }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var compiled = ReferenceCompiler.Compile(document, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry,
                new ReferenceCompilerOptions("trees/trace.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1)));
            Assert.That(compiled.Success, Is.True);
            var gameObject = new GameObject("trace host without channel");
            var host = gameObject.AddComponent<ProductionTreeHost>();
            try
            {
                Assert.That(host.TryBootstrap(compiled.Program, _ => NodeStatus.Success, NativeTraceChannelCapacityV1.None, out var failure),
                    Is.True, failure.Code.ToString());
                Assert.That(host.TraceChannelOwner, Is.Null);
                Assert.That(SelectedHostTraceMenu.TryCreateSession(host, out var session, out var explanation), Is.False);
                Assert.That(session, Is.Null);
                Assert.That(explanation, Does.Contain("runs without a trace channel"));
                Assert.That(explanation, Does.Contain("Trace"));
                Assert.That(explanation, Does.Not.Contain("bootstrap it"));
            }
            finally
            {
                typeof(ProductionTreeHost).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(host, null);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SelectedBootstrappedHostAttachesItsOwnedChannel()
        {
            var node = new NodeDocument(new NodeId("wait"), "aibt.stdlib.wait", 1,
                parameters: new SemanticObject(new[] { new SemanticProperty("ticks", SemanticValue.FromUInt64(1)) }), tags: TagSet.Empty);
            var document = new TreeDocument(TreeDocument.CurrentFormat, 1, new TreeId("tree.test.selected-trace"), "Trace",
                node.Id, new[] { node }, tags: TagSet.Empty, metadata: SemanticObject.Empty);
            var compiled = ReferenceCompiler.Compile(document, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry,
                new ReferenceCompilerOptions("trees/trace.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1)));
            Assert.That(compiled.Success, Is.True);
            var gameObject = new GameObject("trace host");
            var host = gameObject.AddComponent<ProductionTreeHost>();
            try
            {
                var capacity = new NativeTraceChannelCapacityV1(32, 0, 0, 32);
                Assert.That(host.TryBootstrap(compiled.Program, _ => NodeStatus.Success, capacity, out var failure), Is.True, failure.Code.ToString());
                Assert.That(SelectedHostTraceMenu.TryCreateSession(host, out var session, out var explanation), Is.True, explanation);
                Assert.That(session.IsAttached, Is.True);
                Assert.That(session.TryReadTrace(out _, out _), Is.True);
                Assert.That(host.TraceChannelOwner.State, Is.Not.EqualTo(NativeOwnerStateV1.Disposed));
            }
            finally
            {
                typeof(ProductionTreeHost).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(host, null);
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AttachingAnotherHostClearsUnverifiedSourceMapping()
        {
            var path = EditorTestPackagePaths.Resolve("Tests", "Editor", "Preview", "Fixtures", "success-then-running.aibt.json");
            var document = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path).Document;
            var registry = ReferencePreviewDriver.CreatePreviewNodeRegistry();
            var compilation = ReferenceCompiler.Compile(document, registry,
                new ReferenceCompilerOptions("trace/window-fixture.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1)));
            Assert.That(compilation.Success, Is.True);
            var capacity = new NativeTraceChannelCapacityV1(32, 0, 0, 32);
            Assert.That(NativeTraceChannelOwnerV1.TryCreate(capacity, NativeTraceLevelV1.Detailed, new TreeInstanceId(1), 0,
                Allocator.Persistent, out var owner, out _), Is.True);
            var window = ScriptableObject.CreateInstance<TraceTimelineWindow>();
            try
            {
                var session = new NativeExecutionDebuggerSession();
                session.Attach(owner);
                window.AttachSession(session);
                window.LoadGraphContext(document, registry, compilation.Program);
                var graph = window.rootVisualElement.Q<AIBT.Editor.Graph.BehaviorTreeGraphView>();
                Assert.That(graph.NodesById, Is.Not.Empty);
                window.AttachUnmappedSession(session);
                Assert.That(graph.NodesById, Is.Empty);
                Assert.That(window.rootVisualElement.Query<UnityEngine.UIElements.Label>().ToList(),
                    Has.Some.Matches<UnityEngine.UIElements.Label>(label => label.text.Contains("source graph is not mapped")));
                Assert.That(owner.State, Is.Not.EqualTo(NativeOwnerStateV1.Disposed));
            }
            finally { Object.DestroyImmediate(window); owner.TryDispose(out _); }
        }

        [Test]
        public void AttachingASessionAndScrubbingHighlightsTheCorrespondingCompiledNodesWithoutThrowing()
        {
            var path = EditorTestPackagePaths.Resolve("Tests", "Editor", "Preview", "Fixtures", "success-then-running.aibt.json");
            var parseResult = CanonicalTreeJson.Parse(File.ReadAllBytes(path), path);
            Assert.That(parseResult.Success, Is.True);
            var document = parseResult.Document;
            var registry = ReferencePreviewDriver.CreatePreviewNodeRegistry();
            var compilation = ReferenceCompiler.Compile(
                document, registry, new ReferenceCompilerOptions("trace/window-fixture.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 1)));
            Assert.That(compilation.Success, Is.True);

            var rootRuntimeIndex = FindRuntimeIndexForRoot(compilation.Program, document.Root);

            var capacity = new NativeTraceChannelCapacityV1(recordCapacity: 32, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 32);
            Assert.That(NativeTraceChannelOwnerV1.TryCreate(
                capacity, NativeTraceLevelV1.Detailed, new TreeInstanceId(1), 0, Allocator.Persistent, out var owner, out var createFailure), Is.True, createFailure.Code.ToString());

            try
            {
                var semanticHash = new NativeHash256V1(new CompiledHash(StableHash.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("p3-011-window-probe"))));
                var enter = new NativeTraceRecordV1
                {
                    TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                    Phase = NativeUpdatePhaseV1.Execute,
                    UpdateId = 1,
                    SnapshotRevision = 1,
                    TreeSemanticHash = semanticHash,
                    TreeInstanceId = 1,
                    Sequence = 1,
                    WorkerOrdinal = 0,
                    Kind = NativeTraceEventKindV1.NodeEntered,
                    OptionalFields = NativeTraceOptionalFieldsV1.RuntimeNode,
                    RuntimeNodeIndex = rootRuntimeIndex,
                    DebugIdentityIndex = CompiledIndex.Invalid,
                    SourceNodeIndex = CompiledIndex.Invalid,
                };

                Assert.That(owner.TryAcquireWriter(out var lease, out var acquireFailure), Is.True, acquireFailure.Code.ToString());
                using (var recordArray = new NativeArray<NativeTraceRecordV1>(new[] { enter }, Allocator.TempJob))
                {
                    var job = new AppendOneJob { Writer = lease.Writer, Record = enter };
                    var handle = job.Schedule();
                    Assert.That(owner.TryRegisterDependency(lease, handle, out var dependencyFailure), Is.True, dependencyFailure.Code.ToString());
                    handle.Complete();
                }
                Assert.That(owner.TryReleaseWriter(lease, out var releaseFailure), Is.True, releaseFailure.Code.ToString());

                var session = new NativeExecutionDebuggerSession();
                session.Attach(owner);

                var window = ScriptableObject.CreateInstance<TraceTimelineWindow>();
                try
                {
                    window.AttachSession(session);
                    window.LoadGraphContext(document, registry, compilation.Program);

                    Assert.That(window.CurrentModel.Steps.Count, Is.EqualTo(1));
                    Assert.That(window.CurrentModel.HasDroppedEvents, Is.False);
                    Assert.That(window.CurrentStepIndex, Is.EqualTo(0));
                }
                finally
                {
                    Object.DestroyImmediate(window);
                }
            }
            finally
            {
                owner.TryDispose(out _);
            }
        }

        private static uint FindRuntimeIndexForRoot(CompiledProgram program, NodeId root)
        {
            foreach (var entry in program.DebugMap)
            {
                if (entry.AuthoringNodeId.Equals(root))
                {
                    return entry.RuntimeNodeIndex;
                }
            }

            throw new System.InvalidOperationException("Root node has no debug-map entry.");
        }

        [BurstCompile]
        private struct AppendOneJob : IJob
        {
            public NativeTraceWriterV1 Writer;
            public NativeTraceRecordV1 Record;

            public void Execute()
            {
                Writer.TryAppend(Record);
            }
        }
    }
}
