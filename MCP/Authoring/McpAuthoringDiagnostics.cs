namespace AIBT.Mcp.Authoring
{
    // ToolingAndTestInput range (AIBT9000-9999), continuing McpDiagnostics.cs's own allocation
    // comment: AIBT9012/9013 are P6-005's. This card's own codes start at the next free one.
    internal static class McpAuthoringDiagnostics
    {
        internal static readonly DiagnosticCode TreeNotFound = new DiagnosticCode("AIBT9015");
        internal static readonly DiagnosticCode InvalidCreatePath = new DiagnosticCode("AIBT9016");
        internal static readonly DiagnosticCode TreeAlreadyExists = new DiagnosticCode("AIBT9017");
        internal static readonly DiagnosticCode MalformedArguments = new DiagnosticCode("AIBT9018");
        internal static readonly DiagnosticCode NodeNotFound = new DiagnosticCode("AIBT9019");
        internal static readonly DiagnosticCode WriteFailed = new DiagnosticCode("AIBT9020");

        /// <summary>
        /// This card's own concurrency precondition for semantic patches, standing in for
        /// TreeDocument.Revision (never persisted to *.aibt.json -- see
        /// McpAuthoringToolDispatcher.ApplySemanticPatchToDocument's comment). Mirrors
        /// LayoutPatchDiagnostics.HashMismatch's shape for the semantic side.
        /// </summary>
        internal static readonly DiagnosticCode ContentHashMismatch = new DiagnosticCode("AIBT9021");
    }
}
