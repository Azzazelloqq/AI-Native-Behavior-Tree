using System;
using System.Linq;
using AIBT.Burst;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    public sealed class NativeHotReloadBlackboardTests
    {
        [Test]
        public void FreshInstance_ReadsDeclaredNonzeroDefault()
        {
            var program = Program(Slot("value", 0), defaults: new byte[] { 17, 0, 0, 0 });
            var instance = Build(program);
            try { Assert.That(Read(instance, 0), Is.EqualTo(17)); }
            finally { instance.Dispose(); }
        }

        [Test]
        public void RunningNode_WritesOnce_ThenReadsItsValueAfterMigration()
        {
            var program = Program(Slot("value", 0), defaults: new byte[] { 17, 0, 0, 0 });
            var next = Program(Slot("value", 0), defaults: new byte[] { 23, 0, 0, 0 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Tick(ref old, 1, () => Write(old, 0, 91));
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next,
                    HotReloadCompatibilityClassifier.Classify(program, next), Array.Empty<NodeId>(),
                    Allocator.Persistent, out fresh, out _, out var failure), Is.True, failure.Code.ToString());
                var observed = -1;
                Tick(ref fresh, 2, () => observed = Read(fresh, 0));
                Assert.That(observed, Is.EqualTo(91));
                Assert.That(Read(old, 0), Is.EqualTo(91));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void ReorderedSlots_MigrateByStableKey_AndNewKeyUsesDefault()
        {
            var program = Program(new[] { Slot("a", 0), Slot("b", 4) }, new byte[] { 11, 0, 0, 0, 12, 0, 0, 0 });
            var next = Program(new[] { Slot("b", 8, 0), Slot("new", 0, 4), Slot("a", 4, 8) },
                new byte[] { 21, 0, 0, 0, 22, 0, 0, 0, 23, 0, 0, 0 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Write(old, 0, 71); Write(old, 4, 72);
                fresh = Migrate(old, program, next);
                Assert.That(Read(fresh, 8), Is.EqualTo(72));
                Assert.That(Read(fresh, 4), Is.EqualTo(71));
                Assert.That(Read(fresh, 0), Is.EqualTo(22));
                Assert.That(Read(old, 0), Is.EqualTo(71));
                Assert.That(Read(old, 4), Is.EqualTo(72));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [TestCase("type")]
        [TestCase("version")]
        [TestCase("size")]
        [TestCase("alignment")]
        [TestCase("scope")]
        [TestCase("registered")]
        [TestCase("enum")]
        public void UnprovenOrIncompatibleSlot_UsesNewDefault(string change)
        {
            var type = change == "enum" ? BuiltInBlackboardTypes.Enum32.TypeId : BuiltInBlackboardTypes.Int32.TypeId;
            if (change == "registered") type = StableHash.Fnv1A64("test.registered");
            var size = change == "enum" ? (uint)BuiltInBlackboardTypes.Enum32.Size : 4u;
            var alignment = change == "enum" ? (uint)BuiltInBlackboardTypes.Enum32.Alignment : 4u;
            var oldSlot = new CompiledBlackboardSlotRecord(1, type, 1, change == "enum" ? 10ul : 0,
                change == "scope" ? BlackboardScope.Agent : BlackboardScope.Tree, 0, size, alignment, 0, 0);
            var newSlot = new CompiledBlackboardSlotRecord(1,
                change == "type" ? BuiltInBlackboardTypes.Float32.TypeId : type,
                change == "version" ? 2u : 1u, change == "enum" ? 20ul : 0,
                BlackboardScope.Tree, 0, change == "size" ? 8u : size, change == "alignment" ? 2u : alignment, 0, 0);
            var oldDefaults = new byte[size]; oldDefaults[0] = 11;
            var newDefaults = new byte[newSlot.Size]; newDefaults[0] = 27;
            var program = Program(oldSlot, oldDefaults);
            var next = Program(newSlot, newDefaults);
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                if (change != "scope") Write(old, 0, 91);
                fresh = Migrate(old, program, next);
                Assert.That(Read(fresh, 0), Is.EqualTo(27));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void UnwrittenCompatibleKey_KeepsOldDefaultAsItsLiveValue()
        {
            var program = Program(Slot("value", 0), new byte[] { 11, 12, 13, 14 });
            var next = Program(Slot("value", 4, 0), new byte[] { 21, 22, 23, 24 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                fresh = Migrate(old, program, next);
                Access(fresh, bytes => Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 0, 11, 12, 13, 14 })));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void LiveValue_MigratesEveryByteToNewOffset()
        {
            var program = Program(Slot("value", 0), new byte[] { 1, 2, 3, 4 });
            var next = Program(Slot("value", 4, 0), new byte[] { 5, 6, 7, 8 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            var live = new byte[] { 91, 92, 93, 94 };
            try
            {
                Access(old, bytes => { for (var i = 0; i < live.Length; i++) bytes[i] = live[i]; });
                fresh = Migrate(old, program, next);
                Access(fresh, bytes => Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 0, 91, 92, 93, 94 })));
                Access(old, bytes => Assert.That(bytes.ToArray(), Is.EqualTo(live)));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void RemovedKey_DoesNotOverwriteAnotherKeysDefault()
        {
            var program = Program(Slot("old", 0), new byte[] { 11, 0, 0, 0 });
            var next = Program(Slot("new", 0), new byte[] { 27, 0, 0, 0 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            try { Write(old, 0, 91); fresh = Migrate(old, program, next); Assert.That(Read(fresh, 0), Is.EqualTo(27)); }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void ExcludedSubtree_DoesNotOwnOrResetTreeValue()
        {
            var program = Program(Slot("value", 0), new byte[] { 11, 0, 0, 0 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            try
            {
                Write(old, 0, 91);
                fresh = Migrate(old, program, program, new[] { NativeHotReloadTestProgram.LeafANodeId });
                Assert.That(Read(fresh, 0), Is.EqualTo(91));
            }
            finally { fresh.Dispose(); old.Dispose(); }
        }

        [Test]
        public void FullRestart_UsesNewDefaultInsteadOfOldLiveValue()
        {
            var program = Program(Slot("value", 0), new byte[] { 11, 0, 0, 0 });
            var next = Program(Slot("value", 0), new byte[] { 27, 0, 0, 0 });
            var old = Build(program);
            var fresh = default(NativeHotReloadInstance);
            var restarted = false;
            try
            {
                Write(old, 0, 91);
                restarted = NativeHotReloadFullRestart.TryRestart(old, next, 1, Allocator.Persistent, out fresh, out _, out var failure);
                Assert.That(restarted, Is.True, failure.Code.ToString());
                Assert.That(Read(fresh, 0), Is.EqualTo(27));
            }
            finally { fresh.Dispose(); if (!restarted) old.Dispose(); }
        }

        [Test]
        public void RejectedActivePathMigration_PreservesOldValueAndExecution()
        {
            var program = Program(Slot("value", 0), new byte[] { 11, 0, 0, 0 });
            var old = Build(program);
            try
            {
                Tick(ref old, 1, () => Write(old, 0, 91));
                Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, program,
                    HotReloadCompatibilityClassifier.Classify(program, program), new[] { NativeHotReloadTestProgram.LeafANodeId },
                    Allocator.Persistent, out _, out _, out var failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                var observed = -1;
                Tick(ref old, 2, () => observed = Read(old, 0));
                Assert.That(observed, Is.EqualTo(91));
            }
            finally { old.Dispose(); }
        }

        private static NativeHotReloadInstance Build(CompiledProgram program)
        {
            Assert.That(NativeHotReloadInstance.TryBuild(program, Allocator.Persistent, out var instance, out var failure), Is.True, failure.Code.ToString());
            return instance;
        }

        private static NativeHotReloadInstance Migrate(NativeHotReloadInstance old, CompiledProgram program,
            CompiledProgram next, NodeId[] excluded = null)
        {
            Assert.That(NativeHotReloadStateMigration.TryMigrate(old, program, next,
                HotReloadCompatibilityClassifier.Classify(program, next), excluded ?? Array.Empty<NodeId>(),
                Allocator.Persistent, out var fresh, out _, out var failure), Is.True, failure.Code.ToString());
            return fresh;
        }

        private static void Tick(ref NativeHotReloadInstance instance, ulong update, Action callback)
        {
            Assert.That(instance.Machine.TryBeginUpdate(update, out var failure), Is.True, failure.Code.ToString());
            var ticked = false;
            for (var guard = 0; guard < 64; guard++)
            {
                Assert.That(instance.Machine.TryAdvance(out var step, out failure), Is.True, failure.Code.ToString());
                if (step.Kind == NativeLifecycleStepKindV1.Waiting) { Assert.That(ticked, Is.True); return; }
                if (step.Kind != NativeLifecycleStepKindV1.DispatchRequired) continue;
                if (step.Phase == BurstCallbackPhase.Tick) { callback(); ticked = true; }
                Assert.That(instance.Machine.TryCompleteDispatch(step.DispatchToken, BurstContextResult.Success, NodeStatus.Running, out failure), Is.True, failure.Code.ToString());
            }
            Assert.Fail("The running leaf did not reach its update boundary.");
        }

        private static byte Read(NativeHotReloadInstance instance, int offset)
        {
            byte value = 0;
            Access(instance, bytes => value = bytes[offset]);
            return value;
        }

        private static void Write(NativeHotReloadInstance instance, int offset, byte value)
            => Access(instance, bytes => bytes[offset] = value);

        private static void Access(NativeHotReloadInstance instance, Action<NativeArray<byte>> action)
        {
            Assert.That(instance.ProgramOwner.TryAcquireReadLease(out var programLease, out var failure), Is.True, failure.Code.ToString());
            try
            {
                Assert.That(instance.ArenaOwner.TryAcquireExecutionLease(programLease, out var lease, out failure), Is.True, failure.Code.ToString());
                try { action(lease.View.TreeBlackboard); }
                finally { Assert.That(instance.ArenaOwner.TryReleaseExecutionLease(lease, out failure), Is.True, failure.Code.ToString()); }
            }
            finally { Assert.That(instance.ProgramOwner.TryReleaseReadLease(programLease, out failure), Is.True, failure.Code.ToString()); }
        }

        private static CompiledBlackboardSlotRecord Slot(string key, uint offset, uint? defaultOffset = null)
            => new CompiledBlackboardSlotRecord(StableHash.Fnv1A64(key), BuiltInBlackboardTypes.Int32.TypeId,
                1, 0, BlackboardScope.Tree, offset, 4, 4, defaultOffset ?? offset,
                CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write);

        private static CompiledProgram Program(CompiledBlackboardSlotRecord slot, byte[] defaults)
            => Program(new[] { slot }, defaults);

        private static CompiledProgram Program(CompiledBlackboardSlotRecord[] slots, byte[] defaults)
        {
            var template = NativeHotReloadTestProgram.TwoLeafSequence(false);
            var h = template.Header;
            var reads = Enumerable.Range(0, slots.Length).Where(i => (slots[i].AccessFlags & CompiledBlackboardAccessFlags.Read) != 0).Select(i => (uint)i).ToArray();
            var writes = Enumerable.Range(0, slots.Length).Where(i => (slots[i].AccessFlags & CompiledBlackboardAccessFlags.Write) != 0).Select(i => (uint)i).ToArray();
            var nodes = template.Nodes.ToArray();
            var leaf = nodes[1];
            nodes[1] = new CompiledNodeRecord(leaf.NodeTypeId, leaf.NodeTypeVersion,
                leaf.ConfigOffset, leaf.ConfigSize, leaf.ConfigAlignment,
                leaf.InstanceMemoryOffset, leaf.InstanceMemorySize, leaf.InstanceMemoryAlignment,
                leaf.MemoryLifetime, leaf.Children, leaf.Flags, leaf.DebugIdentityIndex,
                new CompiledRange(0, (uint)reads.Length), new CompiledRange(0, (uint)writes.Length));
            CompiledProgram WithHash(CompiledHash hash) => new CompiledProgram(
                new CompiledProgramHeader(h.CompiledFormatVersion, h.ExecutionSemanticsVersion, h.CompilerVersion,
                    h.CanonicalSemanticHash, h.NodeRegistryHash, h.CanonicalPolicyHash, h.PolicyFormatVersion,
                    hash, h.RootNodeIndex, h.NodeCount, h.ChildIndexCount, (uint)slots.Length, h.DebugMapCount,
                    h.ConfigBlobSize, h.InstanceNodeMemorySize, h.RequiredMaximumAlignment, h.CapabilityFlags, h.DeterministicModeCompatible),
                nodes, template.ChildIndices, reads, writes,
                slots, template.Observers, template.WatchedSlotIndices, template.ConfigBlob, defaults, template.DebugMap);
            return WithHash(CompiledProgramContentHashV1.Compute(WithHash(h.CompiledContentHash)));
        }
    }
}
