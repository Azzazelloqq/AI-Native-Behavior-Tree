using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace AIBT.NativeOwnership.Spike
{
    public enum NativeOwnerState
    {
        Uninitialized,
        Initialized,
        Executing,
        Building,
        Scheduled,
        Completed,
        Aborted,
        Faulted,
        Disposed
    }

    public enum NativeExecutionMode
    {
        Success,
        Abort,
        Fault
    }

    public enum NativeResourceKind
    {
        ProgramRecords,
        ConfigBytes,
        InstanceBytes,
        InputRecords,
        InputPayloadBytes,
        CompletionRecords,
        CompletionPayloadBytes,
        CommandRecords,
        CommandPayloadBytes,
        SharedContributionRecords,
        SharedContributionPayloadBytes,
        DiagnosticRecords,
        DiagnosticPayloadBytes,
        TraceRecords,
        TracePayloadBytes,
        Alignment,
        WorkItems,
        ScratchBytes
    }

    public static class NativeDiagnosticCodes
    {
        public const int AllocatorInvalid = 4301;
        public const int CapacityPlanInvalid = 4302;
        public const int CapacityArithmeticOverflow = 4303;
        public const int ProgramCapacityExceeded = 4304;
        public const int InstanceCapacityExceeded = 4305;
        public const int SnapshotCapacityExceeded = 4306;
        public const int OutputCapacityExceeded = 4307;
        public const int CompletionCapacityExceeded = 4308;
        public const int DiagnosticCapacityExceeded = 4309;
        public const int TraceCapacityExceeded = 4310;
        public const int LifetimeStateInvalid = 4311;
        public const int LiveJobOwnershipViolation = 4312;

        public static readonly IReadOnlyList<int> All = new[]
        {
            AllocatorInvalid,
            CapacityPlanInvalid,
            CapacityArithmeticOverflow,
            ProgramCapacityExceeded,
            InstanceCapacityExceeded,
            SnapshotCapacityExceeded,
            OutputCapacityExceeded,
            CompletionCapacityExceeded,
            DiagnosticCapacityExceeded,
            TraceCapacityExceeded,
            LifetimeStateInvalid,
            LiveJobOwnershipViolation
        };
    }

    public readonly struct NativeDiagnostic
    {
        public NativeDiagnostic(
            int code,
            NativeResourceKind resourceKind = NativeResourceKind.ProgramRecords,
            ulong requested = 0,
            ulong capacity = 0,
            ulong ownerId = 0,
            uint generation = 0,
            uint leaseId = 0)
        {
            Code = code;
            ResourceKind = resourceKind;
            Requested = requested;
            Capacity = capacity;
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public int Code { get; }
        public NativeResourceKind ResourceKind { get; }
        public ulong Requested { get; }
        public ulong Capacity { get; }
        public ulong OwnerId { get; }
        public uint Generation { get; }
        public uint LeaseId { get; }
        public bool IsError => Code != 0;
    }

    public sealed class NativeOwnershipException : InvalidOperationException
    {
        public NativeOwnershipException(NativeDiagnostic diagnostic, string message, Exception inner = null)
            : base($"AIBT{diagnostic.Code:0000}: {message}", inner)
        {
            Diagnostic = diagnostic;
        }

        public NativeDiagnostic Diagnostic { get; }
        public int DiagnosticCode => Diagnostic.Code;
    }

    public sealed class NativeCapacityValues
    {
        private readonly ulong[] _values;

        private NativeCapacityValues(ulong[] values)
        {
            _values = (ulong[])values.Clone();
        }

        public static NativeCapacityValues Uniform(ulong value)
        {
            var values = new ulong[Enum.GetValues(typeof(NativeResourceKind)).Length];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = value;
            }

            return new NativeCapacityValues(values);
        }

        public NativeCapacityValues With(NativeResourceKind kind, ulong value)
        {
            var values = (ulong[])_values.Clone();
            values[(int)kind] = value;
            return new NativeCapacityValues(values);
        }

        public ulong Get(NativeResourceKind kind)
        {
            return _values[(int)kind];
        }
    }

    public sealed class NativeCapacityPlan
    {
        private readonly int[] _capacities;

        private NativeCapacityPlan(int[] capacities)
        {
            _capacities = capacities;
        }

        public int Capacity(NativeResourceKind kind)
        {
            return _capacities[(int)kind];
        }

        public static bool TryCreate(
            NativeCapacityValues requirements,
            NativeCapacityValues limits,
            out NativeCapacityPlan plan,
            out NativeDiagnostic diagnostic)
        {
            plan = null;
            if (requirements == null || limits == null)
            {
                diagnostic = new NativeDiagnostic(NativeDiagnosticCodes.CapacityPlanInvalid);
                return false;
            }

            var requiredAlignment = requirements.Get(NativeResourceKind.Alignment);
            var maximumAlignment = limits.Get(NativeResourceKind.Alignment);
            if (!IsPowerOfTwo(requiredAlignment) || !IsPowerOfTwo(maximumAlignment))
            {
                diagnostic = new NativeDiagnostic(
                    NativeDiagnosticCodes.CapacityPlanInvalid,
                    NativeResourceKind.Alignment,
                    requiredAlignment,
                    maximumAlignment);
                return false;
            }

            var capacities = new int[Enum.GetValues(typeof(NativeResourceKind)).Length];
            foreach (NativeResourceKind kind in Enum.GetValues(typeof(NativeResourceKind)))
            {
                var required = requirements.Get(kind);
                var capacity = limits.Get(kind);
                if (required > int.MaxValue || capacity > int.MaxValue)
                {
                    diagnostic = new NativeDiagnostic(
                        NativeDiagnosticCodes.CapacityArithmeticOverflow,
                        kind,
                        required,
                        capacity);
                    return false;
                }

                if (required > capacity)
                {
                    diagnostic = new NativeDiagnostic(CapacityFailureCode(kind), kind, required, capacity);
                    return false;
                }

                capacities[(int)kind] = (int)capacity;
            }

            plan = new NativeCapacityPlan(capacities);
            diagnostic = default;
            return true;
        }

        public static bool TryCheckedAdd(
            uint left,
            uint right,
            NativeResourceKind kind,
            out uint result,
            out NativeDiagnostic diagnostic)
        {
            if (uint.MaxValue - left < right)
            {
                result = 0;
                diagnostic = new NativeDiagnostic(
                    NativeDiagnosticCodes.CapacityArithmeticOverflow,
                    kind,
                    (ulong)left + right,
                    uint.MaxValue);
                return false;
            }

            result = left + right;
            diagnostic = default;
            return true;
        }

        public static int CapacityFailureCode(NativeResourceKind kind)
        {
            switch (kind)
            {
                case NativeResourceKind.ProgramRecords:
                case NativeResourceKind.ConfigBytes:
                    return NativeDiagnosticCodes.ProgramCapacityExceeded;
                case NativeResourceKind.InstanceBytes:
                case NativeResourceKind.WorkItems:
                case NativeResourceKind.ScratchBytes:
                    return NativeDiagnosticCodes.InstanceCapacityExceeded;
                case NativeResourceKind.InputRecords:
                case NativeResourceKind.InputPayloadBytes:
                    return NativeDiagnosticCodes.SnapshotCapacityExceeded;
                case NativeResourceKind.CompletionRecords:
                case NativeResourceKind.CompletionPayloadBytes:
                    return NativeDiagnosticCodes.CompletionCapacityExceeded;
                case NativeResourceKind.CommandRecords:
                case NativeResourceKind.CommandPayloadBytes:
                case NativeResourceKind.SharedContributionRecords:
                case NativeResourceKind.SharedContributionPayloadBytes:
                    return NativeDiagnosticCodes.OutputCapacityExceeded;
                case NativeResourceKind.DiagnosticRecords:
                case NativeResourceKind.DiagnosticPayloadBytes:
                    return NativeDiagnosticCodes.DiagnosticCapacityExceeded;
                case NativeResourceKind.TraceRecords:
                case NativeResourceKind.TracePayloadBytes:
                    return NativeDiagnosticCodes.TraceCapacityExceeded;
                default:
                    return NativeDiagnosticCodes.CapacityPlanInvalid;
            }
        }

        private static bool IsPowerOfTwo(ulong value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }
    }

    public readonly struct NativeLeaseToken
    {
        public NativeLeaseToken(ulong ownerId, uint generation, uint leaseId)
        {
            OwnerId = ownerId;
            Generation = generation;
            LeaseId = leaseId;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public uint LeaseId { get; }
    }

    public readonly struct NativeProgramBinding
    {
        public NativeProgramBinding(ulong ownerId, uint generation)
        {
            OwnerId = ownerId;
            Generation = generation;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
    }

    internal sealed class NativeLeaseTracker
    {
        private readonly HashSet<uint> _active = new HashSet<uint>(4);
        private uint _nextLeaseId;

        public NativeLeaseTracker(ulong ownerId, uint generation)
        {
            OwnerId = ownerId;
            Generation = generation;
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public int ActiveCount => _active.Count;

        public NativeLeaseToken Acquire()
        {
            if (!NativeCapacityPlan.TryCheckedAdd(
                    _nextLeaseId,
                    1,
                    NativeResourceKind.WorkItems,
                    out _nextLeaseId,
                    out var overflow))
            {
                throw new NativeOwnershipException(overflow, "lease ID overflow");
            }

            _active.Add(_nextLeaseId);
            return new NativeLeaseToken(OwnerId, Generation, _nextLeaseId);
        }

        public void Release(NativeLeaseToken token)
        {
            Validate(token);
            _active.Remove(token.LeaseId);
        }

        public void Validate(NativeLeaseToken token)
        {
            if (token.OwnerId != OwnerId || token.Generation != Generation || token.LeaseId == 0 || !_active.Contains(token.LeaseId))
            {
                var diagnostic = new NativeDiagnostic(
                    NativeDiagnosticCodes.LifetimeStateInvalid,
                    NativeResourceKind.WorkItems,
                    ownerId: token.OwnerId,
                    generation: token.Generation,
                    leaseId: token.LeaseId);
                throw new NativeOwnershipException(diagnostic, "lease token is default, stale, foreign, or wrong-generation");
            }
        }
    }

    public sealed class NativeAllocationFailureInjector
    {
        private readonly int _failAfterSuccessfulAllocation;
        private int _successfulAllocations;

        public NativeAllocationFailureInjector(int failAfterSuccessfulAllocation)
        {
            _failAfterSuccessfulAllocation = failAfterSuccessfulAllocation;
        }

        internal void AfterAllocation(NativeResourceKind kind)
        {
            _successfulAllocations++;
            if (_successfulAllocations == _failAfterSuccessfulAllocation)
            {
                var diagnostic = new NativeDiagnostic(NativeCapacityPlan.CapacityFailureCode(kind), kind, 1, 1);
                throw new NativeOwnershipException(diagnostic, "injected native allocation failure");
            }
        }
    }

    public static class NativeAllocationProbe
    {
        private static int _liveAllocations;

        public static int LiveAllocations => Volatile.Read(ref _liveAllocations);
        internal static void Allocated() => Interlocked.Increment(ref _liveAllocations);
        internal static void Released() => Interlocked.Decrement(ref _liveAllocations);
    }

    internal static class NativeAllocation
    {
        public static NativeArray<T> Create<T>(
            int length,
            Allocator allocator,
            NativeResourceKind kind,
            NativeAllocationFailureInjector injector)
            where T : struct
        {
            if (allocator != Allocator.Persistent)
            {
                var allocatorDiagnostic = new NativeDiagnostic(NativeDiagnosticCodes.AllocatorInvalid, kind);
                throw new NativeOwnershipException(allocatorDiagnostic, "v1 owners require Allocator.Persistent");
            }

            NativeArray<T> result = default;
            try
            {
                result = new NativeArray<T>(length, allocator, NativeArrayOptions.ClearMemory);
                NativeAllocationProbe.Allocated();
                injector?.AfterAllocation(kind);
                return result;
            }
            catch (NativeOwnershipException)
            {
                Dispose(ref result);
                throw;
            }
            catch (Exception exception)
            {
                Dispose(ref result);
                var diagnostic = new NativeDiagnostic(NativeCapacityPlan.CapacityFailureCode(kind), kind, (ulong)length, (ulong)length);
                throw new NativeOwnershipException(diagnostic, "native allocation failed", exception);
            }
        }

        public static void Dispose<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (!array.IsCreated)
            {
                return;
            }

            array.Dispose();
            array = default;
            NativeAllocationProbe.Released();
        }
    }

    internal static class NativeOwnerIdentity
    {
        private static long _nextOwnerId;

        public static ulong Next()
        {
            var ownerId = Interlocked.Increment(ref _nextOwnerId);
            if (ownerId <= 0)
            {
                var diagnostic = new NativeDiagnostic(
                    NativeDiagnosticCodes.CapacityArithmeticOverflow,
                    NativeResourceKind.WorkItems,
                    (ulong)ownerId,
                    long.MaxValue);
                throw new NativeOwnershipException(diagnostic, "owner ID overflow");
            }

            return (ulong)ownerId;
        }
    }

    public sealed class NativeProgramImageOwner : IDisposable
    {
        private NativeArray<int> _records;
        private NativeArray<byte> _config;
        private readonly NativeLeaseTracker _leases;

        public NativeProgramImageOwner(
            NativeCapacityPlan plan,
            int value,
            uint generation = 1,
            Allocator allocator = Allocator.Persistent,
            NativeAllocationFailureInjector injector = null)
        {
            if (generation == 0)
            {
                throw LifetimeError(0, 0, 0, "program generation must be nonzero");
            }

            OwnerId = NativeOwnerIdentity.Next();
            Generation = generation;
            _leases = new NativeLeaseTracker(OwnerId, Generation);
            try
            {
                _records = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.ProgramRecords), allocator, NativeResourceKind.ProgramRecords, injector);
                _config = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.ConfigBytes), allocator, NativeResourceKind.ConfigBytes, injector);
                _records[0] = value;
                State = NativeOwnerState.Initialized;
            }
            catch
            {
                NativeAllocation.Dispose(ref _config);
                NativeAllocation.Dispose(ref _records);
                State = NativeOwnerState.Uninitialized;
                throw;
            }
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public NativeOwnerState State { get; private set; }
        public NativeProgramBinding Binding => new NativeProgramBinding(OwnerId, Generation);
        internal NativeArray<int> Records => _records;

        public NativeLeaseToken AcquireLeaseForProbe()
        {
            EnsureInitialized();
            return _leases.Acquire();
        }

        public void ReleaseLeaseForProbe(NativeLeaseToken token)
        {
            _leases.Release(token);
        }

        internal NativeLeaseToken Acquire() => AcquireLeaseForProbe();
        internal void Release(NativeLeaseToken token) => _leases.Release(token);

        public void Dispose()
        {
            if (State == NativeOwnerState.Disposed)
            {
                throw LifetimeError(OwnerId, Generation, 0, "program image was disposed twice");
            }

            if (_leases.ActiveCount != 0)
            {
                throw LiveJobError(OwnerId, Generation, 0, "program image has live leases");
            }

            NativeAllocation.Dispose(ref _config);
            NativeAllocation.Dispose(ref _records);
            State = NativeOwnerState.Disposed;
        }

        private void EnsureInitialized()
        {
            if (State != NativeOwnerState.Initialized)
            {
                throw LifetimeError(OwnerId, Generation, 0, "program image is not initialized");
            }
        }

        internal static NativeOwnershipException LifetimeError(ulong ownerId, uint generation, uint leaseId, string message)
        {
            return new NativeOwnershipException(
                new NativeDiagnostic(NativeDiagnosticCodes.LifetimeStateInvalid, ownerId: ownerId, generation: generation, leaseId: leaseId),
                message);
        }

        internal static NativeOwnershipException LiveJobError(ulong ownerId, uint generation, uint leaseId, string message)
        {
            return new NativeOwnershipException(
                new NativeDiagnostic(NativeDiagnosticCodes.LiveJobOwnershipViolation, ownerId: ownerId, generation: generation, leaseId: leaseId),
                message);
        }
    }

    public sealed class NativeInstanceArenaOwner : IDisposable
    {
        private NativeArray<byte> _committed;
        private readonly NativeLeaseTracker _leases;

        public NativeInstanceArenaOwner(
            NativeCapacityPlan plan,
            NativeProgramBinding binding,
            byte initialValue,
            Allocator allocator = Allocator.Persistent,
            NativeAllocationFailureInjector injector = null)
        {
            if (binding.OwnerId == 0 || binding.Generation == 0)
            {
                throw NativeProgramImageOwner.LifetimeError(binding.OwnerId, binding.Generation, 0, "instance program binding is invalid");
            }

            OwnerId = NativeOwnerIdentity.Next();
            Generation = 1;
            ProgramBinding = binding;
            _leases = new NativeLeaseTracker(OwnerId, Generation);
            try
            {
                _committed = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.InstanceBytes), allocator, NativeResourceKind.InstanceBytes, injector);
                _committed[0] = initialValue;
                State = NativeOwnerState.Initialized;
            }
            catch
            {
                NativeAllocation.Dispose(ref _committed);
                State = NativeOwnerState.Uninitialized;
                throw;
            }
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public NativeProgramBinding ProgramBinding { get; }
        public NativeOwnerState State { get; private set; }
        public byte Value => State == NativeOwnerState.Executing
            ? throw NativeProgramImageOwner.LiveJobError(OwnerId, Generation, 0, "host read attempted during instance execution")
            : _committed[0];
        internal NativeArray<byte> Committed => _committed;

        internal void ValidateProgram(NativeProgramImageOwner program)
        {
            if (program == null || ProgramBinding.OwnerId != program.OwnerId || ProgramBinding.Generation != program.Generation)
            {
                throw NativeProgramImageOwner.LifetimeError(
                    ProgramBinding.OwnerId,
                    ProgramBinding.Generation,
                    0,
                    "instance is bound to a different program owner or generation");
            }
        }

        internal NativeLeaseToken Acquire()
        {
            if (State == NativeOwnerState.Executing)
            {
                throw NativeProgramImageOwner.LiveJobError(OwnerId, Generation, 0, "instance already has a live execution lease");
            }

            if (State != NativeOwnerState.Initialized)
            {
                throw NativeProgramImageOwner.LifetimeError(OwnerId, Generation, 0, "instance is not initialized");
            }

            var token = _leases.Acquire();
            State = NativeOwnerState.Executing;
            return token;
        }

        internal void CommitAndRelease(NativeLeaseToken token, NativeArray<byte> staged)
        {
            _leases.Validate(token);
            if (State != NativeOwnerState.Executing || staged.Length != _committed.Length)
            {
                throw NativeProgramImageOwner.LifetimeError(OwnerId, Generation, token.LeaseId, "instance commit layout/state is invalid");
            }

            NativeArray<byte>.Copy(staged, _committed);
            _leases.Release(token);
            State = NativeOwnerState.Initialized;
        }

        internal void ReleaseWithoutCommit(NativeLeaseToken token)
        {
            _leases.Validate(token);
            _leases.Release(token);
            State = NativeOwnerState.Initialized;
        }

        public void Dispose()
        {
            if (State == NativeOwnerState.Disposed)
            {
                throw NativeProgramImageOwner.LifetimeError(OwnerId, Generation, 0, "instance was disposed twice");
            }

            if (_leases.ActiveCount != 0)
            {
                throw NativeProgramImageOwner.LiveJobError(OwnerId, Generation, 0, "instance has a live execution lease");
            }

            NativeAllocation.Dispose(ref _committed);
            State = NativeOwnerState.Disposed;
        }
    }

    public sealed class NativeInputFrameOwner : IDisposable
    {
        private NativeArray<int> _records;
        private NativeArray<byte> _payload;
        private NativeArray<int> _completions;
        private NativeArray<byte> _completionPayload;
        private readonly NativeLeaseTracker _leases;

        public NativeInputFrameOwner(
            NativeCapacityPlan plan,
            int value,
            Allocator allocator = Allocator.Persistent,
            NativeAllocationFailureInjector injector = null)
        {
            OwnerId = NativeOwnerIdentity.Next();
            Generation = 1;
            _leases = new NativeLeaseTracker(OwnerId, Generation);
            try
            {
                _records = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.InputRecords), allocator, NativeResourceKind.InputRecords, injector);
                _payload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.InputPayloadBytes), allocator, NativeResourceKind.InputPayloadBytes, injector);
                _completions = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.CompletionRecords), allocator, NativeResourceKind.CompletionRecords, injector);
                _completionPayload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.CompletionPayloadBytes), allocator, NativeResourceKind.CompletionPayloadBytes, injector);
                _records[0] = value;
                State = NativeOwnerState.Initialized;
            }
            catch
            {
                NativeAllocation.Dispose(ref _completionPayload);
                NativeAllocation.Dispose(ref _completions);
                NativeAllocation.Dispose(ref _payload);
                NativeAllocation.Dispose(ref _records);
                State = NativeOwnerState.Uninitialized;
                throw;
            }
        }

        public ulong OwnerId { get; }
        public uint Generation { get; }
        public NativeOwnerState State { get; private set; }
        internal NativeArray<int> Records => _records;

        internal NativeLeaseToken Acquire()
        {
            if (State != NativeOwnerState.Initialized && State != NativeOwnerState.Executing)
            {
                throw NativeProgramImageOwner.LifetimeError(OwnerId, Generation, 0, "input frame is not initialized");
            }

            var token = _leases.Acquire();
            State = NativeOwnerState.Executing;
            return token;
        }

        internal void Release(NativeLeaseToken token)
        {
            _leases.Release(token);
            State = _leases.ActiveCount == 0 ? NativeOwnerState.Initialized : NativeOwnerState.Executing;
        }

        public void Dispose()
        {
            if (State == NativeOwnerState.Disposed)
            {
                throw NativeProgramImageOwner.LifetimeError(OwnerId, Generation, 0, "input frame was disposed twice");
            }

            if (_leases.ActiveCount != 0)
            {
                throw NativeProgramImageOwner.LiveJobError(OwnerId, Generation, 0, "input frame has live leases");
            }

            NativeAllocation.Dispose(ref _completionPayload);
            NativeAllocation.Dispose(ref _completions);
            NativeAllocation.Dispose(ref _payload);
            NativeAllocation.Dispose(ref _records);
            State = NativeOwnerState.Disposed;
        }
    }

    public readonly struct NativeSharedStreamReservation
    {
        public NativeSharedStreamReservation(ulong treeInstanceId, uint records, uint payloadBytes)
        {
            TreeInstanceId = treeInstanceId;
            Records = records;
            PayloadBytes = payloadBytes;
        }

        public ulong TreeInstanceId { get; }
        public uint Records { get; }
        public uint PayloadBytes { get; }
    }

    public sealed class NativeExecutionRequest
    {
        public int CommandRecords { get; set; }
        public int CommandPayloadBytes { get; set; }
        public int CompletionRecords { get; set; }
        public int CompletionPayloadBytes { get; set; }
        public int DiagnosticRecords { get; set; }
        public int DiagnosticPayloadBytes { get; set; }
        public int TraceRecords { get; set; }
        public int TracePayloadBytes { get; set; }
        public int WorkItems { get; set; }
        public int ScratchBytes { get; set; }
        public NativeSharedStreamReservation[] SharedStreams { get; set; } = Array.Empty<NativeSharedStreamReservation>();
        public NativeExecutionMode Mode { get; set; }
    }

    public readonly struct NativeCommandRecord
    {
        public NativeCommandRecord(int sequence, int value)
        {
            Sequence = sequence;
            Value = value;
        }

        public int Sequence { get; }
        public int Value { get; }
    }

    public sealed class NativeExecutionPassOwner : IDisposable
    {
        private const int ControlCode = 0;
        private const int ControlRequested = 1;
        private const int ControlPublished = 2;
        private const int ControlOutcome = 3;
        private const int ControlCallbacks = 4;

        private readonly NativeCapacityPlan _plan;
        private NativeArray<byte> _stagedInstance;
        private NativeArray<int> _work;
        private NativeArray<byte> _scratch;
        private NativeArray<NativeCommandRecord> _commands;
        private NativeArray<byte> _commandPayload;
        private NativeArray<int> _completions;
        private NativeArray<byte> _completionPayload;
        private NativeArray<int> _shared;
        private NativeArray<byte> _sharedPayload;
        private NativeArray<int> _diagnostics;
        private NativeArray<byte> _diagnosticPayload;
        private NativeArray<int> _trace;
        private NativeArray<byte> _tracePayload;
        private NativeArray<int> _control;
        private NativeProgramImageOwner _program;
        private NativeInstanceArenaOwner _instance;
        private NativeInputFrameOwner _input;
        private NativeLeaseToken _programLease;
        private NativeLeaseToken _instanceLease;
        private NativeLeaseToken _inputLease;
        private JobHandle _handle;
        private int _publishedCommandCount;

        public NativeExecutionPassOwner(
            NativeCapacityPlan plan,
            Allocator allocator = Allocator.Persistent,
            NativeAllocationFailureInjector injector = null)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            try
            {
                _stagedInstance = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.InstanceBytes), allocator, NativeResourceKind.InstanceBytes, injector);
                _work = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.WorkItems), allocator, NativeResourceKind.WorkItems, injector);
                _scratch = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.ScratchBytes), allocator, NativeResourceKind.ScratchBytes, injector);
                _commands = NativeAllocation.Create<NativeCommandRecord>(plan.Capacity(NativeResourceKind.CommandRecords), allocator, NativeResourceKind.CommandRecords, injector);
                _commandPayload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.CommandPayloadBytes), allocator, NativeResourceKind.CommandPayloadBytes, injector);
                _completions = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.CompletionRecords), allocator, NativeResourceKind.CompletionRecords, injector);
                _completionPayload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.CompletionPayloadBytes), allocator, NativeResourceKind.CompletionPayloadBytes, injector);
                _shared = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.SharedContributionRecords), allocator, NativeResourceKind.SharedContributionRecords, injector);
                _sharedPayload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.SharedContributionPayloadBytes), allocator, NativeResourceKind.SharedContributionPayloadBytes, injector);
                _diagnostics = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.DiagnosticRecords), allocator, NativeResourceKind.DiagnosticRecords, injector);
                _diagnosticPayload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.DiagnosticPayloadBytes), allocator, NativeResourceKind.DiagnosticPayloadBytes, injector);
                _trace = NativeAllocation.Create<int>(plan.Capacity(NativeResourceKind.TraceRecords), allocator, NativeResourceKind.TraceRecords, injector);
                _tracePayload = NativeAllocation.Create<byte>(plan.Capacity(NativeResourceKind.TracePayloadBytes), allocator, NativeResourceKind.TracePayloadBytes, injector);
                _control = NativeAllocation.Create<int>(5, allocator, NativeResourceKind.DiagnosticRecords, injector);
                State = NativeOwnerState.Building;
            }
            catch
            {
                DisposeAllocations();
                State = NativeOwnerState.Uninitialized;
                throw;
            }
        }

        public NativeOwnerState State { get; private set; }
        public int PublishedCommandCount => _publishedCommandCount;
        public int CallbackCount => _control.IsCreated ? _control[ControlCallbacks] : 0;
        public NativeDiagnostic Rejection { get; private set; }

        public NativeCommandRecord GetPublishedCommand(int index)
        {
            if (State != NativeOwnerState.Completed || index < 0 || index >= _publishedCommandCount)
            {
                throw NativeProgramImageOwner.LifetimeError(0, 0, 0, "command is not published");
            }

            return _commands[index];
        }

        public void Schedule(
            NativeProgramImageOwner program,
            NativeInstanceArenaOwner instance,
            NativeInputFrameOwner input,
            NativeExecutionRequest request,
            bool injectScheduleFailure = false)
        {
            if (State != NativeOwnerState.Building)
            {
                if (State == NativeOwnerState.Scheduled)
                {
                    throw NativeProgramImageOwner.LiveJobError(instance?.OwnerId ?? 0, instance?.Generation ?? 0, 0, "pass already has a live scheduled job");
                }

                throw NativeProgramImageOwner.LifetimeError(0, 0, 0, "pass is not building");
            }

            try
            {
                if (program == null || instance == null || input == null)
                {
                    throw NativeProgramImageOwner.LifetimeError(0, 0, 0, "program, instance, and input owners are required");
                }

                instance.ValidateProgram(program);
                if (!TryPreflight(request, out var preflight))
                {
                    Rejection = preflight;
                    State = NativeOwnerState.Faulted;
                    return;
                }
            }
            catch (NativeOwnershipException exception)
            {
                Rejection = exception.Diagnostic;
                State = NativeOwnerState.Faulted;
                throw;
            }

            _program = program;
            _instance = instance;
            _input = input;
            var programAcquired = false;
            var instanceAcquired = false;
            var inputAcquired = false;
            try
            {
                _programLease = program.Acquire();
                programAcquired = true;
                _instanceLease = instance.Acquire();
                instanceAcquired = true;
                _inputLease = input.Acquire();
                inputAcquired = true;

                var job = new NativeOwnershipJob
                {
                    Program = program.Records,
                    CommittedInstance = instance.Committed,
                    Input = input.Records,
                    StagedInstance = _stagedInstance,
                    Commands = _commands,
                    Control = _control,
                    RequestedCommands = request.CommandRecords,
                    Mode = (int)request.Mode
                };
                if (injectScheduleFailure)
                {
                    throw new InvalidOperationException("injected Job.Schedule failure");
                }

                _handle = job.Schedule();
                State = NativeOwnerState.Scheduled;
            }
            catch
            {
                if (inputAcquired)
                {
                    input.Release(_inputLease);
                }

                if (instanceAcquired)
                {
                    instance.ReleaseWithoutCommit(_instanceLease);
                }

                if (programAcquired)
                {
                    program.Release(_programLease);
                }

                _programLease = default;
                _instanceLease = default;
                _inputLease = default;
                State = NativeOwnerState.Building;
                throw;
            }
        }

        public void Complete()
        {
            if (State != NativeOwnerState.Scheduled)
            {
                throw NativeProgramImageOwner.LifetimeError(0, 0, 0, "only a scheduled pass can complete");
            }

            _handle.Complete();
            var outcome = _control[ControlOutcome];
            if (outcome == (int)NativeExecutionMode.Success && _control[ControlCode] == 0)
            {
                _instance.CommitAndRelease(_instanceLease, _stagedInstance);
                _publishedCommandCount = _control[ControlPublished];
                State = NativeOwnerState.Completed;
            }
            else
            {
                _instance.ReleaseWithoutCommit(_instanceLease);
                _publishedCommandCount = 0;
                if (outcome == (int)NativeExecutionMode.Abort)
                {
                    State = NativeOwnerState.Aborted;
                }
                else
                {
                    Rejection = new NativeDiagnostic(
                        _control[ControlCode] == 0 ? NativeDiagnosticCodes.LifetimeStateInvalid : _control[ControlCode],
                        requested: (ulong)Math.Max(_control[ControlRequested], 0),
                        ownerId: _instance.OwnerId,
                        generation: _instance.Generation,
                        leaseId: _instanceLease.LeaseId);
                    State = NativeOwnerState.Faulted;
                }
            }

            _input.Release(_inputLease);
            _program.Release(_programLease);
            _programLease = default;
            _instanceLease = default;
            _inputLease = default;
        }

        public void Dispose()
        {
            if (State == NativeOwnerState.Scheduled)
            {
                throw NativeProgramImageOwner.LiveJobError(_instance?.OwnerId ?? 0, _instance?.Generation ?? 0, _instanceLease.LeaseId, "pass cannot be disposed before its final dependency completes");
            }

            if (State == NativeOwnerState.Disposed)
            {
                throw NativeProgramImageOwner.LifetimeError(0, 0, 0, "pass was disposed twice");
            }

            DisposeAllocations();
            State = NativeOwnerState.Disposed;
        }

        private bool TryPreflight(NativeExecutionRequest request, out NativeDiagnostic diagnostic)
        {
            if (request == null)
            {
                diagnostic = new NativeDiagnostic(NativeDiagnosticCodes.CapacityPlanInvalid);
                return false;
            }

            if (!TryCheckRequestCapacity(NativeResourceKind.CommandRecords, request.CommandRecords, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.CommandPayloadBytes, request.CommandPayloadBytes, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.CompletionRecords, request.CompletionRecords, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.CompletionPayloadBytes, request.CompletionPayloadBytes, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.DiagnosticRecords, request.DiagnosticRecords, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.DiagnosticPayloadBytes, request.DiagnosticPayloadBytes, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.TraceRecords, request.TraceRecords, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.TracePayloadBytes, request.TracePayloadBytes, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.WorkItems, request.WorkItems, out diagnostic) ||
                !TryCheckRequestCapacity(NativeResourceKind.ScratchBytes, request.ScratchBytes, out diagnostic))
            {
                return false;
            }

            uint sharedRecords = 0;
            uint sharedPayload = 0;
            ulong previousTreeInstanceId = 0;
            var hasPrevious = false;
            foreach (var stream in request.SharedStreams ?? Array.Empty<NativeSharedStreamReservation>())
            {
                if (stream.TreeInstanceId == 0 || (hasPrevious && stream.TreeInstanceId <= previousTreeInstanceId))
                {
                    diagnostic = new NativeDiagnostic(
                        NativeDiagnosticCodes.CapacityPlanInvalid,
                        NativeResourceKind.SharedContributionRecords);
                    return false;
                }

                if (!NativeCapacityPlan.TryCheckedAdd(sharedRecords, stream.Records, NativeResourceKind.SharedContributionRecords, out sharedRecords, out diagnostic) ||
                    !NativeCapacityPlan.TryCheckedAdd(sharedPayload, stream.PayloadBytes, NativeResourceKind.SharedContributionPayloadBytes, out sharedPayload, out diagnostic))
                {
                    return false;
                }

                previousTreeInstanceId = stream.TreeInstanceId;
                hasPrevious = true;
            }

            if (sharedRecords > _plan.Capacity(NativeResourceKind.SharedContributionRecords))
            {
                diagnostic = new NativeDiagnostic(NativeDiagnosticCodes.OutputCapacityExceeded, NativeResourceKind.SharedContributionRecords, sharedRecords, (ulong)_plan.Capacity(NativeResourceKind.SharedContributionRecords));
                return false;
            }

            if (sharedPayload > _plan.Capacity(NativeResourceKind.SharedContributionPayloadBytes))
            {
                diagnostic = new NativeDiagnostic(NativeDiagnosticCodes.OutputCapacityExceeded, NativeResourceKind.SharedContributionPayloadBytes, sharedPayload, (ulong)_plan.Capacity(NativeResourceKind.SharedContributionPayloadBytes));
                return false;
            }

            diagnostic = default;
            return true;
        }

        private bool TryCheckRequestCapacity(NativeResourceKind kind, int requested, out NativeDiagnostic diagnostic)
        {
            if (requested < 0 || requested > _plan.Capacity(kind))
            {
                diagnostic = new NativeDiagnostic(
                    NativeCapacityPlan.CapacityFailureCode(kind),
                    kind,
                    (ulong)Math.Max(requested, 0),
                    (ulong)_plan.Capacity(kind));
                return false;
            }

            diagnostic = default;
            return true;
        }

        private void DisposeAllocations()
        {
            NativeAllocation.Dispose(ref _control);
            NativeAllocation.Dispose(ref _tracePayload);
            NativeAllocation.Dispose(ref _trace);
            NativeAllocation.Dispose(ref _diagnosticPayload);
            NativeAllocation.Dispose(ref _diagnostics);
            NativeAllocation.Dispose(ref _sharedPayload);
            NativeAllocation.Dispose(ref _shared);
            NativeAllocation.Dispose(ref _completionPayload);
            NativeAllocation.Dispose(ref _completions);
            NativeAllocation.Dispose(ref _commandPayload);
            NativeAllocation.Dispose(ref _commands);
            NativeAllocation.Dispose(ref _scratch);
            NativeAllocation.Dispose(ref _work);
            NativeAllocation.Dispose(ref _stagedInstance);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct NativeOwnershipJob : IJob
    {
        [ReadOnly] public NativeArray<int> Program;
        [ReadOnly] public NativeArray<byte> CommittedInstance;
        [ReadOnly] public NativeArray<int> Input;
        public NativeArray<byte> StagedInstance;
        public NativeArray<NativeCommandRecord> Commands;
        public NativeArray<int> Control;
        public int RequestedCommands;
        public int Mode;

        public void Execute()
        {
            Control[4]++;
            Control[3] = Mode;
            if (Mode == (int)NativeExecutionMode.Abort)
            {
                return;
            }

            if (Mode == (int)NativeExecutionMode.Fault)
            {
                Control[0] = NativeDiagnosticCodes.LifetimeStateInvalid;
                return;
            }

            if (RequestedCommands < 0 || RequestedCommands > Commands.Length)
            {
                Control[0] = NativeDiagnosticCodes.OutputCapacityExceeded;
                Control[1] = RequestedCommands;
                return;
            }

            for (var index = 0; index < StagedInstance.Length; index++)
            {
                StagedInstance[index] = CommittedInstance[index];
            }

            var value = CommittedInstance[0] + Program[0] + Input[0];
            StagedInstance[0] = (byte)value;
            for (var index = 0; index < RequestedCommands; index++)
            {
                Commands[index] = new NativeCommandRecord(index, value + index);
            }

            Control[2] = RequestedCommands;
        }
    }

    public static class NativeOwnershipScenario
    {
        public static NativeCapacityPlan CreateValidPlan(int commandRecords = 2)
        {
            var requirements = NativeCapacityValues.Uniform(1)
                .With(NativeResourceKind.Alignment, 4)
                .With(NativeResourceKind.InstanceBytes, 4)
                .With(NativeResourceKind.CommandRecords, (ulong)commandRecords);
            var limits = NativeCapacityValues.Uniform(4)
                .With(NativeResourceKind.Alignment, 8)
                .With(NativeResourceKind.InstanceBytes, 4)
                .With(NativeResourceKind.CommandRecords, (ulong)commandRecords);
            if (!NativeCapacityPlan.TryCreate(requirements, limits, out var plan, out var diagnostic))
            {
                throw new NativeOwnershipException(diagnostic, "valid fixture plan was rejected");
            }

            return plan;
        }

        public static NativeExecutionRequest CreateValidRequest(int commandRecords = 2, NativeExecutionMode mode = NativeExecutionMode.Success)
        {
            return new NativeExecutionRequest
            {
                CommandRecords = commandRecords,
                CommandPayloadBytes = 1,
                CompletionRecords = 1,
                CompletionPayloadBytes = 1,
                DiagnosticRecords = 1,
                DiagnosticPayloadBytes = 1,
                TraceRecords = 1,
                TracePayloadBytes = 1,
                WorkItems = 1,
                ScratchBytes = 1,
                SharedStreams = new[]
                {
                    new NativeSharedStreamReservation(1, 1, 1),
                    new NativeSharedStreamReservation(2, 1, 1)
                },
                Mode = mode
            };
        }
    }

    public static class NativeOwnershipReleaseProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            try
            {
                var plan = NativeOwnershipScenario.CreateValidPlan();
                using (var program = new NativeProgramImageOwner(plan, 3))
                using (var instance = new NativeInstanceArenaOwner(plan, program.Binding, 10))
                using (var input = new NativeInputFrameOwner(plan, 5))
                using (var pass = new NativeExecutionPassOwner(plan))
                {
                    pass.Schedule(program, instance, input, NativeOwnershipScenario.CreateValidRequest());
                    pass.Complete();
                    if (pass.State != NativeOwnerState.Completed || instance.Value != 18 || pass.PublishedCommandCount != 2)
                    {
                        throw new InvalidOperationException("release ownership scenario produced invalid state");
                    }
                }

                if (NativeAllocationProbe.LiveAllocations != 0)
                {
                    throw new InvalidOperationException("release ownership scenario leaked native allocations");
                }

                Debug.Log("AIBT_NATIVE_OWNERSHIP_PLAYER_OK");
                Application.Quit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Application.Quit(2);
            }
        }
    }
}
