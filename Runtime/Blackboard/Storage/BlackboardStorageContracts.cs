using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    internal readonly struct ReferenceBlackboardInitialValue
    {
        private readonly byte[] _registeredBytes;

        private ReferenceBlackboardInitialValue(
            ulong stableKeyId,
            BlackboardValue builtInValue,
            ulong registeredTypeId,
            uint registeredTypeVersion,
            byte[] registeredBytes)
        {
            StableKeyId = stableKeyId;
            BuiltInValue = builtInValue;
            RegisteredTypeId = registeredTypeId;
            RegisteredTypeVersion = registeredTypeVersion;
            _registeredBytes = registeredBytes;
        }

        internal ulong StableKeyId { get; }
        internal BlackboardValue BuiltInValue { get; }
        internal ulong RegisteredTypeId { get; }
        internal uint RegisteredTypeVersion { get; }
        internal bool IsRegistered => RegisteredTypeId != 0;
        internal bool IsValid => StableKeyId != 0
            && (IsRegistered
                ? RegisteredTypeVersion != 0 && _registeredBytes != null
                : BuiltInValue.IsValid);

        internal static ReferenceBlackboardInitialValue BuiltIn(
            ulong stableKeyId,
            BlackboardValue value)
        {
            if (stableKeyId == 0) throw new ArgumentOutOfRangeException(nameof(stableKeyId));
            if (!value.IsValid) throw new ArgumentException("A valid built-in value is required.", nameof(value));
            return new ReferenceBlackboardInitialValue(stableKeyId, value, 0, 0, null);
        }

        internal static ReferenceBlackboardInitialValue Registered(
            ulong stableKeyId,
            ulong typeId,
            uint typeVersion,
            byte[] bytes)
        {
            if (stableKeyId == 0) throw new ArgumentOutOfRangeException(nameof(stableKeyId));
            if (typeId == 0) throw new ArgumentOutOfRangeException(nameof(typeId));
            if (typeVersion == 0) throw new ArgumentOutOfRangeException(nameof(typeVersion));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            return new ReferenceBlackboardInitialValue(
                stableKeyId,
                default,
                typeId,
                typeVersion,
                (byte[])bytes.Clone());
        }

        internal byte[] CopyRegisteredBytes()
            => _registeredBytes == null ? null : (byte[])_registeredBytes.Clone();
    }

    internal readonly struct ReferenceBlackboardSnapshotEntry
    {
        private readonly byte[] _registeredBytes;

        internal ReferenceBlackboardSnapshotEntry(
            uint slotIndex,
            ulong stableKeyId,
            BlackboardTypeDescriptor type,
            ulong version,
            BlackboardValue builtInValue,
            byte[] registeredBytes)
        {
            if (stableKeyId == 0) throw new ArgumentOutOfRangeException(nameof(stableKeyId));
            if (!type.IsValid) throw new ArgumentException("A valid blackboard type is required.", nameof(type));
            if (type.ValueType == BlackboardValueType.Registered)
            {
                if (registeredBytes == null || registeredBytes.Length != type.Size)
                    throw new ArgumentException("Registered snapshot bytes must match the declared type size.", nameof(registeredBytes));
                if (builtInValue.IsValid)
                    throw new ArgumentException("A registered snapshot entry cannot contain a built-in value.", nameof(builtInValue));
            }
            else
            {
                if (!builtInValue.IsValid || builtInValue.Type != type.ValueType)
                    throw new ArgumentException("A built-in snapshot value must match the declared type.", nameof(builtInValue));
                if (registeredBytes != null)
                    throw new ArgumentException("A built-in snapshot entry cannot contain registered bytes.", nameof(registeredBytes));
            }

            SlotIndex = slotIndex;
            StableKeyId = stableKeyId;
            Type = type;
            Version = version;
            BuiltInValue = builtInValue;
            _registeredBytes = registeredBytes == null ? null : (byte[])registeredBytes.Clone();
        }

        internal uint SlotIndex { get; }
        internal ulong StableKeyId { get; }
        internal BlackboardTypeDescriptor Type { get; }
        internal ulong Version { get; }
        internal BlackboardValue BuiltInValue { get; }
        internal bool IsRegistered => Type.ValueType == BlackboardValueType.Registered;
        internal byte[] CopyRegisteredBytes()
            => _registeredBytes == null ? null : (byte[])_registeredBytes.Clone();
    }

    internal sealed class ReferenceBlackboardSnapshot
    {
        private readonly ReadOnlyCollection<ReferenceBlackboardSnapshotEntry> _entries;

        internal ReferenceBlackboardSnapshot(
            ulong revision,
            IEnumerable<ReferenceBlackboardSnapshotEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            Revision = revision;
            _entries = Array.AsReadOnly(new List<ReferenceBlackboardSnapshotEntry>(entries).ToArray());
        }

        internal ulong Revision { get; }
        internal IReadOnlyList<ReferenceBlackboardSnapshotEntry> Entries => _entries;
    }

    internal readonly struct BlackboardStorageResult
    {
        internal BlackboardStorageResult(
            bool success,
            uint slotIndex,
            ulong stableKeyId,
            ulong oldVersion,
            ulong newVersion,
            Diagnostic diagnostic)
        {
            Success = success;
            SlotIndex = slotIndex;
            StableKeyId = stableKeyId;
            OldVersion = oldVersion;
            NewVersion = newVersion;
            Diagnostic = diagnostic;
        }

        internal bool Success { get; }
        internal bool Changed => Success && NewVersion != OldVersion;
        internal uint SlotIndex { get; }
        internal ulong StableKeyId { get; }
        internal ulong OldVersion { get; }
        internal ulong NewVersion { get; }
        internal Diagnostic Diagnostic { get; }
        internal static BlackboardStorageResult ForSlot(
            uint slotIndex,
            ulong stableKeyId,
            ulong oldVersion,
            ulong newVersion)
            => new BlackboardStorageResult(true, slotIndex, stableKeyId, oldVersion, newVersion, null);
        internal static BlackboardStorageResult Failed(Diagnostic diagnostic)
            => new BlackboardStorageResult(false, CompiledIndex.Invalid, 0, 0, 0, diagnostic);
    }

    internal readonly struct BlackboardSlotChange
    {
        internal BlackboardSlotChange(
            uint slotIndex,
            ulong stableKeyId,
            ulong oldVersion,
            ulong newVersion)
        {
            SlotIndex = slotIndex;
            StableKeyId = stableKeyId;
            OldVersion = oldVersion;
            NewVersion = newVersion;
        }

        internal uint SlotIndex { get; }
        internal ulong StableKeyId { get; }
        internal ulong OldVersion { get; }
        internal ulong NewVersion { get; }
    }

    internal readonly struct BlackboardResetResult
    {
        private static readonly IReadOnlyList<BlackboardSlotChange> EmptyChanges
            = Array.AsReadOnly(Array.Empty<BlackboardSlotChange>());

        internal BlackboardResetResult(bool success, BlackboardSlotChange[] changes, Diagnostic diagnostic)
        {
            Success = success;
            Changes = changes == null || changes.Length == 0
                ? EmptyChanges
                : new ReadOnlyCollection<BlackboardSlotChange>((BlackboardSlotChange[])changes.Clone());
            Diagnostic = diagnostic;
        }

        internal bool Success { get; }
        internal IReadOnlyList<BlackboardSlotChange> Changes { get; }
        internal bool Changed => Success && Changes.Count != 0;
        internal Diagnostic Diagnostic { get; }
        internal static BlackboardResetResult Unchanged => new BlackboardResetResult(true, null, null);
        internal static BlackboardResetResult Failed(Diagnostic diagnostic)
            => new BlackboardResetResult(false, null, diagnostic);
    }

    internal static class BlackboardStorageDiagnosticCodes
    {
        internal static readonly DiagnosticCode InvalidSlot = new DiagnosticCode("AIBT4201");
        internal static readonly DiagnosticCode UndeclaredAccess = new DiagnosticCode("AIBT4202");
        internal static readonly DiagnosticCode TypeMismatch = new DiagnosticCode("AIBT4203");
        internal static readonly DiagnosticCode UnsupportedScope = new DiagnosticCode("AIBT4204");
        internal static readonly DiagnosticCode InvalidValue = new DiagnosticCode("AIBT4205");
        internal static readonly DiagnosticCode VersionOverflow = new DiagnosticCode("AIBT4206");
        internal static readonly DiagnosticCode MissingTypeBinding = new DiagnosticCode("AIBT4207");
        internal static readonly DiagnosticCode RegistryMismatch = new DiagnosticCode("AIBT4208");
        internal static readonly DiagnosticCode EqualityFault = new DiagnosticCode("AIBT4209");
    }

    internal static class BlackboardStorageDiagnostics
    {
        internal static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(BlackboardStorageDiagnosticCodes.InvalidSlot),
            Descriptor(BlackboardStorageDiagnosticCodes.UndeclaredAccess),
            Descriptor(BlackboardStorageDiagnosticCodes.TypeMismatch),
            Descriptor(BlackboardStorageDiagnosticCodes.UnsupportedScope),
            Descriptor(BlackboardStorageDiagnosticCodes.InvalidValue),
            Descriptor(BlackboardStorageDiagnosticCodes.VersionOverflow),
            Descriptor(BlackboardStorageDiagnosticCodes.MissingTypeBinding),
            Descriptor(BlackboardStorageDiagnosticCodes.RegistryMismatch),
            Descriptor(BlackboardStorageDiagnosticCodes.EqualityFault),
        });

        internal static Diagnostic Create(DiagnosticCode code, string message, TreeInstanceId treeInstanceId)
        {
            return Catalog.Create(code, message, new DiagnosticLocation(treeInstanceId: treeInstanceId));
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.Execution,
                DiagnosticSeverity.Error,
                requiredFields: DiagnosticField.TreeInstanceId);
        }
    }
}
