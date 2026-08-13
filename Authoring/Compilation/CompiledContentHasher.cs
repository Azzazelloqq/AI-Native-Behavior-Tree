using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AIBT.Authoring
{
    internal static class CompiledContentHasher
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static CompiledHash Compute(
            uint compiledFormatVersion,
            uint executionSemanticsVersion,
            CompiledCompilerVersion compilerVersion,
            CompiledHash semanticHash,
            CompiledHash registryHash,
            CompiledHash policyHash,
            uint policyFormatVersion,
            uint rootNodeIndex,
            uint instanceNodeMemorySize,
            uint requiredMaximumAlignment,
            uint capabilityFlags,
            bool deterministicModeCompatible,
            IReadOnlyList<CompiledNodeRecord> nodes,
            IReadOnlyList<uint> childIndices,
            IReadOnlyList<uint> readSlotIndices,
            IReadOnlyList<uint> writeSlotIndices,
            IReadOnlyList<CompiledBlackboardSlotRecord> blackboardSlots,
            IReadOnlyList<CompiledObserverRecord> observers,
            IReadOnlyList<uint> watchedSlotIndices,
            byte[] configBlob,
            byte[] defaultValueBlob,
            IReadOnlyList<CompiledDebugMapEntry> debugMap)
        {
            using (var stream = new MemoryStream(4096))
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(CompiledProgramHeader.ExpectedMagic);
                writer.Write(compiledFormatVersion);
                writer.Write(executionSemanticsVersion);
                writer.Write(compilerVersion.Major);
                writer.Write(compilerVersion.Minor);
                writer.Write(compilerVersion.Patch);
                writer.Write(compilerVersion.BuildRevision);
                WriteRequiredString(writer, semanticHash.HexadecimalValue);
                WriteRequiredString(writer, registryHash.HexadecimalValue);
                WriteRequiredString(writer, policyHash.HexadecimalValue);
                writer.Write(policyFormatVersion);
                writer.Write(rootNodeIndex);
                writer.Write((uint)nodes.Count);
                writer.Write((uint)childIndices.Count);
                writer.Write((uint)blackboardSlots.Count);
                writer.Write((uint)debugMap.Count);
                writer.Write((uint)configBlob.Length);
                writer.Write(instanceNodeMemorySize);
                writer.Write(requiredMaximumAlignment);
                writer.Write(capabilityFlags);
                writer.Write(deterministicModeCompatible ? (byte)1 : (byte)0);

                WriteNodes(writer, nodes);
                WriteUInt32Table(writer, childIndices);
                WriteUInt32Table(writer, readSlotIndices);
                WriteUInt32Table(writer, writeSlotIndices);
                WriteBlackboardSlots(writer, blackboardSlots);
                WriteObservers(writer, observers);
                WriteUInt32Table(writer, watchedSlotIndices);
                WriteBlob(writer, configBlob);
                WriteBlob(writer, defaultValueBlob);
                WriteDebugMap(writer, debugMap);
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

        private static void WriteBlackboardSlots(
            BinaryWriter writer,
            IReadOnlyList<CompiledBlackboardSlotRecord> values)
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

        private static void WriteBlob(BinaryWriter writer, byte[] bytes)
        {
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
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
