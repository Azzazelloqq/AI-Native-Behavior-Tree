using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;

namespace AIBT
{
    // Applies ADR-P7-011 (P7-012) to production. Mirrors HotReloadStateMigration's own shape for the
    // native backend, consuming the already-accepted, backend-agnostic HotReloadProgramIdentityMap/
    // HotReloadCompatibilityClassifier unchanged (both operate purely on CompiledProgram/NodeId, no
    // reference-executor-specific type in either signature). Real, disclosed differences from the
    // reference-executor's own Migrate:
    //   1. Does NOT fall back to full restart when the old instance is active -- ADR-P7-011 decision 3
    //      explicitly permits migrating an active instance directly (proven live by the spike),
    //      unlike the reference executor's own idle-only scope reduction.
    //   2. Actually copies NodeMemory bytes per migrated node (Frame/Generation alone, which is all
    //      the disposable spike itself copied, silently drops every memory-backed node's real state --
    //      composite cursors, Cooldown/Timeout deadlines, any leaf's own working memory) and applies
    //      the composite-cursor-reset rule (ADR-P5-001 item 2 / ADR-P7-011 decision 5) for the first
    //      time in this codebase: a migrated composite node whose direct children's order changed
    //      gets its leading 4 NodeMemory cursor bytes reset to zero instead of copied, since
    //      NativeLifecycleMachineV1's own ReadCursor/WriteCursor -- not the inert NativeFrameStateV1.
    //      ChildCursor field -- is what a composite's branching decision actually reads.
    //   3. The Frames array is NOT indexed by compiled node index -- investigated directly against
    //      NativeLifecycleMachineV1's own dispatch code (ChildSelected/PopFrame): it is a call STACK
    //      indexed by DEPTH (_frames[control.Depth-1] is "the current frame"; entering a child pushes
    //      _frames[control.Depth] and increments Depth; exiting pops and decrements), reused across
    //      sibling nodes over the instance's lifetime -- a frame's OWN NodeIndex field, not its array
    //      position, says which node it currently represents. Copying "oldFrames[oldIndex] ->
    //      newFrames[newIndex]" by node index (as this file originally did) is simply wrong: it reads
    //      and writes unrelated stack slots. The correct migration walks the OLD instance's own
    //      Control.Depth-many active stack slots position-for-position (preserving depth), remapping
    //      only each frame's own NodeIndex field via the identity maps, then sets the fresh instance's
    //      own Control.Depth to match -- exactly the "capture/seed frame stack by depth, remap
    //      NodeIndex" approach Spikes~/ActiveInstanceHotReloadMigration/
    //      SpikeActiveInstanceHotReloadMigration.cs already proved out for the reference executor
    //      (P6-018), generalized here to the native backend's own Depth-indexed _frames.
    // P7-029: copied active descendants under a structurally changed composite are queued for
    // HotReload cancellation on the fresh machine. Their old result must never advance the new
    // cursor. The outermost changed active owner handles nested changes in one traversal.
    internal static class NativeHotReloadStateMigration
    {
        /// <param name="excludedNodeIds">
        /// Empty for pure compatible migration; a subtree's full node-ID set for subtree restart --
        /// the same shared mechanism, per ADR-P5-001's "one mechanism, not three" framing. The
        /// caller expands <see cref="HotReloadClassificationResult.RestartSubtreeRootNodeIds"/> to
        /// full subtrees (mirroring <c>HotReloadStateMigration.ExpandRestartSubtrees</c>'s own
        /// reference-executor-side logic) before calling this method.
        /// </param>
        /// <remarks>
        /// Unlike <see cref="NativeHotReloadFullRestart.TryRestart"/>, this never disposes
        /// <paramref name="oldInstance"/> -- matching the reference executor's own
        /// <c>HotReloadStateMigration.Migrate</c>, which likewise leaves <c>oldMachine</c> for the
        /// caller. The old instance is only read from (its live state is copied, never mutated), so
        /// a caller may still inspect it afterward before disposing it themselves.
        /// </remarks>
        internal static bool TryMigrate(
            NativeHotReloadInstance oldInstance,
            CompiledProgram oldProgram,
            CompiledProgram newProgram,
            HotReloadClassificationResult classification,
            IReadOnlyCollection<NodeId> excludedNodeIds,
            Allocator allocator,
            out NativeHotReloadInstance freshInstance,
            out NativeHotReloadMigrationReport report,
            out NativeRuntimeFailureV1 failure)
        {
            if (oldProgram == null) throw new ArgumentNullException(nameof(oldProgram));
            if (newProgram == null) throw new ArgumentNullException(nameof(newProgram));
            if (classification == null) throw new ArgumentNullException(nameof(classification));

            report = default;
            if (!NativeHotReloadInstance.TryBuild(newProgram, allocator, out freshInstance, out failure))
            {
                return false;
            }

            var oldMap = HotReloadProgramIdentityMap.Build(oldProgram);
            var newMap = HotReloadProgramIdentityMap.Build(newProgram);

            var oldIndexToNodeId = new Dictionary<uint, NodeId>(oldProgram.DebugMap.Count);
            foreach (var entry in oldProgram.DebugMap)
            {
                oldIndexToNodeId[entry.RuntimeNodeIndex] = entry.AuthoringNodeId;
            }

            if (!oldInstance.ProgramOwner.TryAcquireReadLease(out var oldProgramLease, out failure))
            {
                freshInstance.Dispose();
                return false;
            }

            if (!oldInstance.ArenaOwner.TryAcquireExecutionLease(oldProgramLease, out var oldExecLease, out failure))
            {
                oldInstance.ProgramOwner.TryReleaseReadLease(oldProgramLease, out _);
                freshInstance.Dispose();
                return false;
            }

            if (!freshInstance.ProgramOwner.TryAcquireReadLease(out var newProgramLease, out failure))
            {
                oldInstance.ArenaOwner.TryReleaseExecutionLease(oldExecLease, out _);
                oldInstance.ProgramOwner.TryReleaseReadLease(oldProgramLease, out _);
                freshInstance.Dispose();
                return false;
            }

            if (!freshInstance.ArenaOwner.TryAcquireExecutionLease(newProgramLease, out var newExecLease, out failure))
            {
                freshInstance.ProgramOwner.TryReleaseReadLease(newProgramLease, out _);
                oldInstance.ArenaOwner.TryReleaseExecutionLease(oldExecLease, out _);
                oldInstance.ProgramOwner.TryReleaseReadLease(oldProgramLease, out _);
                freshInstance.Dispose();
                return false;
            }

            var oldFrames = oldExecLease.View.Frames;
            var oldGenerations = oldExecLease.View.Generations;
            var oldMemory = oldExecLease.View.NodeMemory;
            var newFrames = newExecLease.View.Frames;
            var newGenerations = newExecLease.View.Generations;
            var newMemory = newExecLease.View.NodeMemory;

            uint migratedCount = 0, resetCount = 0, droppedCount = 0, cursorResetCount = 0;
            foreach (var pair in classification.NodeVerdicts)
            {
                if (pair.Value.Category == HotReloadNodeVerdictCategory.Dropped)
                {
                    droppedCount++;
                    continue;
                }

                if (!newMap.TryGetRuntimeIndex(pair.Key, out var newIndex))
                {
                    continue;
                }

                if (pair.Value.Category != HotReloadNodeVerdictCategory.Migrate || excludedNodeIds.Contains(pair.Key))
                {
                    // New nodes, Incompatible nodes, and anything nested under a restarting subtree
                    // keep the fresh instance's default (zero-initialized) state.
                    resetCount++;
                    continue;
                }

                if (!oldMap.TryGetRuntimeIndex(pair.Key, out var oldIndex))
                {
                    continue;
                }

                newGenerations[(int)newIndex] = oldGenerations[(int)oldIndex];

                var oldRecord = oldInstance.Nodes[(int)oldIndex];
                var newRecord = freshInstance.Nodes[(int)newIndex];
                CopyNodeMemory(oldMemory, oldRecord, newMemory, newRecord);
                if (NativeHotReloadInstance.ClassifyKind(newRecord.NodeTypeId) == NativeLifecycleNodeKindV1.Cooldown)
                    freshInstance.CooldownInitialized[(int)newIndex] = oldInstance.CooldownInitialized[(int)oldIndex];

                if (classification.StructuralChildChangeNodeIds.Contains(pair.Key) && IsCompositeKind(newRecord.NodeTypeId))
                {
                    ResetCursor(newMemory, newRecord);
                    cursorResetCount++;
                }

                migratedCount++;
            }

            // The active call stack (Control.Depth-many _frames slots, position 0 = root) is
            // copied SEPARATELY from the per-node loop above: _frames is indexed by DEPTH, not by
            // compiled node index (confirmed by reading NativeLifecycleMachineV1's own
            // ChildSelected/PopFrame code -- entering a child pushes _frames[Depth] and increments
            // Depth, exiting pops and decrements, so the same array slot is reused across sibling
            // nodes over the instance's lifetime). Each slot's own NodeIndex field, not its array
            // position, says which node it currently represents, so migrating it means copying the
            // slot at the SAME depth and remapping only that field.
            var oldControl = oldInstance.Control[0];
            if (oldControl.Depth > (uint)newFrames.Length)
            {
                freshInstance.ArenaOwner.TryReleaseExecutionLease(newExecLease, out _);
                freshInstance.ProgramOwner.TryReleaseReadLease(newProgramLease, out _);
                oldInstance.ArenaOwner.TryReleaseExecutionLease(oldExecLease, out _);
                oldInstance.ProgramOwner.TryReleaseReadLease(oldProgramLease, out _);
                freshInstance.Dispose();
                failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid);
                return false;
            }

