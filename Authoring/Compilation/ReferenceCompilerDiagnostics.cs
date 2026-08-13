using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class ReferenceCompilerDiagnosticCodes
    {
        public static readonly DiagnosticCode InvalidOptions = new DiagnosticCode("AIBT3010");
        public static readonly DiagnosticCode PolicyHashMismatch = new DiagnosticCode("AIBT3011");
        public static readonly DiagnosticCode UnsupportedCapability = new DiagnosticCode("AIBT3012");
        public static readonly DiagnosticCode StableIdentityCollision = new DiagnosticCode("AIBT3013");
        public static readonly DiagnosticCode LayoutOverflow = new DiagnosticCode("AIBT3014");
        public static readonly DiagnosticCode ConfigurationPacking = new DiagnosticCode("AIBT3015");
        public static readonly DiagnosticCode DefaultValuePacking = new DiagnosticCode("AIBT3016");
        public static readonly DiagnosticCode InvalidCompiledStructure = new DiagnosticCode("AIBT3017");
    }

    internal static class ReferenceCompilerDiagnostics
    {
        private const DiagnosticField LocationFields = DiagnosticField.DocumentId
            | DiagnosticField.JsonPointer
            | DiagnosticField.TreeId
            | DiagnosticField.NodeId
            | DiagnosticField.RelatedLocations;

        private static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(ReferenceCompilerDiagnosticCodes.InvalidOptions),
            Descriptor(ReferenceCompilerDiagnosticCodes.PolicyHashMismatch),
            Descriptor(ReferenceCompilerDiagnosticCodes.UnsupportedCapability),
            Descriptor(ReferenceCompilerDiagnosticCodes.StableIdentityCollision),
            Descriptor(ReferenceCompilerDiagnosticCodes.LayoutOverflow),
            Descriptor(ReferenceCompilerDiagnosticCodes.ConfigurationPacking),
            Descriptor(ReferenceCompilerDiagnosticCodes.DefaultValuePacking),
            Descriptor(ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure),
        });

        internal static Diagnostic Create(
            DiagnosticCode code,
            string message,
            string documentId = null,
            string jsonPointer = null,
            TreeId treeId = default,
            NodeId nodeId = default,
            IEnumerable<DiagnosticLocation> relatedLocations = null)
        {
            return Catalog.Create(
                code,
                message,
                new DiagnosticLocation(documentId, jsonPointer, treeId: treeId, nodeId: nodeId),
                relatedLocations);
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.RegistryAndCompiler,
                DiagnosticSeverity.Error,
                optionalFields: LocationFields);
        }
    }
}
