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
        public static readonly DiagnosticCode InvalidGeneratedBinding = new DiagnosticCode("AIBT3018");
        public static readonly DiagnosticCode GeneratedLayoutMismatch = new DiagnosticCode("AIBT3019");
        public static readonly DiagnosticCode MissingScopeContract = new DiagnosticCode("AIBT2042");
        public static readonly DiagnosticCode ScopeContractMismatch = new DiagnosticCode("AIBT2043");
        public static readonly DiagnosticCode SharedReductionMissing = new DiagnosticCode("AIBT2044");
        public static readonly DiagnosticCode InvalidReduction = new DiagnosticCode("AIBT2045");
        public static readonly DiagnosticCode UnsupportedReduction = new DiagnosticCode("AIBT2046");
    }

    internal static class ReferenceCompilerDiagnostics
    {
        private const DiagnosticField LocationFields = DiagnosticField.DocumentId
            | DiagnosticField.JsonPointer
            | DiagnosticField.TreeId
            | DiagnosticField.NodeId
            | DiagnosticField.RelatedLocations;

        internal static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(ReferenceCompilerDiagnosticCodes.InvalidOptions),
            Descriptor(ReferenceCompilerDiagnosticCodes.PolicyHashMismatch),
            Descriptor(ReferenceCompilerDiagnosticCodes.UnsupportedCapability),
            Descriptor(ReferenceCompilerDiagnosticCodes.StableIdentityCollision),
            Descriptor(ReferenceCompilerDiagnosticCodes.LayoutOverflow),
            Descriptor(ReferenceCompilerDiagnosticCodes.ConfigurationPacking),
            Descriptor(ReferenceCompilerDiagnosticCodes.DefaultValuePacking),
            Descriptor(ReferenceCompilerDiagnosticCodes.InvalidCompiledStructure),
            Descriptor(ReferenceCompilerDiagnosticCodes.InvalidGeneratedBinding),
            Descriptor(ReferenceCompilerDiagnosticCodes.GeneratedLayoutMismatch),
            Descriptor(ReferenceCompilerDiagnosticCodes.MissingScopeContract),
            Descriptor(ReferenceCompilerDiagnosticCodes.ScopeContractMismatch),
            Descriptor(ReferenceCompilerDiagnosticCodes.SharedReductionMissing),
            Descriptor(ReferenceCompilerDiagnosticCodes.InvalidReduction),
            Descriptor(ReferenceCompilerDiagnosticCodes.UnsupportedReduction),
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
                code.Value[4] == '2' ? DiagnosticSubsystem.SemanticValidation : DiagnosticSubsystem.RegistryAndCompiler,
                DiagnosticSeverity.Error,
                optionalFields: LocationFields);
        }
    }
}