            uint resetOwnerDepth = 0;
            for (var depth = 0u; depth < oldControl.Depth; depth++)
            {
                var frame = oldFrames[(int)depth];
                if (!oldIndexToNodeId.TryGetValue(frame.NodeIndex, out var frameNodeId)
                    || !newMap.TryGetRuntimeIndex(frameNodeId, out var frameNewIndex)
                    || !classification.NodeVerdicts.TryGetValue(frameNodeId, out var frameVerdict)
                    || frameVerdict.Category != HotReloadNodeVerdictCategory.Migrate
                    || excludedNodeIds.Contains(frameNodeId))
                {
                    // The active path runs through a node that cannot migrate (dropped,
                    // incompatible, excluded by a subtree restart, or lacking a stable NodeId at
                    // all) -- there is no safe way to preserve continuity past this point without
                    // risking an inconsistent frame stack. ADR-P7-011's own scope is the shallow,
                    // fully-migratable active path; the caller must fall back to a full restart
                    // when this happens, rather than this method silently truncating the stack.
                    freshInstance.ArenaOwner.TryReleaseExecutionLease(newExecLease, out _);
                    freshInstance.ProgramOwner.TryReleaseReadLease(newProgramLease, out _);
                    oldInstance.ArenaOwner.TryReleaseExecutionLease(oldExecLease, out _);
                    oldInstance.ProgramOwner.TryReleaseReadLease(oldProgramLease, out _);
                    freshInstance.Dispose();
                    failure = new NativeRuntimeFailureV1(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid);
                    return false;
                }

                frame.NodeIndex = frameNewIndex;
                newFrames[(int)depth] = frame;
                if (resetOwnerDepth == 0 && frame.LifecycleState == NativeFrameLifecycleStateV1.Running
                    && classification.StructuralChildChangeNodeIds.Contains(frameNodeId)
                    && IsCompositeKind(freshInstance.Nodes[(int)frameNewIndex].NodeTypeId))
                    resetOwnerDepth = depth + 1;
            }

