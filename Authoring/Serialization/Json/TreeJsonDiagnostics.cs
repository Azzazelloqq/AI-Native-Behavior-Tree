using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class TreeJsonDiagnosticCodes
    {
        public static readonly DiagnosticCode InvalidUtf8 = new DiagnosticCode("AIBT1001");
        public static readonly DiagnosticCode InvalidSyntax = new DiagnosticCode("AIBT1002");
        public static readonly DiagnosticCode DuplicateProperty = new DiagnosticCode("AIBT1003");
        public static readonly DiagnosticCode SchemaViolation = new DiagnosticCode("AIBT1004");
        public static readonly DiagnosticCode UnsupportedVersion = new DiagnosticCode("AIBT1005");
        public static readonly DiagnosticCode InvalidUnicode = new DiagnosticCode("AIBT1006");
        public static readonly DiagnosticCode UnrepresentableDocument = new DiagnosticCode("AIBT1007");
        public static readonly DiagnosticCode MissingRegisteredSchema = new DiagnosticCode("AIBT1008");
    }

    internal static class TreeJsonDiagnostics
    {
        public static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(TreeJsonDiagnosticCodes.InvalidUtf8),
            Descriptor(TreeJsonDiagnosticCodes.InvalidSyntax),
            Descriptor(TreeJsonDiagnosticCodes.DuplicateProperty),
            Descriptor(TreeJsonDiagnosticCodes.SchemaViolation),
            Descriptor(TreeJsonDiagnosticCodes.UnsupportedVersion),
            Descriptor(TreeJsonDiagnosticCodes.InvalidUnicode),
            Descriptor(TreeJsonDiagnosticCodes.UnrepresentableDocument),
            Descriptor(TreeJsonDiagnosticCodes.MissingRegisteredSchema),
        });

        public static Diagnostic Create(
            DiagnosticCode code,
            string message,
            string documentId = null,
            string pointer = null,
            int? line = null,
            int? column = null)
        {
            var location = new DiagnosticLocation(documentId, pointer, line, column);
            return Catalog.Create(code, message, location);
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.SyntaxAndSerialization,
                DiagnosticSeverity.Error,
                optionalFields: DiagnosticField.DocumentId | DiagnosticField.JsonPointer | DiagnosticField.LineAndColumn);
        }
    }
}
