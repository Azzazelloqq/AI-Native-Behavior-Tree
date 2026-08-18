using System;
using System.Collections.Generic;
using System.Globalization;

namespace AIBT
{
    public static class NativeDiagnosticProjectorV1
    {
        public static bool TryProject(
            in NativeDiagnosticRecordV1 record,
            IReadOnlyList<CompiledDebugMapEntry> debugMap,
            IReadOnlyList<NativeDiagnosticLocationV1> relatedLocations,
            out Diagnostic diagnostic)
        {
            diagnostic = null;
            try
            {
                if (!record.HasValidHeader
                    || !NativeDiagnosticContractV1.ValidateFields(record)
                    || debugMap == null
                    || relatedLocations == null
                    || (ulong)record.RelatedLocationOffset + record.RelatedLocationCount > (uint)relatedLocations.Count
                    || !TryProjectLocation(record.PrimaryLocation, debugMap, out var primary))
                {
                    return false;
                }

                var related = new DiagnosticLocation[record.RelatedLocationCount];
                for (var index = 0u; index < record.RelatedLocationCount; index++)
                {
                    if (!TryProjectLocation(
                        relatedLocations[(int)(record.RelatedLocationOffset + index)],
                        debugMap,
                        out related[(int)index]))
                    {
                        return false;
                    }
                }

                var codeText = "AIBT" + record.CodeNumber.ToString("D4", CultureInfo.InvariantCulture);
                var code = new DiagnosticCode(codeText);
                var message = ProjectMessage(record, codeText);
                diagnostic = new Diagnostic(code, record.Severity, message, primary, related);
                return true;
            }
            catch (Exception)
            {
                diagnostic = null;
                return false;
            }
        }

        private static bool TryProjectLocation(
            in NativeDiagnosticLocationV1 native,
            IReadOnlyList<CompiledDebugMapEntry> debugMap,
            out DiagnosticLocation location)
        {
            location = default;
            if (!native.IsValid)
            {
                return false;
            }

            NodeId nodeId = default;
            string documentId = null;
            if ((native.Flags & NativeDiagnosticLocationFlagsV1.DebugIdentity) != 0)
            {
                if (native.DebugIdentityIndex >= debugMap.Count)
                {
                    return false;
                }

                var debug = debugMap[(int)native.DebugIdentityIndex];
                if (debug.RuntimeNodeIndex != native.RuntimeNodeIndex)
                {
                    return false;
                }

                nodeId = debug.AuthoringNodeId;
                documentId = debug.SourcePath;
            }

            var treeInstanceId = (native.Flags & NativeDiagnosticLocationFlagsV1.TreeInstance) != 0
                ? new TreeInstanceId(native.TreeInstanceId)
                : default;
            location = new DiagnosticLocation(
                documentId: documentId,
                nodeId: nodeId,
                treeInstanceId: treeInstanceId);
            return true;
        }

        private static string ProjectMessage(in NativeDiagnosticRecordV1 record, string codeText)
        {
            switch (record.CodeNumber)
            {
                case 4301:
                    return "Native owner allocator is not Persistent (ownerKind=" + Value(record, NativeDiagnosticFieldIdV1.OwnerKind)
                        + ", allocator=" + SignedValue(record, NativeDiagnosticFieldIdV1.Allocator) + ").";
                case 4302:
                    return CapacityMessage("Native capacity plan is invalid", record, includeAlignment: true);
                case 4303:
                    return "Native capacity arithmetic overflowed (resourceKind=" + Value(record, NativeDiagnosticFieldIdV1.ResourceKind)
                        + ", operation=" + Value(record, NativeDiagnosticFieldIdV1.Operation)
                        + ", left=" + Value(record, NativeDiagnosticFieldIdV1.Left)
                        + ", right=" + Value(record, NativeDiagnosticFieldIdV1.Right) + ").";
                case 4304: return CapacityMessage("Native program capacity was exceeded", record, false);
                case 4305: return CapacityMessage("Native instance capacity was exceeded", record, false);
                case 4306: return CapacityMessage("Native snapshot capacity was exceeded", record, false);
                case 4307: return CapacityMessage("Native output capacity was exceeded", record, false);
                case 4308: return CapacityMessage("Native completion capacity was exceeded", record, false);
                case 4309: return CapacityMessage("Native diagnostic capacity was exceeded", record, false);
                case 4310:
                    return CapacityMessage("Native trace capacity was exceeded", record, false)
                        + " Dropped count=" + Value(record, NativeDiagnosticFieldIdV1.DroppedCount) + ".";
                case 4311: return LifetimeMessage("Native lifetime state is invalid", record);
                case 4312: return LifetimeMessage("Native storage has a live job ownership conflict", record);
                default:
                    return codeText + " native runtime diagnostic.";
            }
        }

        private static string CapacityMessage(
            string prefix,
            in NativeDiagnosticRecordV1 record,
            bool includeAlignment)
        {
            var message = prefix + " (resourceKind=" + Value(record, NativeDiagnosticFieldIdV1.ResourceKind)
                + ", requested=" + Value(record, NativeDiagnosticFieldIdV1.Requested)
                + ", capacity=" + Value(record, NativeDiagnosticFieldIdV1.Capacity);
            if (includeAlignment)
            {
                message += ", alignment=" + Value(record, NativeDiagnosticFieldIdV1.Alignment);
            }

            return message + ").";
        }

        private static string LifetimeMessage(string prefix, in NativeDiagnosticRecordV1 record)
            => prefix + " (ownerKind=" + Value(record, NativeDiagnosticFieldIdV1.OwnerKind)
                + ", ownerId=" + Value(record, NativeDiagnosticFieldIdV1.OwnerId)
                + ", generation=" + Value(record, NativeDiagnosticFieldIdV1.Generation)
                + ", leaseId=" + Value(record, NativeDiagnosticFieldIdV1.LeaseId)
                + ", state=" + Value(record, NativeDiagnosticFieldIdV1.OwnerState)
                + ", operation=" + Value(record, NativeDiagnosticFieldIdV1.Operation) + ").";

        private static string Value(in NativeDiagnosticRecordV1 record, NativeDiagnosticFieldIdV1 id)
        {
            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);
                if (field.FieldId == id)
                {
                    return field.Value.ToString(CultureInfo.InvariantCulture);
                }
            }

            return "0";
        }

        private static string SignedValue(in NativeDiagnosticRecordV1 record, NativeDiagnosticFieldIdV1 id)
        {
            for (var index = 0; index < record.FieldCount; index++)
            {
                var field = record.GetField(index);
                if (field.FieldId == id)
                {
                    return unchecked((long)field.Value).ToString(CultureInfo.InvariantCulture);
                }
            }

            return "0";
        }
    }
}
