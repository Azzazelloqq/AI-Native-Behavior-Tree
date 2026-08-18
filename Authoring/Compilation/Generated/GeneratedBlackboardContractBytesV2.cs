using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace AIBT.Authoring
{
    internal static class GeneratedBlackboardContractBytesV2
    {
        internal const string RegisteredSchemaDomain = "AIBT-VALUE-SCHEMA-V1\0";

        internal static byte[] RegisteredSchema(RegisteredBlackboardTypeCatalogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            var writer = new GeneratedByteWriter(RegisteredSchemaDomain);
            writer.U32(1);
            writer.String(entry.CanonicalTypeId);
            writer.U64(StableHash.Fnv1A64(entry.CanonicalTypeId));
            writer.U32(entry.Version);
            writer.String(entry.CanonicalSchemaId);
            writer.U64(StableHash.Fnv1A64(entry.CanonicalSchemaId));
            writer.U32((uint)entry.Descriptor.Size);
            writer.U8((byte)entry.Descriptor.Alignment);
            writer.U32((uint)entry.Fields.Count);
            for (var index = 0; index < entry.Fields.Count; index++)
            {
                var field = entry.Fields[index];
                writer.String(field.FieldId);
                writer.U64(field.NumericFieldId);
                writer.String(field.ValueTypeId);
                writer.U64(StableHash.Fnv1A64(field.ValueTypeId));
                writer.U32(field.ValueTypeVersion);
                writer.Hash(field.RegisteredSchemaHash);
                writer.U32(field.Offset);
                writer.U32(field.Size);
                writer.U8(field.Alignment);
                writer.U8((byte)field.Encoding);
            }
            return writer.ToArray();
        }

        internal static byte[] Schema(
            BlackboardScope scope,
            BlackboardScopeContract contract,
            IList<GeneratedScopeSlot> slots)
        {
            var writer = new GeneratedByteWriter(null);
            writer.String("aibt.blackboard-scope");
            writer.U32(1);
            writer.U8(scope == BlackboardScope.Agent ? (byte)1 : (byte)2);
            writer.String(contract.ContractId);
            writer.U32(contract.ContractVersion);
            writer.U32((uint)slots.Count);
            for (var index = 0; index < slots.Count; index++)
            {
                var key = slots[index].Key;
                writer.String(key.Id);
                writer.String(key.Type.CanonicalTypeId);
                writer.U32(key.Type.RuntimeDescriptor.Version);
                writer.String(key.Type.EnumContract ?? string.Empty);
                writer.Bytes(CanonicalDefault(key));
                writer.U8((byte)key.Reduction);
            }
            return writer.ToArray();
        }

        internal static byte[] Layout(
            BlackboardScope scope,
            BlackboardScopeContract contract,
            CompiledHash schemaHash,
            IList<GeneratedScopeSlot> slots)
        {
            var writer = new GeneratedByteWriter(null);
            writer.String("aibt.blackboard-layout");
            writer.U32(1);
            writer.U8(scope == BlackboardScope.Agent ? (byte)1 : (byte)2);
            writer.String(contract.ContractId);
            writer.U32(contract.ContractVersion);
            writer.Hash(schemaHash.HexadecimalValue);
            writer.U32((uint)slots.Count);
            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                var key = slot.Key;
                var layout = key.Type.RuntimeDescriptor;
                writer.String(key.Id);
                writer.U32(slot.SlotIndex);
                writer.U64(layout.TypeId);
                writer.U32(layout.Version);
                writer.U64(key.Type.EnumContractId);
                writer.U32(slot.Offset);
                writer.U32((uint)layout.Size);
                writer.U32((uint)layout.Alignment);
                writer.Bytes(slot.DefaultBytes ?? Array.Empty<byte>());
                writer.U8((byte)key.Reduction);
            }
            return writer.ToArray();
        }

        internal static byte[] CanonicalDefault(BlackboardKeyDefinition key)
        {
            if (key.DefaultValue == null || !key.DefaultValue.TryGetRuntimeValue(out var value))
                return Array.Empty<byte>();
            string text;
            switch (value.Type)
            {
                case BlackboardValueType.Bool: value.TryGetBool(out var boolean); text = boolean ? "true" : "false"; break;
                case BlackboardValueType.Int32: value.TryGetInt32(out var int32); text = int32.ToString(CultureInfo.InvariantCulture); break;
                case BlackboardValueType.Int64: value.TryGetInt64(out var int64); text = int64.ToString(CultureInfo.InvariantCulture); break;
                case BlackboardValueType.Float32: value.TryGetFloat32(out var float32); text = CanonicalJsonNumber.Format(float32); break;
                case BlackboardValueType.Float64: value.TryGetFloat64(out var float64); text = CanonicalJsonNumber.Format(float64); break;
                case BlackboardValueType.Float2: value.TryGetFloat2(out var float2); text = "{\"x\":" + CanonicalJsonNumber.Format(float2.X) + ",\"y\":" + CanonicalJsonNumber.Format(float2.Y) + "}"; break;
                case BlackboardValueType.Float3: value.TryGetFloat3(out var float3); text = "{\"x\":" + CanonicalJsonNumber.Format(float3.X) + ",\"y\":" + CanonicalJsonNumber.Format(float3.Y) + ",\"z\":" + CanonicalJsonNumber.Format(float3.Z) + "}"; break;
                case BlackboardValueType.Quaternion: value.TryGetQuaternion(out var q); text = "{\"x\":" + CanonicalJsonNumber.Format(q.X) + ",\"y\":" + CanonicalJsonNumber.Format(q.Y) + ",\"z\":" + CanonicalJsonNumber.Format(q.Z) + ",\"w\":" + CanonicalJsonNumber.Format(q.W) + "}"; break;
                case BlackboardValueType.Enum32: value.TryGetEnum32(out var enum32); text = "{\"contract\":" + JsonConvert.ToString(key.Type.EnumContract) + ",\"value\":" + enum32.Value.ToString(CultureInfo.InvariantCulture) + "}"; break;
                case BlackboardValueType.FixedString32:
                case BlackboardValueType.FixedString64:
                case BlackboardValueType.FixedString128:
                case BlackboardValueType.FixedString512: value.TryGetFixedString(out var fixedString); text = JsonConvert.ToString(fixedString); break;
                case BlackboardValueType.AgentId: value.TryGetAgentId(out var agent); text = JsonConvert.ToString(agent.ToString()); break;
                case BlackboardValueType.EntityId: value.TryGetEntityId(out var entity); text = JsonConvert.ToString(entity.ToString()); break;
                case BlackboardValueType.OperationId: value.TryGetOperationId(out var operation); text = JsonConvert.ToString(operation.ToString()); break;
                case BlackboardValueType.AssetId: value.TryGetAssetId(out var asset); text = asset.HasLocalFileId ? "{\"guid\":" + JsonConvert.ToString(asset.ToGuidString()) + ",\"localFileId\":" + asset.LocalFileId.ToString(CultureInfo.InvariantCulture) + "}" : JsonConvert.ToString(asset.ToGuidString()); break;
                default: return Array.Empty<byte>();
            }
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