            var newControl = freshInstance.Control[0];
            newControl.Depth = oldControl.Depth;
            freshInstance.Control[0] = newControl;

            freshInstance.ArenaOwner.TryReleaseExecutionLease(newExecLease, out _);
            freshInstance.ProgramOwner.TryReleaseReadLease(newProgramLease, out _);
            oldInstance.ArenaOwner.TryReleaseExecutionLease(oldExecLease, out _);
            oldInstance.ProgramOwner.TryReleaseReadLease(oldProgramLease, out _);

            // Cancellation is dispatched by the fresh machine before new-order traversal;
            // migration itself neither calls application code nor mutates the old instance.
            if (resetOwnerDepth != 0 && !freshInstance.Machine.TryQueueHotReloadChildReset(resetOwnerDepth, out failure))
            {
                freshInstance.Dispose();
                return false;
            }

            report = NativeHotReloadMigrationReport.Migrated(migratedCount, resetCount, droppedCount, cursorResetCount);
            failure = default;
            return true;
        }

        private static void CopyNodeMemory(
            NativeArray<byte> oldMemory, NativeCompiledNodeRecordV1 oldRecord,
            NativeArray<byte> newMemory, NativeCompiledNodeRecordV1 newRecord)
        {
            // The smaller of the two sizes: an incompatible-layout node never classifies Migrate at
            // all (HotReloadCompatibilityClassifier's own HasCompatibleLayout check), so in practice
            // old/new sizes always match for a node reaching this call -- the Math.Min is defensive,
            // never silently truncating a real layout mismatch that should have been IncompatibleRestart.
            var size = Math.Min(oldRecord.InstanceMemorySize, newRecord.InstanceMemorySize);
            for (var offset = 0u; offset < size; offset++)
            {
                newMemory[(int)(newRecord.InstanceMemoryOffset + offset)] = oldMemory[(int)(oldRecord.InstanceMemoryOffset + offset)];
            }
        }

        // The real cursor a composite's branching decision reads/writes lives in the leading 4
        // NodeMemory bytes (NativeLifecycleMachineV1's own private ReadCursor/WriteCursor, confirmed
        // by reading the dispatch code directly) -- NOT NativeFrameStateV1.ChildCursor, which is
        // written but never read by any dispatch path.
        private static void ResetCursor(NativeArray<byte> memory, NativeCompiledNodeRecordV1 record)
        {
            if (record.InstanceMemorySize < 4u)
            {
                return;
            }

            var offset = (int)record.InstanceMemoryOffset;
            memory[offset] = 0;
            memory[offset + 1] = 0;
            memory[offset + 2] = 0;
            memory[offset + 3] = 0;
        }

        private static bool IsCompositeKind(ulong typeId)
        {
            var kind = NativeHotReloadInstance.ClassifyKind(typeId);
            return kind == NativeLifecycleNodeKindV1.MemorySequence
                || kind == NativeLifecycleNodeKindV1.MemorySelector
                || kind == NativeLifecycleNodeKindV1.ReactiveSequence
                || kind == NativeLifecycleNodeKindV1.ReactiveSelector;
        }
    }
}
