using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AIBT
{
    internal static class CompiledProgramContentHashV1
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static CompiledHash Compute(CompiledProgram program)
        {
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            var header = program.Header;
            using (var stream = new MemoryStream(4096))
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(header.Magic);
                writer.Write(header.CompiledFormatVersion);
                writer.Write(header.ExecutionSemanticsVersion);
                writer.Write(header.CompilerVersion.Major);
                writer.Write(header.CompilerVersion.Minor);
                writer.Write(header.CompilerVersion.Patch);
                writer.Write(header.CompilerVersion.BuildRevision);
                WriteRequiredString(writer, header.CanonicalSemanticHash.HexadecimalValue);
                WriteRequiredString(writer, header.NodeRegistryHash.HexadecimalValue);
                WriteRequiredString(writer, header.CanonicalPolicyHash.HexadecimalValue);
                writer.Write(header.PolicyFormatVersion);
                writer.Write(header.RootNodeIndex);
                writer.Write((uint)program.Nodes.Count);
                writer.Write((uint)program.ChildIndices.Count);
                writer.Write((uint)program.BlackboardSlots.Count);
                writer.Write((uint)program.DebugMap.Count);
                writer.Write((uint)program.ConfigBlob.Count);
                writer.Write(header.InstanceNodeMemorySize);
                writer.Write(header.RequiredMaximumAlignment);
                writer.Write(header.CapabilityFlags);
                writer.Write(header.DeterministicModeCompatible ? (byte)1 : (byte)0);

                WriteNodes(writer, program.Nodes);
                WriteUInt32Table(writer, program.ChildIndices);
                WriteUInt32Table(writer, program.ReadSlotIndices);
                WriteUInt32Table(writer, program.WriteSlotIndices);
                WriteBlackboardSlots(writer, program.BlackboardSlots);
                WriteObservers(writer, program.Observers);
                WriteUInt32Table(writer, program.WatchedSlotIndices);
                WriteBlob(writer, program.ConfigBlob);
                WriteBlob(writer, program.DefaultValueBlob);
                WriteDebugMap(writer, program.DebugMap);
                writer.Flush();
                return new CompiledHash(StableHash.Sha256Hex(stream.ToArray()));
            }
        }

        private static void WriteNodes(BinaryWriter writer, IReadOnlyList<CompiledNodeRecord> values)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
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
        }

        private static void WriteBlackboardSlots(BinaryWriter writer, IReadOnlyList<CompiledBlackboardSlotRecord> values)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
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
        }

        private static void WriteObservers(BinaryWriter writer, IReadOnlyList<CompiledObserverRecord> values)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                writer.Write(value.ObserverNodeIndex);
                writer.Write(value.OwningReactiveCompositeIndex);
                writer.Write((byte)value.Mode);
                WriteRange(writer, value.WatchedSlots);
            }
        }

        private static void WriteDebugMap(BinaryWriter writer, IReadOnlyList<CompiledDebugMapEntry> values)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                writer.Write(value.RuntimeNodeIndex);
                WriteRequiredString(writer, value.AuthoringNodeId.Value);
                WriteRequiredString(writer, value.SourcePath);
                WriteOptionalString(writer, value.DisplayName);
            }
        }

        private static void WriteUInt32Table(BinaryWriter writer, IReadOnlyList<uint> values)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                writer.Write(values[index]);
            }
        }

        private static void WriteBlob(BinaryWriter writer, IReadOnlyList<byte> values)
        {
            writer.Write((uint)values.Count);
            for (var index = 0; index < values.Count; index++)
            {
                writer.Write(values[index]);
            }
        }

        private static void WriteRange(BinaryWriter writer, CompiledRange range)
        {
            writer.Write(range.Offset);
            writer.Write(range.Count);
        }

        private static void WriteRequiredString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var bytes = Utf8.GetBytes(value);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteOptionalString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(CompiledIndex.Invalid);
                return;
            }

            WriteRequiredString(writer, value);
        }
    }
}
