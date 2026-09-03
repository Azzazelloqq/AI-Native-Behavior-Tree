namespace AIBT.Mcp.Migration
{
    // ToolingAndTestInput range (AIBT9000-9999), continuing McpVerificationDiagnostics.cs's own
    // running-allocation comment: verification used up through AIBT9024, later dispatchers pushed
    // it to AIBT9043. This starts at the next free code.
    internal static class McpMigrationDiagnostics
    {
        internal static readonly DiagnosticCode TreeNotFound = new DiagnosticCode("AIBT9044");
        internal static readonly DiagnosticCode MalformedArguments = new DiagnosticCode("AIBT9045");
    }
}
