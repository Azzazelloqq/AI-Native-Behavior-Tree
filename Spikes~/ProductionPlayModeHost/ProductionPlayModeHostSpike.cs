using AIBT;
using AIBT.Authoring;
using UnityEngine;

namespace AIBT.Spikes
{
    /// <summary>
    /// P7-010 disposable spike prototype: a MonoBehaviour production Play-mode host driving one
    /// compiled tree instance every Update() through the already-accepted public
    /// <see cref="ReferencePreviewDriver"/> (AIBT.Authoring's own facade over the reference
    /// executor -- chosen here so this spike needs zero AIBT.Runtime internals access and is not
    /// subject to Unity's restriction on adding Test-assembly components to a scene), and
    /// separately owning a real, public <see cref="NativeTraceChannelOwnerV1"/> that a host would
    /// hand to whatever code writes trace records into it. Proves (a) real per-frame ticking
    /// across real Play-mode frames and (b) that a host-owned trace channel accepts P3-010's own
    /// unmodified <c>NativeExecutionDebuggerSession.Attach()</c> mid-session with zero API
    /// changes. Archived to Spikes~/ProductionPlayModeHost/ and removed from Tests/Editor/ once
    /// verified -- never committed here.
    /// </summary>
    public sealed class ProductionPlayModeHostSpike : MonoBehaviour
    {
        private ReferencePreviewDriver _driver;
        private bool _hasDriver;
        private ulong _updateId;
        private ulong _traceSequence;
        private NativeHash256V1 _semanticHash;
        private TreeInstanceId _treeInstanceId;

        public NativeTraceChannelOwnerV1 TraceChannelOwner { get; private set; }
        public ulong TotalUpdates => _updateId;
        public string LastError { get; private set; }
        public string LastRootResult { get; private set; }

        private void Awake()
        {
            const string json = "{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"tree.spike.p7010\",\"name\":\"P7-010 Spike\",\"root\":\"n0\",\"nodes\":{\"n0\":{\"type\":\"aibt.test.running\",\"typeVersion\":1}}}";
            var parseResult = CanonicalTreeJson.Parse(json, "spikes/p7-010/tree.spike.p7010.aibt.json");
            if (parseResult.Document == null)
            {
                LastError = "parse-failed:" + parseResult.Diagnostics.Count;
                Debug.LogError("AIBT P7-010 spike: " + LastError);
                return;
            }

            if (!ReferencePreviewDriver.TryCreate(parseResult.Document, "spikes/p7-010/tree.spike.p7010.aibt.json", out _driver, out var diagnostics))
            {
                LastError = "compile-failed:" + diagnostics.Count;
                Debug.LogError("AIBT P7-010 spike: " + LastError);
                return;
            }

            _hasDriver = true;

            // A fixed, valid, non-zero placeholder hash for this spike's own trace records --
            // ReferencePreviewDriver does not publicly expose a CompiledProgram's real
            // CompiledHash (an AIBT.Runtime-internal detail), and this spike is proving the
            // host/channel-ownership shape, not real hash provenance (that is P7-007's job).
            _semanticHash = new NativeHash256V1(new CompiledHash(new string('7', CompiledHash.HexLength)));
            _treeInstanceId = new TreeInstanceId(1);

            var capacity = new NativeTraceChannelCapacityV1(recordCapacity: 65, payloadCapacity: 0, maximumPayloadBytes: 0, emissionCapacity: 256);
            if (!NativeTraceChannelOwnerV1.TryCreate(capacity, NativeTraceLevelV1.Lifecycle, _treeInstanceId, workerOrdinal: 0, Unity.Collections.Allocator.Persistent, out var owner, out var traceFailure))
            {
                LastError = "trace-create:" + traceFailure.Code;
                Debug.LogError("AIBT P7-010 spike: " + LastError);
                return;
            }

            TraceChannelOwner = owner;
            Debug.Log("AIBT P7-010 spike host ready: driver created, trace channel owner id " + owner.OwnerId);
        }

        private void Update()
        {
            if (!_hasDriver) return;
            _updateId++;
            var envelope = _driver.RunTick();
            LastRootResult = envelope.RootResult.HasValue ? envelope.RootResult.Value.ToString() : "Waiting";
            WriteTraceBracket(NativeTraceEventKindV1.UpdateStarted);
            WriteTraceBracket(NativeTraceEventKindV1.UpdateCompleted);
            Debug.Log($"AIBT P7-010 spike tick #{_updateId}: progress={envelope.Progress} root={LastRootResult} frame={Time.frameCount}");
        }

        private void WriteTraceBracket(NativeTraceEventKindV1 kind)
        {
            if (TraceChannelOwner == null) return;
            if (!TraceChannelOwner.TryAcquireWriter(out var lease, out var acquireFailure))
            {
                Debug.LogWarning("AIBT P7-010 spike trace acquire failed: " + acquireFailure.Code);
                return;
            }

            var writer = lease.Writer;
            _traceSequence++;
            var record = new NativeTraceRecordV1
            {
                TraceFormatVersion = NativeTraceRecordV1.FormatVersion,
                Phase = NativeUpdatePhaseV1.Execute,
                UpdateId = _updateId,
                SnapshotRevision = 1,
                TreeSemanticHash = _semanticHash,
                TreeInstanceId = _treeInstanceId.Value,
                Sequence = _traceSequence,
                WorkerOrdinal = 0,
                Kind = kind,
                RuntimeNodeIndex = CompiledIndex.Invalid,
                DebugIdentityIndex = CompiledIndex.Invalid,
                SourceNodeIndex = CompiledIndex.Invalid,
            };

            var appendResult = writer.TryAppend(record);
            if (appendResult != NativeTraceAppendResultV1.Written)
            {
                Debug.LogWarning("AIBT P7-010 spike trace append: " + appendResult);
            }

            if (!TraceChannelOwner.TryReleaseWriter(lease, out var releaseFailure))
            {
                Debug.LogWarning("AIBT P7-010 spike trace release failed: " + releaseFailure.Code);
            }
        }

        private void OnDestroy()
        {
            _hasDriver = false;
            if (TraceChannelOwner != null && TraceChannelOwner.State != NativeOwnerStateV1.Disposed)
            {
                TraceChannelOwner.TryDispose(out _);
            }
        }
    }
}
