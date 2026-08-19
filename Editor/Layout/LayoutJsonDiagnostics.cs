namespace AIBT.Editor.Layout
{
    /// <summary>editor-layout-v1.md's AIBT1101-1111 diagnostic codes for *.aibt.layout.json.</summary>
    public static class LayoutJsonDiagnosticCodes
    {
        public static readonly DiagnosticCode InvalidUtf8 = new DiagnosticCode("AIBT1101");
        public static readonly DiagnosticCode InvalidSyntax = new DiagnosticCode("AIBT1102");
        public static readonly DiagnosticCode DuplicateProperty = new DiagnosticCode("AIBT1103");
        public static readonly DiagnosticCode SchemaViolation = new DiagnosticCode("AIBT1104");
        public static readonly DiagnosticCode UnsupportedVersion = new DiagnosticCode("AIBT1105");
        public static readonly DiagnosticCode InvalidUnicode = new DiagnosticCode("AIBT1106");
        public static readonly DiagnosticCode TreeIdMismatch = new DiagnosticCode("AIBT1107");
        public static readonly DiagnosticCode UnknownNodeReference = new DiagnosticCode("AIBT1108");
        public static readonly DiagnosticCode NodeInMultipleGroups = new DiagnosticCode("AIBT1109");
        public static readonly DiagnosticCode OrphanedReroute = new DiagnosticCode("AIBT1110");
        public static readonly DiagnosticCode InvalidDirection = new DiagnosticCode("AIBT1111");
    }

    internal static class LayoutJsonDiagnostics
    {
        public static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(LayoutJsonDiagnosticCodes.InvalidUtf8),
            Descriptor(LayoutJsonDiagnosticCodes.InvalidSyntax),
            Descriptor(LayoutJsonDiagnosticCodes.DuplicateProperty),
            Descriptor(LayoutJsonDiagnosticCodes.SchemaViolation),
            Descriptor(LayoutJsonDiagnosticCodes.UnsupportedVersion),
            Descriptor(LayoutJsonDiagnosticCodes.InvalidUnicode),
            Descriptor(LayoutJsonDiagnosticCodes.TreeIdMismatch),
            Descriptor(LayoutJsonDiagnosticCodes.UnknownNodeReference),
            Descriptor(LayoutJsonDiagnosticCodes.NodeInMultipleGroups),
            Descriptor(LayoutJsonDiagnosticCodes.OrphanedReroute),
            Descriptor(LayoutJsonDiagnosticCodes.InvalidDirection),
        });

        public static Diagnostic Create(DiagnosticCode code, string message, string documentId = null, string pointer = null)
        {
            var location = new DiagnosticLocation(documentId, pointer);
            return Catalog.Create(code, message, location);
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.SyntaxAndSerialization,
                DiagnosticSeverity.Error,
                optionalFields: DiagnosticField.DocumentId | DiagnosticField.JsonPointer);
        }
    }
}
