using System.Reflection;
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
                Object.DestroyImmediate(_gameObject);
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

        private ProductionTreeHost CreateHost()
        {
            _gameObject = new GameObject("AIBT.Tests.ProductionTreeHost");
            return _gameObject.AddComponent<ProductionTreeHost>();
        }

        private static void InvokeUpdate(ProductionTreeHost host)
        {
            var method = typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(host, null);
        }

        private static class Fixture
        {
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
