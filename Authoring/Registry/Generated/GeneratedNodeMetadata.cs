using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace AIBT.Authoring
{
    internal static class GeneratedNodeMetadata
    {
        internal const ulong CanonicalBytesEqualityContractId = 0x69e3a80e385e338eUL;
        internal const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";

        internal static ReadOnlyCollection<GeneratedStorageField> Layout(
            IEnumerable<GeneratedStorageField> source,
            out uint size,
            out byte alignment)
        {
            var values = new List<GeneratedStorageField>(source ?? throw new ArgumentNullException(nameof(source)));
            values.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.FieldId, right.FieldId));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var numeric = new Dictionary<ulong, string>();
            ulong cursor = 0;
            alignment = 1;
            for (var index = 0; index < values.Count; index++)
            {
                var field = values[index] ?? throw new ArgumentException("Storage fields cannot contain null.", nameof(source));
                if (!ids.Add(field.FieldId)) throw new ArgumentException("Generated field IDs must be unique.", nameof(source));
                if (numeric.TryGetValue(field.NumericFieldId, out var existing) && existing != field.FieldId)
                    throw new ArgumentException("Generated field numeric identities collide.", nameof(source));
                numeric[field.NumericFieldId] = field.FieldId;
                cursor = Align(cursor, field.Alignment);
                if (cursor >= uint.MaxValue || cursor + field.Size > uint.MaxValue)
                    throw new OverflowException("Generated layout exceeds the unsigned 32-bit address space.");
                field.Offset = (uint)cursor;
                cursor += field.Size;
                if (field.Alignment > alignment) alignment = field.Alignment;
            }
            cursor = Align(cursor, alignment);
            if (cursor >= uint.MaxValue) throw new OverflowException("Generated layout exceeds the unsigned 32-bit address space.");
            size = (uint)cursor;
            return values.AsReadOnly();
        }

        internal static ReadOnlyCollection<GeneratedBindingDescriptor> Bindings(IEnumerable<GeneratedBindingDescriptor> source)
        {
            var values = new List<GeneratedBindingDescriptor>(source ?? throw new ArgumentNullException(nameof(source)));
            values.Sort((left, right) => Utf8OrdinalComparer.Instance.Compare(left.BindingId, right.BindingId));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var numeric = new Dictionary<ulong, string>();
            for (var index = 0; index < values.Count; index++)
            {
                var binding = values[index] ?? throw new ArgumentException("Bindings cannot contain null.", nameof(source));
                if (!ids.Add(binding.BindingId)) throw new ArgumentException("Generated binding IDs must be unique.", nameof(source));
                if (numeric.TryGetValue(binding.NumericBindingId, out var existing) && existing != binding.BindingId)
                    throw new ArgumentException("Generated binding numeric identities collide.", nameof(source));
                numeric[binding.NumericBindingId] = binding.BindingId;
                binding.Ordinal = (uint)index;
            }
            return values.AsReadOnly();
        }

        internal static void ValidateFieldBindings(
            IReadOnlyList<GeneratedStorageField> fields,
            IReadOnlyList<GeneratedBindingDescriptor> bindings)
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < bindings.Count; index++) declared.Add(bindings[index].BindingId);
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < fields.Count; index++)
            {
                var bindingId = fields[index].BindingId;
                if (bindingId == null) continue;
                if (!declared.Contains(bindingId) || !used.Add(bindingId))
                    throw new ArgumentException("Each generated handle field requires one unique binding descriptor.");
            }
            if (used.Count != bindings.Count)
                throw new ArgumentException("Each binding descriptor requires one generated handle field.");
        }

        internal static CompiledHash HashLayout(GeneratedNodeDescriptor node, bool configuration)
        {
            var fields = configuration ? node.Configuration : node.Memory;
            var totalSize = configuration ? node.Manifest.Configuration.Size : node.Manifest.Memory.Size;
            var totalAlignment = configuration ? node.Manifest.Configuration.Alignment : node.Manifest.Memory.Alignment;
            var writer = new GeneratedByteWriter(configuration ? "AIBT-CONFIG-LAYOUT-V1\0" : "AIBT-MEMORY-LAYOUT-V1\0");
            writer.U32(1);
            writer.String(node.Manifest.TypeId);
            writer.U32(node.Manifest.Version);
            writer.U32(totalSize);
            writer.U8(totalAlignment);
            writer.U32((uint)fields.Count);
            for (var index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                writer.String(field.FieldId);
                writer.U64(field.NumericFieldId);
                writer.String(field.ValueTypeId);
                writer.U32(field.ValueTypeVersion);
                writer.Hash(field.RegisteredSchemaHash);
                writer.U32(field.Offset);
                writer.U32(field.Size);
                writer.U8(field.Alignment);
                writer.U8((byte)field.Encoding);
            }
            var padding = Padding(fields, totalSize);
            writer.U32((uint)padding.Count);
            for (var index = 0; index < padding.Count; index++)
            {
                writer.U32(padding[index].Offset);
                writer.U32(padding[index].Size);
            }
            return writer.Finish();
        }

        internal static CompiledHash HashAccess(GeneratedNodeDescriptor node)
        {
            var writer = new GeneratedByteWriter("AIBT-ACCESS-LAYOUT-V1\0");
            writer.U32(1);
            writer.String(node.Manifest.TypeId);
            writer.U32(node.Manifest.Version);
            writer.U32((uint)node.Bindings.Count);
            for (var index = 0; index < node.Bindings.Count; index++)
            {
                var binding = node.Bindings[index];
                writer.String(binding.BindingId);
                writer.U64(binding.NumericBindingId);
                writer.U8((byte)binding.Kind);
                writer.U8((byte)binding.Scope);
                writer.U8((byte)binding.PhaseCapabilities);
                writer.U32(binding.Ordinal);
                writer.U32((uint)binding.Types.Count);
                for (var typeIndex = 0; typeIndex < binding.Types.Count; typeIndex++)
                {
                    var type = binding.Types[typeIndex];
                    writer.U8((byte)type.Role);
                    writer.String(type.CanonicalTypeId);
                    writer.U64(type.NumericTypeId);
                    writer.U32(type.Version);
                    writer.Hash(type.SchemaHash);
                }
            }
            return writer.Finish();
        }

        private static List<PaddingRange> Padding(IReadOnlyList<GeneratedStorageField> fields, uint totalSize)
        {
            var physical = new List<GeneratedStorageField>(fields);
            physical.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            var result = new List<PaddingRange>();
            uint cursor = 0;
            for (var index = 0; index < physical.Count; index++)
            {
                if (physical[index].Offset > cursor) result.Add(new PaddingRange(cursor, physical[index].Offset - cursor));
                cursor = physical[index].Offset + physical[index].Size;
            }
            if (totalSize > cursor) result.Add(new PaddingRange(cursor, totalSize - cursor));
            return result;
        }

        private static ulong Align(ulong value, byte alignment)
        {
            var mask = (ulong)alignment - 1;
            return checked((value + mask) & ~mask);
        }

        private readonly struct PaddingRange
        {
            internal PaddingRange(uint offset, uint size) { Offset = offset; Size = size; }
            internal uint Offset { get; }
            internal uint Size { get; }
        }
    }

    internal sealed class GeneratedByteWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();

        internal GeneratedByteWriter(string rawDomainTag)
        {
            if (rawDomainTag != null)
            {
                var bytes = Encoding.UTF8.GetBytes(rawDomainTag);
                _stream.Write(bytes, 0, bytes.Length);
            }
        }

        internal void U8(byte value) => _stream.WriteByte(value);
        internal void U16(ushort value) { U8((byte)value); U8((byte)(value >> 8)); }
        internal void U32(uint value)
        {
            U8((byte)value); U8((byte)(value >> 8)); U8((byte)(value >> 16)); U8((byte)(value >> 24));
        }
        internal void U64(ulong value)
        {
            U32((uint)value); U32((uint)(value >> 32));
        }
        internal void String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Bytes(bytes);
        }
        internal void Bytes(byte[] bytes)
        {
            U32((uint)bytes.Length);
            _stream.Write(bytes, 0, bytes.Length);
        }
        internal void Raw(byte[] bytes) => _stream.Write(bytes, 0, bytes.Length);
        internal void Hash(string hexadecimal)
        {
            if (hexadecimal == null || hexadecimal.Length != 64) throw new ArgumentException("A canonical hash is required.", nameof(hexadecimal));
            for (var index = 0; index < hexadecimal.Length; index += 2)
                U8((byte)((Nibble(hexadecimal[index]) << 4) | Nibble(hexadecimal[index + 1])));
        }
        internal CompiledHash Finish() => new CompiledHash(StableHash.Sha256Hex(_stream.ToArray()));
        internal byte[] ToArray() => _stream.ToArray();

        private static int Nibble(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            throw new ArgumentException("Hash text must be lowercase hexadecimal.");
        }
    }
}
