namespace AIBT.Mcp.Verification
{
    // ToolingAndTestInput range (AIBT9000-9999), continuing McpAuthoringDiagnostics.cs's own
    // running-allocation comment: P6-006 used up through AIBT9021. This starts at the next free
    // code.
    internal static class McpVerificationDiagnostics
    {
        internal static readonly DiagnosticCode TreeNotFound = new DiagnosticCode("AIBT9022");
        internal static readonly DiagnosticCode MalformedArguments = new DiagnosticCode("AIBT9023");
        internal static readonly DiagnosticCode UnsupportedSimulateStep = new DiagnosticCode("AIBT9024");
    }
}
