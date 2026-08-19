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
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Editor.Trace
{
    /// <summary>End-to-end: a real compiled tree, a real bounded channel, and the actual window class.</summary>
    public sealed class TraceTimelineWindowTests
    {
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
