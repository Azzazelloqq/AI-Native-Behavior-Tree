using System.Collections.Generic;

namespace AIBT.Tests.BehaviorCases
{
    internal static class BehaviorCaseJsonDiagnosticCodes
    {
        internal static readonly DiagnosticCode InvalidUtf8 = new DiagnosticCode("AIBT9001");
        internal static readonly DiagnosticCode InvalidSyntax = new DiagnosticCode("AIBT9002");
        internal static readonly DiagnosticCode DuplicateProperty = new DiagnosticCode("AIBT9003");
        internal static readonly DiagnosticCode SchemaViolation = new DiagnosticCode("AIBT9004");
        internal static readonly DiagnosticCode UnsupportedVersion = new DiagnosticCode("AIBT9005");
        internal static readonly DiagnosticCode SemanticViolation = new DiagnosticCode("AIBT9006");
        internal static readonly DiagnosticCode UnrepresentableDocument = new DiagnosticCode("AIBT9007");
    }

    internal static class BehaviorCaseJsonDiagnostics
    {
        private static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(BehaviorCaseJsonDiagnosticCodes.InvalidUtf8),
            Descriptor(BehaviorCaseJsonDiagnosticCodes.InvalidSyntax),
            Descriptor(BehaviorCaseJsonDiagnosticCodes.DuplicateProperty),
            Descriptor(BehaviorCaseJsonDiagnosticCodes.SchemaViolation),
            Descriptor(BehaviorCaseJsonDiagnosticCodes.UnsupportedVersion),
            Descriptor(BehaviorCaseJsonDiagnosticCodes.SemanticViolation),
            Descriptor(BehaviorCaseJsonDiagnosticCodes.UnrepresentableDocument),
        });

        internal static Diagnostic Create(
            DiagnosticCode code,
            string message,
            string documentId = null,
            string pointer = null,
            int? line = null,
            int? column = null)
        {
            return Catalog.Create(code, message, new DiagnosticLocation(documentId, pointer, line, column));
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.ToolingAndTestInput,
                DiagnosticSeverity.Error,
                optionalFields: DiagnosticField.DocumentId | DiagnosticField.JsonPointer | DiagnosticField.LineAndColumn);
        }
    }
}
