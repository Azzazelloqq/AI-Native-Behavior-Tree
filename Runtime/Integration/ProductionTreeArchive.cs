using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AIBT
{
    // Derived asset envelope v1. Semantic program bytes retain their existing v1 hash grammar.
    internal static class ProductionTreeArchive
    {
        private const uint Magic = 0x41544250;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static byte[] Write(NativeProgramBlackboardBindingV2 binding)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic);
                writer.Write(1u);
                WriteBytes(writer, CompiledProgramContentHashV1.Serialize(binding.SemanticProgram));
                WriteBytes(writer, binding.GetOuterPreimageCopy());
                WriteList(writer, binding.Scopes, WriteScope);
                WriteList(writer, binding.Slots, WriteSlot);
                WriteList(writer, binding.SlotAuthorities, WriteAuthority);
                WriteList(writer, binding.Accesses, WriteAccess);
                WriteList(writer, binding.WatchedSlots, WriteWatched);
                WriteList(writer, binding.RegisteredTypes, WriteRegisteredType);
                WriteList(writer, binding.RegisteredFields, WriteRegisteredField);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static bool TryRead(byte[] payload, out NativeProgramBlackboardBindingV2 binding)
        {
            binding = null;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != 1u) return false;
                    var program = ReadProgram(ReadBytes(reader));
                    var candidate = new NativeProgramBlackboardBindingV2(program, ReadBytes(reader),
                        ReadList(reader, ReadScope), ReadList(reader, ReadSlot), ReadList(reader, ReadAuthority),
                        ReadList(reader, ReadAccess), ReadList(reader, ReadWatched),
                        ReadList(reader, ReadRegisteredType), ReadList(reader, ReadRegisteredField));
                    // The production host uses a legacy semantic projection plus generated scope
                    // tables, not NativeProgramImageOwnerV2's unified-slot representation.
                    if (stream.Position != stream.Length) return false;
                    binding = candidate;
                    return true;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException || exception is OverflowException)
            {
                return false;
            }
        }

        private static CompiledProgram ReadProgram(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                if (reader.ReadUInt32() != CompiledProgramHeader.ExpectedMagic) throw new InvalidDataException();
                var header = new CompiledProgramHeader(reader.ReadUInt32(), reader.ReadUInt32(),
                    new CompiledCompilerVersion(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt32()),
                    new CompiledHash(ReadString(reader)), new CompiledHash(ReadString(reader)), new CompiledHash(ReadString(reader)),
                    reader.ReadUInt32(), new CompiledHash(StableHash.Sha256Hex(bytes)), reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                    reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadByte() == 1);
                var program = new CompiledProgram(header, ReadList(reader, ReadNode), ReadList(reader, r => r.ReadUInt32()),
                    ReadList(reader, r => r.ReadUInt32()), ReadList(reader, r => r.ReadUInt32()), ReadList(reader, ReadLegacySlot),
                    ReadList(reader, ReadObserver), ReadList(reader, r => r.ReadUInt32()), ReadBytes(reader), ReadBytes(reader), ReadList(reader, ReadDebug));
                if (stream.Position != stream.Length || CompiledProgramContentHashV1.Compute(program) != header.CompiledContentHash)
                    throw new InvalidDataException("Compiled program hash mismatch.");
                return program;
            }
        }

        private static void WriteList<T>(BinaryWriter writer, IReadOnlyList<T> values, Action<BinaryWriter, T> write)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++) write(writer, values[index]);
        }

        private static T[] ReadList<T>(BinaryReader reader, Func<BinaryReader, T> read)
        {
            var values = new T[ReadCount(reader)];
            for (var index = 0; index < values.Length; index++) values[index] = read(reader);
            return values;
        }

        private static int ReadCount(BinaryReader reader)
        {
            var count = reader.ReadUInt32();
            if (count > int.MaxValue || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Compiled table exceeds the remaining payload.");
            return (int)count;
        }

        private static byte[] ReadBytes(BinaryReader reader)
        {
            var count = ReadCount(reader);
            var value = reader.ReadBytes(count);
            if (value.Length != count) throw new EndOfStreamException();
            return value;
        }

        private static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader) => Utf8.GetString(ReadBytes(reader));
        private static void WriteString(BinaryWriter writer, string value) => WriteBytes(writer, Utf8.GetBytes(value));
        private static CompiledRange ReadRange(BinaryReader reader) => new CompiledRange(reader.ReadUInt32(), reader.ReadUInt32());
        private static void WriteRange(BinaryWriter writer, CompiledRange value) { writer.Write(value.Offset); writer.Write(value.Count); }
        private static CompiledHash ReadHash(BinaryReader reader) { var text = ReadString(reader); return text.Length == 0 ? default : new CompiledHash(text); }
        private static string ReadOptionalString(BinaryReader reader)
        {
            var count = reader.ReadUInt32();
            if (count == CompiledIndex.Invalid) return null;
            if (count > int.MaxValue || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
            return Utf8.GetString(reader.ReadBytes((int)count));
        }
        private static void WriteOptionalString(BinaryWriter writer, string value)
        {
            if (value == null) writer.Write(CompiledIndex.Invalid);
            else WriteString(writer, value);
        }

        private static void WriteNode(BinaryWriter writer, CompiledNodeRecord value)
        {
            writer.Write(value.NodeTypeId);
            writer.Write(value.NodeTypeVersion);
            writer.Write(value.ConfigOffset);
            writer.Write(value.ConfigSize);
            writer.Write(value.ConfigAlignment);
            writer.Write(value.InstanceMemoryOffset);
            writer.Write(value.InstanceMemorySize);
            writer.Write(value.InstanceMemoryAlignment);
            writer.Write((byte)value.MemoryLifetime);
            WriteRange(writer, value.Children);
            writer.Write((uint)value.Flags);
            writer.Write(value.DebugIdentityIndex);
            WriteRange(writer, value.ReadSlots);
            WriteRange(writer, value.WriteSlots);
        }

        private static CompiledNodeRecord ReadNode(BinaryReader reader)
            => new CompiledNodeRecord(
                reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), (NodeMemoryLifetime)reader.ReadByte(),
                ReadRange(reader), (CompiledNodeFlags)reader.ReadUInt32(), reader.ReadUInt32(),
                ReadRange(reader), ReadRange(reader));

        private static void WriteLegacySlot(BinaryWriter writer, CompiledBlackboardSlotRecord value)
        {
            writer.Write(value.StableKeyId);
            writer.Write(value.TypeId);
            writer.Write(value.TypeVersion);
            writer.Write(value.EnumContractId);
            writer.Write((byte)value.Scope);
            writer.Write(value.Offset);
            writer.Write(value.Size);
            writer.Write(value.Alignment);
            writer.Write(value.DefaultValueOffset);
            writer.Write((byte)value.AccessFlags);
        }

        private static CompiledBlackboardSlotRecord ReadLegacySlot(BinaryReader reader)
            => new CompiledBlackboardSlotRecord(
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(),
                reader.ReadUInt64(), (BlackboardScope)reader.ReadByte(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                (CompiledBlackboardAccessFlags)reader.ReadByte());

        private static void WriteObserver(BinaryWriter writer, CompiledObserverRecord value)
        {
            writer.Write(value.ObserverNodeIndex);
            writer.Write(value.OwningReactiveCompositeIndex);
            writer.Write((byte)value.Mode);
            WriteRange(writer, value.WatchedSlots);
        }

        private static CompiledObserverRecord ReadObserver(BinaryReader reader)
            => new CompiledObserverRecord(
                reader.ReadUInt32(), reader.ReadUInt32(), (CompiledObserverMode)reader.ReadByte(),
                ReadRange(reader));

        private static void WriteSlot(BinaryWriter writer, NativeBlackboardSlotBindingV2 value)
        {
            writer.Write(value.StableKeyId);
            writer.Write(value.TypeId);
            writer.Write(value.TypeVersion);
            writer.Write(value.EnumContractId);
            writer.Write((byte)value.Scope);
            writer.Write(value.ScopeSlotIndex);
            writer.Write(value.Offset);
            writer.Write(value.Size);
            writer.Write(value.Alignment);
            writer.Write(value.DefaultOffset);
            writer.Write(value.DefaultSize);
            writer.Write((byte)value.AccessFlags);
            writer.Write(value.ScopeDescriptorIndex);
            writer.Write(value.RegisteredTypeIndex);
            writer.Write((byte)value.Reduction);
        }

        private static NativeBlackboardSlotBindingV2 ReadSlot(BinaryReader reader)
            => new NativeBlackboardSlotBindingV2(
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(),
                reader.ReadUInt64(), (BlackboardScope)reader.ReadByte(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), (CompiledBlackboardAccessFlags)reader.ReadByte(),
                reader.ReadUInt32(), reader.ReadUInt32(), (NativeBlackboardReductionKindV2)reader.ReadByte());

        private static void WriteAccess(BinaryWriter writer, NativeBlackboardAccessBindingV2 value)
        {
            writer.Write(value.NodeIndex);
            writer.Write(value.AccessOrdinal);
            writer.Write((byte)value.Scope);
            writer.Write(value.SlotIndex);
            writer.Write((byte)value.Mode);
            writer.Write(value.TypeId);
            writer.Write(value.TypeVersion);
            writer.Write(value.EnumContractId);
            writer.Write(value.RegisteredTypeIndex);
            writer.Write((byte)value.Reduction);
        }

        private static NativeBlackboardAccessBindingV2 ReadAccess(BinaryReader reader)
            => new NativeBlackboardAccessBindingV2(
                reader.ReadUInt32(), reader.ReadUInt32(), (BlackboardScope)reader.ReadByte(),
                reader.ReadUInt32(), (NativeBlackboardAccessModeV2)reader.ReadByte(), reader.ReadUInt64(),
                reader.ReadUInt32(), reader.ReadUInt64(), reader.ReadUInt32(),
                (NativeBlackboardReductionKindV2)reader.ReadByte());

        private static void WriteWatched(BinaryWriter writer, NativeBlackboardWatchedSlotBindingV2 value)
        {
            writer.Write((byte)value.Scope);
            writer.Write(value.SlotIndex);
        }

        private static NativeBlackboardWatchedSlotBindingV2 ReadWatched(BinaryReader reader)
            => new NativeBlackboardWatchedSlotBindingV2(
                (BlackboardScope)reader.ReadByte(), reader.ReadUInt32());

        private static void WriteRegisteredDescriptor(BinaryWriter writer, RegisteredUnmanagedTypeDescriptor value)
        {
            writer.Write(value.TypeId);
            writer.Write(value.Version);
            writer.Write(value.Size);
            writer.Write(value.Alignment);
            writer.Write(value.EqualityContractId);
            writer.Write(value.CanonicalSchemaId);
            writer.Write(value.MigrationSourceVersion);
            writer.Write(value.MigrationContractId);
        }

        private static RegisteredUnmanagedTypeDescriptor ReadRegisteredDescriptor(BinaryReader reader)
            => new RegisteredUnmanagedTypeDescriptor(
                reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadUInt64(), reader.ReadUInt64(),
                reader.ReadUInt32(), reader.ReadUInt64());

        private static void WriteRegisteredField(BinaryWriter writer, NativeRegisteredBlackboardFieldBindingV2 value)
        {
            writer.Write(value.FieldId);
            writer.Write(value.ValueTypeId);
            writer.Write(value.ValueTypeVersion);
            writer.Write(value.Offset);
            writer.Write(value.Size);
            writer.Write(value.Alignment);
            writer.Write((byte)value.Encoding);
            writer.Write(value.RegisteredSchemaId);
            WriteString(writer, value.RegisteredSchemaHash.HexadecimalValue ?? string.Empty);
            writer.Write(value.EqualityContractId);
        }

        private static NativeRegisteredBlackboardFieldBindingV2 ReadRegisteredField(BinaryReader reader)
            => new NativeRegisteredBlackboardFieldBindingV2(
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                (NativeBlackboardFieldEncodingV2)reader.ReadByte(), reader.ReadUInt64(), ReadHash(reader),
                reader.ReadUInt64());

        private static void WriteScope(BinaryWriter writer, NativeBlackboardScopeBindingV2 value)
        {
            writer.Write((byte)value.Scope);
            WriteString(writer, value.ContractId);
            writer.Write(value.ContractVersion);
            writer.Write(value.FirstSlot);
            writer.Write(value.SlotCount);
            WriteBytes(writer, value.GetSchemaBytesCopy());
            WriteBytes(writer, value.GetRawLayoutCopy());
        }

        private static NativeBlackboardScopeBindingV2 ReadScope(BinaryReader reader)
            => new NativeBlackboardScopeBindingV2(
                (BlackboardScope)reader.ReadByte(), ReadString(reader), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), ReadBytes(reader),
                ReadBytes(reader));

        private static void WriteAuthority(BinaryWriter writer, NativeBlackboardSlotAuthorityV2 value)
        {
            WriteString(writer, value.CanonicalKeyId);
            WriteString(writer, value.CanonicalTypeId);
            WriteString(writer, value.EnumContract);
            WriteBytes(writer, value.GetCanonicalDefaultJsonCopy());
        }

        private static NativeBlackboardSlotAuthorityV2 ReadAuthority(BinaryReader reader)
            => new NativeBlackboardSlotAuthorityV2(
                ReadString(reader), ReadString(reader), ReadString(reader),
                ReadBytes(reader));

        private static void WriteRegisteredType(BinaryWriter writer, NativeRegisteredBlackboardTypeBindingV2 value)
        {
            WriteRegisteredDescriptor(writer, value.Descriptor);
            WriteBytes(writer, value.GetSchemaPreimageCopy());
            writer.Write(value.FirstField);
            writer.Write(value.FieldCount);
        }

        private static NativeRegisteredBlackboardTypeBindingV2 ReadRegisteredType(BinaryReader reader)
            => new NativeRegisteredBlackboardTypeBindingV2(
                ReadRegisteredDescriptor(reader), ReadBytes(reader), reader.ReadUInt32(),
                reader.ReadUInt32());

        private static void WriteDebug(BinaryWriter writer, CompiledDebugMapEntry value)
        {
            writer.Write(value.RuntimeNodeIndex);
            WriteString(writer, value.AuthoringNodeId.Value);
            WriteString(writer, value.SourcePath);
            WriteOptionalString(writer, value.DisplayName);
        }

        private static CompiledDebugMapEntry ReadDebug(BinaryReader reader)
            => new CompiledDebugMapEntry(
                reader.ReadUInt32(), new NodeId(ReadString(reader)), ReadString(reader),
                ReadOptionalString(reader));
    }
}
