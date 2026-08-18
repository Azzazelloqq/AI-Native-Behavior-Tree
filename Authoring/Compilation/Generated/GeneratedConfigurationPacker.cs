using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public sealed class GeneratedConfigurationPackResult
    {
        internal GeneratedConfigurationPackResult(byte[] bytes, DiagnosticCollection diagnostics)
        {
            Bytes = bytes;
            Diagnostics = diagnostics;
        }

        public byte[] Bytes { get; }
        public DiagnosticCollection Diagnostics { get; }
        public bool Success => Bytes != null && Diagnostics.Count == 0;
    }

    public static class GeneratedConfigurationPacker
    {
        public static GeneratedConfigurationPackResult Pack(
            GeneratedNodeDescriptor descriptor,
            SemanticObject parameters,
            IReadOnlyDictionary<string, uint> accessOrdinals,
            string documentId = null,
            NodeId nodeId = default)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            parameters = parameters ?? SemanticObject.Empty;
            accessOrdinals = accessOrdinals ?? new Dictionary<string, uint>();
            var diagnostics = new List<Diagnostic>();
            var bytes = new byte[descriptor.Manifest.Configuration.Size];
            var expectedParameters = new HashSet<string>(StringComparer.Ordinal);
            var expectedBindings = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < descriptor.Configuration.Count; index++)
            {
                var field = descriptor.Configuration[index];
                if (field.Encoding == GeneratedFieldEncoding.GeneratedHandle)
                {
                    expectedBindings.Add(field.BindingId);
                    if (!accessOrdinals.TryGetValue(field.BindingId, out var ordinal) || ordinal == CompiledIndex.Invalid)
                    {
                        Add(diagnostics, "Missing or invalid generated access ordinal '" + field.BindingId + "'.", documentId, nodeId);
                        continue;
                    }
                    WriteU32(bytes, field.Offset, ordinal);
                    continue;
                }

                expectedParameters.Add(field.FieldId);
                if (!parameters.TryGetValue(field.FieldId, out var value) || value == null)
                {
                    Add(diagnostics, "Missing generated configuration parameter '" + field.FieldId + "'.", documentId, nodeId);
                    continue;
                }
                if (!WriteScalar(bytes, field, value))
                    Add(diagnostics, "Parameter '" + field.FieldId + "' does not match its generated scalar encoding.", documentId, nodeId);
            }

            var seenParameters = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < parameters.Properties.Count; index++)
            {
                var property = parameters.Properties[index];
                if (property == null) continue;
                if (!expectedParameters.Contains(property.Name))
                    Add(diagnostics, "Unknown generated configuration parameter '" + property.Name + "'.", documentId, nodeId);
                else if (!seenParameters.Add(property.Name))
                    Add(diagnostics, "Duplicate generated configuration parameter '" + property.Name + "'.", documentId, nodeId);
            }
            foreach (var ordinal in accessOrdinals)
                if (!expectedBindings.Contains(ordinal.Key))
                    Add(diagnostics, "Unknown generated access ordinal '" + ordinal.Key + "'.", documentId, nodeId);

            var collection = new DiagnosticCollection(diagnostics);
            return new GeneratedConfigurationPackResult(collection.Count == 0 ? bytes : null, collection);
        }

        private static bool WriteScalar(byte[] bytes, GeneratedStorageField field, SemanticValue value)
        {
            switch (field.Encoding)
            {
                case GeneratedFieldEncoding.Bool8:
                    if (field.Size != 1 || !value.TryGetBoolean(out var boolean)) return false;
                    bytes[(int)field.Offset] = boolean ? (byte)1 : (byte)0;
                    return true;
                case GeneratedFieldEncoding.UInt32LE:
                    if (field.Size != 4 || !TryUnsigned(value, uint.MaxValue, out var uint32)) return false;
                    WriteU32(bytes, field.Offset, (uint)uint32);
                    return true;
                case GeneratedFieldEncoding.UInt64LE:
                    if (field.Size != 8 || !TryUnsigned(value, ulong.MaxValue, out var uint64)) return false;
                    WriteU64(bytes, field.Offset, uint64);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryUnsigned(SemanticValue value, ulong maximum, out ulong result)
        {
            if (value.TryGetUInt64(out result)) return result <= maximum;
            if (value.TryGetInt64(out var signed) && signed >= 0)
            {
                result = (ulong)signed;
                return result <= maximum;
            }
            result = 0;
            return false;
        }

        private static void WriteU32(byte[] bytes, uint offset, uint value)
        {
            bytes[(int)offset] = (byte)value;
            bytes[(int)offset + 1] = (byte)(value >> 8);
            bytes[(int)offset + 2] = (byte)(value >> 16);
            bytes[(int)offset + 3] = (byte)(value >> 24);
        }

        private static void WriteU64(byte[] bytes, uint offset, ulong value)
        {
            WriteU32(bytes, offset, (uint)value);
            WriteU32(bytes, offset + 4, (uint)(value >> 32));
        }

        private static void Add(List<Diagnostic> diagnostics, string message, string documentId, NodeId nodeId)
        {
            diagnostics.Add(ReferenceCompilerDiagnostics.Create(
                ReferenceCompilerDiagnosticCodes.ConfigurationPacking,
                message,
                documentId,
                "/nodes/" + Escape(nodeId.Value) + "/parameters",
                nodeId: nodeId));
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("~", "~0").Replace("/", "~1");
    }
}
