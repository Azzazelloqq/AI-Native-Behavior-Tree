using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReferenceExecutionSafetyTests
    {
        [Test]
        public void HandlerReentrancy_IsRejectedWithoutChangingOuterResult()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success) { ReenterOnTick = true };
            var machine = ReferenceExecutionTestProgram.Create(handler);
            handler.ReentrantMachine = machine;

            var outer = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(outer.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(handler.ReentrantResult.HasValue, Is.True);
            Assert.That(handler.ReentrantResult.Value.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(handler.ReentrantResult.Value.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidOperation));
        }

        [Test]
        public void InspectionFromCallback_IsRejectedWithoutChangingOuterResult()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success) { InspectOnTick = true };
            var machine = ReferenceExecutionTestProgram.Create(handler);
            handler.ReentrantMachine = machine;

            var outer = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(outer.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(handler.InspectionException, Is.TypeOf<System.InvalidOperationException>());
            Assert.That(machine.CaptureInspection().ActiveNodeCount, Is.Zero);
        }

        [Test]
        public void MachineInitialBlackboard_IsVisibleBeforeFirstUpdate_AndRestartRestoresCompiledDefault()
        {
            const ulong stableKeyId = 123;
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = CreateMachineWithInt32Blackboard(
                handler,
                stableKeyId,
                compiledDefault: 7,
                initialValue: 42);

            var initial = machine.CaptureInspection();
            Assert.That(initial.Blackboard.Revision, Is.Zero);
            Assert.That(initial.Blackboard.Entries, Has.Count.EqualTo(1));
            Assert.That(initial.Blackboard.Entries[0].StableKeyId, Is.EqualTo(stableKeyId));
            Assert.That(initial.Blackboard.Entries[0].Version, Is.Zero);
            Assert.That(initial.Blackboard.Entries[0].BuiltInValue.TryGetInt32(out var value), Is.True);
            Assert.That(value, Is.EqualTo(42));

            Assert.That(machine.Update(ReferenceExecutionTestProgram.Update(1)).RootResult,
                Is.EqualTo(NodeStatus.Success));
            Assert.That(machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));

            var restarted = machine.CaptureInspection();
            Assert.That(restarted.Blackboard.Revision, Is.EqualTo(1));
            Assert.That(restarted.Blackboard.Entries[0].Version, Is.EqualTo(1));
            Assert.That(restarted.Blackboard.Entries[0].BuiltInValue.TryGetInt32(out value), Is.True);
            Assert.That(value, Is.EqualTo(7));
        }

        [Test]
        public void HandlerException_FaultsAndClearsActivationMemoryWithoutFurtherCallbacks()
        {
            var trace = new RecordingReferenceTraceSink();
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success)
            {
                ThrowOn = "Tick",
                TickMemoryValue = 91,
            };
            var machine = ReferenceExecutionTestProgram.Create(handler, trace);

            var fault = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(fault.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(fault.RootResult, Is.Null);
            Assert.That(fault.Steps, Is.EqualTo(1));
            Assert.That(fault.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.HandlerFault));
            Assert.That(handler.Calls, Is.EqualTo(new[] { "Enter", "Tick" }));
            Assert.That(machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[] { 0 }));
            Assert.That(trace.Records.Select(item => item.Kind), Does.Contain(ReferenceTraceEventKind.DiagnosticRaised));
            Assert.That(machine.Update(ReferenceExecutionTestProgram.Update(2)).Progress,
                Is.EqualTo(ReferenceExecutionProgress.Rejected));

            handler.ThrowOn = null;
            Assert.That(machine.Restart().Progress, Is.EqualTo(ReferenceExecutionProgress.Completed));
            machine.Update(ReferenceExecutionTestProgram.Update(3));
            Assert.That(handler.MemoryAtEnter.Last(), Is.Zero);
        }

        [TestCase("Enter", 0)]
        [TestCase("Tick", 1)]
        [TestCase("Exit", 2)]
        public void TerminalLifecycleCallbackException_FaultsWithoutFurtherCallbacks(
            string callback,
            int expectedCompletedSteps)
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success) { ThrowOn = callback };
            var machine = ReferenceExecutionTestProgram.Create(handler);

            var result = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Steps, Is.EqualTo(expectedCompletedSteps));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.HandlerFault));
            Assert.That(handler.Calls.Last(), Does.StartWith(callback));
        }

        [TestCase("Abort", 0)]
        [TestCase("Exit", 1)]
        public void AbortedLifecycleCallbackException_FaultsWithoutFurtherCallbacks(
            string callback,
            int expectedCompletedSteps)
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running) { ThrowOn = callback };
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            var result = machine.Abort(
                ReferenceExecutionTestProgram.Update(2),
                NodeAbortReason.Explicit,
                new RuntimeNodeIndex(0));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Steps, Is.EqualTo(expectedCompletedSteps));
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.HandlerFault));
        }

        [Test]
        public void MissingHandler_FaultsBeforeEnter()
        {
            var trace = new RecordingReferenceTraceSink();
            var machine = ReferenceExecutionTestProgram.CreateWithoutHandler(trace);

            var result = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Faulted));
            Assert.That(result.Steps, Is.Zero);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(ReferenceExecutionDiagnosticCodes.MissingHandler));
            Assert.That(trace.Records.Any(item => item.Kind == ReferenceTraceEventKind.NodeEntered), Is.False);
        }

        [Test]
        public void LowerUpdateId_IsRejected()
        {
            var trace = new RecordingReferenceTraceSink();
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running);
            var machine = ReferenceExecutionTestProgram.Create(handler, trace);
            machine.Update(ReferenceExecutionTestProgram.Update(2));

            var result = machine.Update(ReferenceExecutionTestProgram.Update(1));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(result.Diagnostics.Single().Code,
                Is.EqualTo(ReferenceExecutionDiagnosticCodes.InvalidOperation));
            Assert.That(handler.Calls.Count(call => call == "Tick"), Is.EqualTo(1));
            var rejection = trace.Records.Last();
            Assert.That(rejection.Kind, Is.EqualTo(ReferenceTraceEventKind.DiagnosticRaised));
            Assert.That(rejection.UpdateId, Is.EqualTo(1));
            Assert.That(rejection.SnapshotRevision, Is.EqualTo(new Revision(101)));
        }

        [TestCase((NodeAbortReason)255, 0u)]
        [TestCase(NodeAbortReason.Explicit, RuntimeNodeIndex.InvalidValue)]
        [TestCase(NodeAbortReason.Explicit, 1u)]
        public void Abort_RejectsUnknownReasonAndInvalidOrForeignSource(
            NodeAbortReason reason,
            uint sourceValue)
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Running);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.Update(ReferenceExecutionTestProgram.Update(1));

            var result = machine.Abort(
                ReferenceExecutionTestProgram.Update(2),
                reason,
                new RuntimeNodeIndex(sourceValue));

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(handler.Calls.Count(call => call.StartsWith("Abort")), Is.Zero);
        }

        [TestCase((NodeAbortReason)255, 0u)]
        [TestCase(NodeAbortReason.Explicit, RuntimeNodeIndex.InvalidValue)]
        [TestCase(NodeAbortReason.Explicit, 1u)]
        public void RequestAbort_RejectsInvalidInputWithoutChangingOpenUpdate(
            NodeAbortReason reason,
            uint sourceValue)
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler);
            machine.BeginUpdate(ReferenceExecutionTestProgram.Update(1));
            machine.AdvanceOneStep();

            var rejected = machine.RequestAbort(reason, new RuntimeNodeIndex(sourceValue));

            Assert.That(rejected.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(machine.HasOpenUpdate, Is.True);
            ReferenceExecutionEnvelope step;
            do
            {
                step = machine.AdvanceOneStep();
            }
            while (step.Progress == ReferenceExecutionProgress.Suspended);

            Assert.That(step.RootResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(handler.Calls, Is.EqualTo(new[] { "Enter", "Tick", "Exit:Success" }));
        }

        [Test]
        public void RestartPreflightsGenerationOverflowWithoutMutatingTerminalState()
        {
            var handler = new ScriptedReferenceLeaf(NodeStatus.Success);
            var machine = ReferenceExecutionTestProgram.Create(handler, memoryLifetime: NodeMemoryLifetime.Instance);
            handler.ExitMemoryValue = 44;
            machine.Update(ReferenceExecutionTestProgram.Update(1));
            var field = typeof(ReferenceExecutionMachine).GetField(
                "_activationGenerations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var generations = (uint[])field.GetValue(machine);
            generations[0] = uint.MaxValue;

            var result = machine.Restart();

            Assert.That(result.Progress, Is.EqualTo(ReferenceExecutionProgress.Rejected));
            Assert.That(machine.TerminalResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(machine.CopyNodeMemory(new RuntimeNodeIndex(0)), Is.EqualTo(new byte[] { 44 }));
        }

        private static ReferenceExecutionMachine CreateMachineWithInt32Blackboard(
            IReferenceLeafHandler handler,
            ulong stableKeyId,
            int compiledDefault,
            int initialValue)
        {
            const ulong nodeTypeId = 17;
            const uint nodeTypeVersion = 1;
            var hash = new CompiledHash(new string('e', CompiledHash.HexLength));
            var descriptor = BuiltInBlackboardTypes.Int32;
            var node = new CompiledNodeRecord(
                nodeTypeId,
                nodeTypeVersion,
                0,
                0,
                1,
                0,
                1,
                1,
                NodeMemoryLifetime.Activation,
                new CompiledRange(0, 0),
                CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                CompiledIndex.Invalid,
                new CompiledRange(0, 0),
                new CompiledRange(0, 0));
            var slot = new CompiledBlackboardSlotRecord(
                stableKeyId,
                descriptor.TypeId,
                descriptor.Version,
                0,
                BlackboardScope.Tree,
                0,
                (uint)descriptor.Size,
                (uint)descriptor.Alignment,
                0,
                CompiledBlackboardAccessFlags.None);
            var header = new CompiledProgramHeader(
                1,
                1,
                new CompiledCompilerVersion(1, 0, 0, 0),
                hash,
                hash,
                hash,
                1,
                hash,
                0,
                1,
                0,
                1,
                0,
                0,
                4,
                1,
                0,
                true);
            var defaultBytes = new byte[]
            {
                (byte)compiledDefault,
                (byte)(compiledDefault >> 8),
                (byte)(compiledDefault >> 16),
                (byte)(compiledDefault >> 24),
            };
            var program = new CompiledProgram(
                header,
                new[] { node },
                System.Array.Empty<uint>(),
                System.Array.Empty<uint>(),
                System.Array.Empty<uint>(),
                new[] { slot },
                System.Array.Empty<CompiledObserverRecord>(),
                System.Array.Empty<uint>(),
                System.Array.Empty<byte>(),
                defaultBytes,
                System.Array.Empty<CompiledDebugMapEntry>());
            return new ReferenceExecutionMachine(
                program,
                new TreeInstanceId(41),
                new ReferenceLeafRegistry(new[]
                {
                    new ReferenceLeafBinding(nodeTypeId, nodeTypeVersion, handler),
                }),
                initialBlackboard: new[]
                {
                    ReferenceBlackboardInitialValue.BuiltIn(
                        stableKeyId,
                        BlackboardValue.FromInt32(initialValue)),
                });
        }
    }
}
