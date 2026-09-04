using System.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Tests.Runtime.Integration
{
    /// <summary>
    /// P7-027: proves <see cref="ProductionTreeHost"/> drives a real compiled program's lifecycle
    /// via real, on-demand dispatch (not a pre-supplied array, unlike
    /// <c>SchedulingPolicyDriver</c>'s own benchmark-only shape) and tears down leak-free. Reuses
    /// <c>Tests/Runtime/Benchmarking/SchedulingPolicyDriverTests</c>'s own already-proven minimal
    /// single-generated-leaf program shape (a real <see cref="NativeLifecycleNodeKindV1.GeneratedLeaf"/>
    /// under one <c>aibt.core.memory-sequence</c>), duplicated locally per this codebase's own
    /// established small-fixture-duplication precedent (<c>P6-014</c>'s own item-1 precedent).
    /// <see cref="ProductionTreeHost.Update"/> is invoked via reflection since a plain EditMode test
    /// has no real Player loop calling it -- the real, live Play-mode proof (host actually ticking
    /// every frame, debugger attached) is this card's own required live verification, not this test.
    /// </summary>
    public sealed class ProductionTreeHostTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void DestroyGameObject()
        {
            if (_gameObject != null)
            {
                var host = _gameObject.GetComponent<ProductionTreeHost>();
                if (host != null)
                    typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        [Test]
        public void TryBootstrap_RealProgram_SucceedsAndOwnsATraceChannel()
        {
            var host = CreateHost();
            var program = Fixture.CreateSingleLeafProgram();

            var succeeded = host.TryBootstrap(program, _ => NodeStatus.Success, Fixture.TraceCapacity, out var failure);

            Assert.That(succeeded, Is.True, failure.Code.ToString());
            Assert.That(host.TraceChannelOwner, Is.Not.Null);
            Assert.That(host.TraceChannelOwner.State, Is.Not.EqualTo(NativeOwnerStateV1.Disposed));
        }

        [Test]
        public void Update_RealDispatchDelegate_DrivesTheLeafOnDemandNotFromAPreSuppliedArray()
        {
            var host = CreateHost();
            var program = Fixture.CreateSingleLeafProgram();
            var dispatchedNodeIndices = new System.Collections.Generic.List<uint>();

            Assert.That(host.TryBootstrap(program, nodeIndex =>
            {
                dispatchedNodeIndices.Add(nodeIndex);
                return NodeStatus.Success;
            }, Fixture.TraceCapacity, out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());

            InvokeUpdate(host);

            Assert.That(dispatchedNodeIndices, Has.Count.EqualTo(1), "The single generated-leaf child must be dispatched exactly once per tick.");
            Assert.That(dispatchedNodeIndices[0], Is.EqualTo(1u), "Node index 1 is the leaf per Fixture.CreateSingleLeafProgram's own debug map.");
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL));
        }

        [Test]
        public void Update_RunningThenSuccess_MatchesSchedulingPolicyDriverTests_OwnAlreadyProvenTwoTickPattern()
        {
            var host = CreateHost();
            var program = Fixture.CreateSingleLeafProgram();
            var statuses = new System.Collections.Generic.Queue<NodeStatus>(new[] { NodeStatus.Running, NodeStatus.Success });

            Assert.That(host.TryBootstrap(program, _ => statuses.Dequeue(), Fixture.TraceCapacity, out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());

            InvokeUpdate(host);
            Assert.That(host.LastRootResult, Is.Null.Or.Not.EqualTo(NodeStatus.Success), "Still Running/Waiting after the first tick.");

            InvokeUpdate(host);
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(host.TotalUpdates, Is.EqualTo(2uL));
        }

        [Test]
        public void OnDestroy_DisposesTheTraceChannelAndTheAgentLeakFree()
        {
            var host = CreateHost();
            var program = Fixture.CreateSingleLeafProgram();
            Assert.That(host.TryBootstrap(program, _ => NodeStatus.Success, Fixture.TraceCapacity, out var bootstrapFailure), Is.True, bootstrapFailure.Code.ToString());
            InvokeUpdate(host);
            var owner = host.TraceChannelOwner;

            // Invoked directly via reflection rather than relying on Object.DestroyImmediate to
            // cascade OnDestroy synchronously in this EditMode context (not guaranteed timing) --
            // same rationale as InvokeUpdate for Update() above.
            var method = typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(host, null);

            Assert.That(owner.State, Is.EqualTo(NativeOwnerStateV1.Disposed));
        }

        [TestCase(NodeStatus.Success)]
        [TestCase(NodeStatus.Failure)]
        public void TerminalRoot_RemainsObservableWithoutExecutingMoreFrames(NodeStatus terminal)
        {
            var host = CreateHost();
            var ticks = 0;
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), _ =>
            {
                ticks++;
                return terminal;
            }, Fixture.TraceCapacity, out _), Is.True);

            InvokeUpdate(host);
            InvokeUpdate(host);
            InvokeUpdate(host);

            Assert.That(host.LastRootResult, Is.EqualTo(terminal));
            Assert.That(ticks, Is.EqualTo(1));
            Assert.That(host.TotalUpdates, Is.EqualTo(1UL));
        }

        private ProductionTreeHost CreateHost()
        {
            _gameObject = new GameObject("AIBT.Tests.ProductionTreeHost");
            return _gameObject.AddComponent<ProductionTreeHost>();
        }

        [TestCase(NodeStatus.Success)]
        [TestCase(NodeStatus.Failure)]
        public void Lifecycle_InitializesBeforeTickAndExitsWithActualReason(NodeStatus terminal)
        {
            var host = CreateHost();
            var calls = new List<BurstCallbackPhase>();
            var initialized = false;
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls.Add(request.Phase);
                if (request.Phase == BurstCallbackPhase.Enter) initialized = true;
                if (request.Phase == BurstCallbackPhase.Tick) Assert.That(initialized, Is.True);
                if (request.Phase == BurstCallbackPhase.Exit)
                {
                    Assert.That(request.ExitReason, Is.EqualTo(terminal == NodeStatus.Success ? BurstNodeExitReason.Success : BurstNodeExitReason.Failure));
                    initialized = false;
                }
                status = terminal;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => 10, out _), Is.True);
            InvokeUpdate(host);
            Assert.That(calls, Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick, BurstCallbackPhase.Exit }));
            Assert.That(initialized, Is.False);
            Assert.That(host.LastRootResult, Is.EqualTo(terminal));
            Assert.That(host.TraceChannelOwner.TryGetSnapshot(out var snapshot, out _), Is.True);
            Assert.That(Enumerable.Range(0, (int)snapshot.RecordCount).Select(i => snapshot.Records[i])
                .Single(r => r.Kind == NativeTraceEventKindV1.NodeExited && r.RuntimeNodeIndex == 1).ExitReason,
                Is.EqualTo(terminal == NodeStatus.Success ? NativeTraceNodeExitReasonV1.Success : NativeTraceNodeExitReasonV1.Failure));
        }

        [Test]
        public void Timeout_UsesClockAndCancelsAtDeadlineBeforeRetick()
        {
            var host = CreateHost();
            long now = 10;
            var calls = new List<ProductionTreeHost.DispatchRequest>();
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls.Add(request);
                status = NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.Timeout(), Dispatch, Fixture.TraceCapacity, () => now, out _), Is.True);
            InvokeUpdate(host);
            now = 109;
            InvokeUpdate(host);
            Assert.That(host.LastRootResult, Is.Null);
            now = 110;
            InvokeUpdate(host);
            Assert.That(calls.Select(c => c.Phase), Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick, BurstCallbackPhase.Tick, BurstCallbackPhase.Abort, BurstCallbackPhase.Exit }));
            Assert.That(calls[3].AbortReason, Is.EqualTo(BurstNodeAbortReason.Timeout));
            Assert.That(calls[4].ExitReason, Is.EqualTo(BurstNodeExitReason.Aborted));
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Failure));
        }

        [TestCase(20, 1)]
        [TestCase(110, 2)]
        public void Cooldown_RevisitedWithinLiveTreeHonorsDeadline(long revisitTime, int expectedEntries)
        {
            var host = CreateHost();
            long now = 10;
            var entries = 0;
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                if (request.NodeIndex == 3 && request.Phase == BurstCallbackPhase.Enter) entries++;
                status = request.NodeIndex == 3 ? NodeStatus.Success : NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.ReactiveCooldown(), Dispatch, Fixture.TraceCapacity, () => now, out _), Is.True);
            InvokeUpdate(host);
            now = revisitTime;
            InvokeUpdate(host);
            Assert.That(entries, Is.EqualTo(expectedEntries));
            Assert.That(host.LastRootResult, revisitTime < 110 ? Is.EqualTo(NodeStatus.Failure) : Is.Null);
        }

        [Test]
        public void Budget_ZeroAndSingleStepsPreserveUpdateTimeAndCallbackOrder()
        {
            var host = CreateHost();
            var calls = new List<ProductionTreeHost.DispatchRequest>();
            long now = 10;
            var clockReads = 0;
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls.Add(request);
                status = NodeStatus.Success;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => { clockReads++; return now; }, out _), Is.True);
            host.StepBudget = 0;
            InvokeUpdate(host);
            Assert.That(calls, Is.Empty);
            host.StepBudget = 1;
            for (var frame = 0; frame < 24 && !host.LastRootResult.HasValue; frame++)
            {
                now++;
                InvokeUpdate(host);
                Assert.That(host.TraceChannelOwner.TryGetSnapshot(out _, out _), Is.True, "A yielded segment must release its writer lease.");
            }
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(calls.Select(c => c.Phase), Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick, BurstCallbackPhase.Exit }));
            Assert.That(calls.All(c => c.UpdateId == 1 && c.TimeMicroseconds == 10), Is.True);
            Assert.That(clockReads, Is.EqualTo(1));
            Assert.That(host.TraceChannelOwner.TryGetSnapshot(out var snapshot, out _), Is.True);
            var events = Enumerable.Range(0, (int)snapshot.RecordCount).Select(i => snapshot.Records[i].Kind).ToArray();
            Assert.That(events.Count(e => e == NativeTraceEventKindV1.UpdateStarted), Is.EqualTo(1));
            Assert.That(events.Count(e => e == NativeTraceEventKindV1.UpdateCompleted), Is.EqualTo(1));
            Assert.That(events.Count(e => e == NativeTraceEventKindV1.BudgetYielded), Is.GreaterThan(0));
            Assert.That(events.Count(e => e == NativeTraceEventKindV1.ExecutionResumed), Is.EqualTo(events.Count(e => e == NativeTraceEventKindV1.BudgetYielded)));
        }

        [Test]
        public void DisablePauses_DestroyCancelsAndDisposesExactlyOnce()
        {
            var host = CreateHost();
            var calls = new List<ProductionTreeHost.DispatchRequest>();
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls.Add(request);
                status = NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => 10, out _), Is.True);
            InvokeUpdate(host);
            host.enabled = false;
            InvokeUpdate(host);
            Assert.That(calls.Count, Is.EqualTo(2));
            host.enabled = true;
            InvokeUpdate(host);
            Assert.That(calls.Count, Is.EqualTo(3));
            host.StepBudget = 0;
            typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            Assert.That(calls.Select(c => c.Phase), Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick, BurstCallbackPhase.Tick, BurstCallbackPhase.Abort, BurstCallbackPhase.Exit }));
            Assert.That(calls[3].AbortReason, Is.EqualTo(BurstNodeAbortReason.TreeStopped));
            Assert.That(calls[4].ExitReason, Is.EqualTo(BurstNodeExitReason.Aborted));
            Assert.That(host.TraceChannelOwner.State, Is.EqualTo(NativeOwnerStateV1.Disposed));
        }

        [Test]
        public void CallbackException_StopsOnceAndDisposesWithoutReinvokingUserCode()
        {
            var host = CreateHost();
            var calls = 0;
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls++;
                throw new InvalidOperationException("host-test-failure");
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => 10, out _), Is.True);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AIBT ProductionTreeHost:.*host-test-failure"));
            InvokeUpdate(host);
            InvokeUpdate(host);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(host.LastFailure.Code, Is.Not.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
        }

        private static void InvokeUpdate(ProductionTreeHost host)
        {
            var method = typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(host, null);
        }

        [TestCase(-1)]
        [TestCase(9)]
        public void InvalidClock_StopsBeforeAnotherCallback(long invalidTime)
        {
            var host = CreateHost();
            long now = 10;
            var calls = 0;
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls++;
                status = NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => now, out _), Is.True);
            InvokeUpdate(host);
            now = invalidTime;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AIBT ProductionTreeHost:.*Clock"));
            InvokeUpdate(host);
            InvokeUpdate(host);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
        }

        [Test]
        public void BootstrapTwice_RejectsWithoutReplacingLiveInstance()
        {
            var host = CreateHost();
            var ticks = 0;
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), _ => { ticks++; return NodeStatus.Running; }, Fixture.TraceCapacity, out _), Is.True);
            var owner = host.TraceChannelOwner;
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), _ => NodeStatus.Success, Fixture.TraceCapacity, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
            InvokeUpdate(host);
            Assert.That(ticks, Is.EqualTo(1));
            Assert.That(host.TraceChannelOwner, Is.SameAs(owner));
        }

        [Test]
        public void DestroyAfterTerminalTick_PreservesPendingExitReason()
        {
            var host = CreateHost();
            var calls = new List<ProductionTreeHost.DispatchRequest>();
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls.Add(request);
                status = NodeStatus.Success;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => 10, out _), Is.True);
            host.StepBudget = 1;
            for (var i = 0; i < 20 && !calls.Any(c => c.Phase == BurstCallbackPhase.Tick); i++) InvokeUpdate(host);
            Assert.That(calls.Select(c => c.Phase), Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick }));
            // EditMode does not guarantee OnDestroy for a component that never received Awake.
            // Invoke the callback boundary here; real Unity teardown is separately verified in Play mode.
            typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            Assert.That(calls.Select(c => c.Phase), Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick, BurstCallbackPhase.Exit }));
            Assert.That(calls[2].ExitReason, Is.EqualTo(BurstNodeExitReason.Success));
        }

        [Test]
        public void RootLeaf_RecordsOneExit()
        {
            var host = CreateHost();
            Assert.That(host.TryBootstrap(Fixture.RootLeaf(), _ => NodeStatus.Success, Fixture.TraceCapacity, out _), Is.True);
            InvokeUpdate(host);
            Assert.That(host.TraceChannelOwner.TryGetSnapshot(out var snapshot, out _), Is.True);
            Assert.That(Enumerable.Range(0, (int)snapshot.RecordCount).Count(i => snapshot.Records[i].Kind == NativeTraceEventKindV1.NodeExited), Is.EqualTo(1));
        }

        [Test]
        public void DestructionInsideCallback_CompletesCallbackBeforeDisposingStorage()
        {
            var host = CreateHost();
            var calls = new List<BurstCallbackPhase>();
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                calls.Add(request.Phase);
                if (request.Phase == BurstCallbackPhase.Tick)
                    typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
                status = NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(Fixture.CreateSingleLeafProgram(), Dispatch, Fixture.TraceCapacity, () => 10, out _), Is.True);
            InvokeUpdate(host);
            Assert.That(calls, Is.EqualTo(new[] { BurstCallbackPhase.Enter, BurstCallbackPhase.Tick, BurstCallbackPhase.Abort, BurstCallbackPhase.Exit }));
            Assert.That(host.TraceChannelOwner.State, Is.EqualTo(NativeOwnerStateV1.Disposed));
        }

        private static class Fixture
        {
            internal static CompiledProgram RootLeaf() => CreateConfigured(
                new[] { Node("test.leaf", 0, 0, 0, 0, 0, 0, 0) }, Array.Empty<uint>(), Array.Empty<byte>(), 0);
            internal static CompiledProgram Timeout() => CreateConfigured(
                new[] { Node("aibt.core.timeout", 0, 16, 0, 8, 0, 1, 0), Node("test.leaf", 0, 0, 0, 0, 0, 0, 1) },
                new uint[] { 1 }, new byte[] { 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 8);

            internal static CompiledProgram ReactiveCooldown() => CreateConfigured(
                new[] { Node("aibt.core.reactive-sequence", 0, 0, 0, 4, 0, 2, 0),
                    Node("aibt.core.cooldown", 0, 16, 8, 8, 2, 1, 1, NodeMemoryLifetime.Instance),
                    Node("test.gate", 0, 0, 0, 0, 0, 0, 2), Node("test.action", 0, 0, 0, 0, 0, 0, 3) },
                new uint[] { 1, 2, 3 }, new byte[] { 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 16);

            private static CompiledNodeRecord Node(string type, uint configOffset, uint configSize, uint memoryOffset, uint memorySize,
                uint childOffset, uint childCount, uint index, NodeMemoryLifetime lifetime = NodeMemoryLifetime.Activation)
                => new CompiledNodeRecord(StableHash.Fnv1A64(type), 1, configOffset, configSize, configSize == 0 ? 1u : 8u,
                    memoryOffset, memorySize, memorySize == 0 ? 1u : memorySize, lifetime,
                    new CompiledRange(childOffset, childCount), CompiledNodeFlags.BurstDomain, index, default, default);

            private static CompiledProgram CreateConfigured(CompiledNodeRecord[] nodes, uint[] children, byte[] config, uint memorySize)
            {
                var debug = nodes.Select((n, i) => new CompiledDebugMapEntry((uint)i, new NodeId("node" + i), "/tree/nodes/" + i)).ToArray();
                CompiledProgram Build(CompiledHash hash) => new CompiledProgram(
                    new CompiledProgramHeader(1, 1, new CompiledCompilerVersion(1, 0, 0, 1), Hash('a'), Hash('b'), Hash('c'), 1, hash,
                        0, (uint)nodes.Length, (uint)children.Length, 0, (uint)debug.Length, (uint)config.Length, memorySize,
                        nodes.Max(n => Math.Max(n.ConfigAlignment, n.InstanceMemoryAlignment)), 1, true),
                    nodes, children, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<CompiledBlackboardSlotRecord>(),
                    Array.Empty<CompiledObserverRecord>(), Array.Empty<uint>(), config, Array.Empty<byte>(), debug);
                return Build(CompiledProgramContentHashV1.Compute(Build(Hash('d'))));
            }

            internal static NativeTraceChannelCapacityV1 TraceCapacity =>
                new NativeTraceChannelCapacityV1(recordCapacity: 65, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 256);

            /// <summary>A two-node program: root memory-sequence with one generated-leaf child. No blackboard, no config. Mirrors SchedulingPolicyDriverTests's own already-proven fixture shape exactly.</summary>
            internal static CompiledProgram CreateSingleLeafProgram()
            {
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain, 0, default, default),
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("test.leaf"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        default, CompiledNodeFlags.BurstDomain, 1, default, default),
                };
                var children = new uint[] { 1 };
                var debug = new[]
                {
                    new CompiledDebugMapEntry(0, new NodeId("root"), "/tree/root"),
                    new CompiledDebugMapEntry(1, new NodeId("leaf"), "/tree/leaf"),
                };

                var preliminary = BuildProgram(Hash('d'), nodes, children, debug);
                var contentHash = CompiledProgramContentHashV1.Compute(preliminary);
                return BuildProgram(contentHash, nodes, children, debug);
            }

            private static CompiledProgram BuildProgram(
                CompiledHash contentHash,
                CompiledNodeRecord[] nodes,
                uint[] children,
                CompiledDebugMapEntry[] debug)
            {
                var header = new CompiledProgramHeader(
                    1, 1, new CompiledCompilerVersion(1, 0, 0, 1),
                    Hash('a'), Hash('b'), Hash('c'), 1, contentHash,
                    0, (uint)nodes.Length, (uint)children.Length, 0, (uint)debug.Length,
                    0, 4, 4, 1, true);
                return new CompiledProgram(
                    header, nodes, children, System.Array.Empty<uint>(), System.Array.Empty<uint>(),
                    System.Array.Empty<CompiledBlackboardSlotRecord>(), System.Array.Empty<CompiledObserverRecord>(),
                    System.Array.Empty<uint>(), System.Array.Empty<byte>(), System.Array.Empty<byte>(), debug);
            }

            private static CompiledHash Hash(char value) => new CompiledHash(new string(value, 64));
        }
    }
}
