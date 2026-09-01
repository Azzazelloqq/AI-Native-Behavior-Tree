using System;
using AIBT.Burst;
using AIBT.Tests.Runtime;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.Stress
{
    /// <summary>
    /// P7-004's hot-reload-under-load deliverable: reload fired repeatedly against a live
    /// population, not just the single-reload cases `P5-004`/`P5-005`/`P5-006` (reference executor)
    /// and `P7-012` (native backend) already cover in their own evidence. Both populations use a
    /// fixed, seeded partition (never `UnityEngine.Random`/wall-clock timing) into a "never
    /// reloaded" group -- compared against an all-untouched control for exact isolation -- and a
    /// "repeatedly reloaded" group -- full-restarted every wave, proving the mechanism itself does
    /// not accumulate corruption across many cycles.
    /// </summary>
    public sealed class HotReloadUnderLoadStressTests
    {
        private const int NeverReloadedCount = 20;
        private const int RepeatedlyReloadedCount = 20;
        private const int Waves = 10;

        [Test]
        public void ReferenceExecutor_RepeatedReloadWaves_LeaveNeverReloadedInstancesIdenticalToAnUntouchedControl()
        {
            var program = BuildReferenceProgram();
            var control = new ReferenceInstance[NeverReloadedCount + RepeatedlyReloadedCount];
            var experiment = new ReferenceInstance[NeverReloadedCount + RepeatedlyReloadedCount];
            for (var index = 0; index < control.Length; index++)
            {
                control[index] = ReferenceInstance.Create(program);
                experiment[index] = ReferenceInstance.Create(program);
            }

            var updateId = 1ul;
            for (var wave = 0; wave < Waves; wave++)
            {
                for (var index = 0; index < control.Length; index++)
                {
                    control[index].Tick(updateId);
                    experiment[index].Tick(updateId);
                }

                // Indices [0, NeverReloadedCount) are never touched; [NeverReloadedCount, end) are
                // full-restarted every single wave -- a fixed, deterministic partition, not random.
                for (var index = NeverReloadedCount; index < experiment.Length; index++)
                {
                    experiment[index] = experiment[index].Restart(program, updateId + 1);
                }

                updateId += 2;
            }

            for (var index = 0; index < NeverReloadedCount; index++)
            {
                Assert.That(experiment[index].TickCount, Is.EqualTo(control[index].TickCount), "never-reloaded instance " + index);
                Assert.That(experiment[index].IsHealthy(updateId), Is.True, "never-reloaded instance " + index);
            }

            for (var index = NeverReloadedCount; index < experiment.Length; index++)
            {
                Assert.That(experiment[index].IsHealthy(updateId), Is.True,
                    "repeatedly-reloaded instance " + index + " must still be healthy after " + Waves + " reload cycles.");
            }
        }

        [Test]
        public void NativeBackend_RepeatedReloadWaves_LeaveNeverReloadedLanesIdenticalToAnUntouchedControlBatch()
        {
            var program = BuildNativeProgram();
            var control = new NativeHotReloadInstance[NeverReloadedCount + RepeatedlyReloadedCount];
            var experiment = new NativeHotReloadInstance[NeverReloadedCount + RepeatedlyReloadedCount];
            try
            {
                for (var index = 0; index < control.Length; index++)
                {
                    control[index] = BuildFreshNativeInstance(program);
                    experiment[index] = BuildFreshNativeInstance(program);
                }

                var updateId = 1ul;
                for (var wave = 0; wave < Waves; wave++)
                {
                    for (var index = 0; index < control.Length; index++)
                    {
                        Assert.That(TickOnceRunning(ref control[index].Machine, updateId), Is.True, "control " + index);
                        Assert.That(TickOnceRunning(ref experiment[index].Machine, updateId), Is.True, "experiment " + index);
                    }

                    for (var index = NeverReloadedCount; index < experiment.Length; index++)
                    {
                        Assert.That(
                            NativeHotReloadFullRestart.TryRestart(experiment[index], program, updateId + 1, Allocator.Persistent,
                                out var reloaded, out _, out var restartFailure),
                            Is.True, "wave " + wave + " lane " + index + ": " + restartFailure.Code);
                        // Deliberately no TryBeginUpdate here -- the fresh instance starts with
                        // Depth=0/UpdateOpen=0, and the NEXT wave's own TickOnceRunning call opens
                        // its first update naturally. Calling TryBeginUpdate here too would leave
                        // that update open-but-never-advanced, making the next wave's own
                        // TryBeginUpdate call fail (UpdateOpen != 0).
                        experiment[index] = reloaded;
                    }

                    updateId += 2;
                }

                for (var index = 0; index < NeverReloadedCount; index++)
                {
                    Assert.That(
                        experiment[index].Control[0].Depth, Is.EqualTo(control[index].Control[0].Depth),
                        "never-reloaded lane " + index + " must match the untouched control's own active depth.");
                }

                for (var index = NeverReloadedCount; index < experiment.Length; index++)
                {
                    Assert.That(TickOnceRunning(ref experiment[index].Machine, updateId), Is.True,
                        "repeatedly-reloaded lane " + index + " must still be healthy after " + Waves + " reload cycles.");
                }
            }
            finally
            {
                foreach (var instance in control) instance.Dispose();
                foreach (var instance in experiment) instance.Dispose();
            }
        }

        // Deliberately does NOT call TryBeginUpdate -- the wave loop's own first TickOnceRunning
        // call does that at updateId 1; calling it here too would make that later call fail
        // (updateId must strictly exceed the instance's own last-used one).
        private static NativeHotReloadInstance BuildFreshNativeInstance(CompiledProgram program)
        {
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var instance, out var buildFailure),
                Is.True, buildFailure.Code.ToString());
            return instance;
        }

        // Drives exactly one atomic-step round for a leaf that always returns Running -- the
        // machine reaches Waiting (still active) after Enter+Tick, matching the always-live
        // population this stress test needs to reload against repeatedly.
        private static bool TickOnceRunning(ref NativeLifecycleMachineV1 machine, ulong updateId)
        {
            if (!machine.TryBeginUpdate(updateId, out _)) return false;
            for (var guard = 0; guard < 8; guard++)
            {
                if (!machine.TryAdvance(out var step, out _)) return false;
                if (step.Kind == NativeLifecycleStepKindV1.DispatchRequired)
                {
                    var status = step.Phase == BurstCallbackPhase.Tick ? NodeStatus.Running : NodeStatus.Running;
                    if (!machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, status, out _)) return false;
                    continue;
                }
                if (step.Kind == NativeLifecycleStepKindV1.Waiting) return true;
            }
            return false;
        }

        private static CompiledProgram BuildReferenceProgram()
        {
            var nodes = new[]
            {
                new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing, 0, default, default),
                new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.tests.p7004.always-running"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                    default, CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing, 1, default, default),
            };
            return BuildProgram(nodes);
        }

        private static CompiledProgram BuildNativeProgram()
        {
            var nodes = new[]
            {
                new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.core.memory-sequence"), 1, 0, 0, 1, 0, 4, 4, NodeMemoryLifetime.Activation,
                    new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain, 0, default, default),
                new CompiledNodeRecord(
                    StableHash.Fnv1A64("aibt.tests.p7004.always-running"), 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                    default, CompiledNodeFlags.BurstDomain, 1, default, default),
            };
            return BuildProgram(nodes);
        }

        private static CompiledProgram BuildProgram(CompiledNodeRecord[] nodes)
        {
            var children = new uint[] { 1 };
            var debug = new[]
            {
                new CompiledDebugMapEntry(0, new NodeId("root"), "test/root"),
                new CompiledDebugMapEntry(1, new NodeId("leaf"), "test/leaf"),
            };
            var preliminary = Build(new CompiledHash(new string('d', 64)), nodes, children, debug);
            var contentHash = CompiledProgramContentHashV1.Compute(preliminary);
            return Build(contentHash, nodes, children, debug);
        }

        private static CompiledProgram Build(CompiledHash contentHash, CompiledNodeRecord[] nodes, uint[] children, CompiledDebugMapEntry[] debug)
        {
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                new CompiledHash(new string('a', 64)), new CompiledHash(new string('b', 64)), new CompiledHash(new string('c', 64)),
                1, contentHash,
                0, (uint)nodes.Length, (uint)children.Length, 0, (uint)debug.Length,
                0, 4, 4, 0, true);
            return new CompiledProgram(
                header, nodes, children, Array.Empty<uint>(), Array.Empty<uint>(),
                Array.Empty<CompiledBlackboardSlotRecord>(), Array.Empty<CompiledObserverRecord>(),
                Array.Empty<uint>(), Array.Empty<byte>(), Array.Empty<byte>(), debug);
        }

        /// <summary>One reference-executor instance's own machine + dedicated leaf handler (each
        /// instance needs its own <see cref="ScriptedReferenceLeaf"/> -- sharing one across many
        /// machines would make their own call logs interleave, corrupting this test's own isolation
        /// proof, not the production code under test).</summary>
        private sealed class ReferenceInstance
        {
            private ReferenceExecutionMachine _machine;
            private ScriptedReferenceLeaf _leaf;

            internal int TickCount => _leaf.Calls.Count;

            internal static ReferenceInstance Create(CompiledProgram program)
            {
                var leaf = new ScriptedReferenceLeaf(); // empty status queue: Tick always returns Running.
                var registry = new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(StableHash.Fnv1A64("aibt.tests.p7004.always-running"), 1, leaf),
                });
                var machine = new ReferenceExecutionMachine(
                    program, new TreeInstanceId(1), registry, null,
                    ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                    ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                    ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                    ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                    RegisteredBlackboardRegistry.Empty,
                    ReferenceObserverConditionRegistry.Empty);
                return new ReferenceInstance { _machine = machine, _leaf = leaf };
            }

            internal void Tick(ulong updateId)
            {
                var envelope = _machine.Update(new ReferenceUpdateContext(updateId, new Revision(updateId), 0));
                Assert.That(envelope.Progress, Is.EqualTo(ReferenceExecutionProgress.Waiting),
                    "an always-Running leaf must leave every tick suspended, never terminal.");
            }

            internal ReferenceInstance Restart(CompiledProgram program, ulong abortUpdateId)
            {
                var freshLeaf = new ScriptedReferenceLeaf();
                var freshRegistry = new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(StableHash.Fnv1A64("aibt.tests.p7004.always-running"), 1, freshLeaf),
                });
                var freshMachine = HotReloadFullRestart.Restart(
                    _machine, program, new ReferenceUpdateContext(abortUpdateId, new Revision(abortUpdateId), 0),
                    new TreeInstanceId(1), freshRegistry, null,
                    ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns(),
                    ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns(),
                    ReferenceDecoratorRegistry.CreatePhase1BuiltIns(),
                    ReferenceParallelRegistry.CreatePhase1BuiltIns(),
                    RegisteredBlackboardRegistry.Empty,
                    ReferenceObserverConditionRegistry.Empty,
                    out var report);
                Assert.That(report.OldInstanceWasAborted, Is.True);
                return new ReferenceInstance { _machine = freshMachine, _leaf = freshLeaf };
            }

            internal bool IsHealthy(ulong updateId)
            {
                var envelope = _machine.Update(new ReferenceUpdateContext(updateId, new Revision(updateId), 0));
                return envelope.Progress == ReferenceExecutionProgress.Waiting;
            }
        }
    }
}
