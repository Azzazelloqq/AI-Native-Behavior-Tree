namespace AIBT.Mcp.Testing
{
    // ToolingAndTestInput range (AIBT9000-9999), continuing McpVerificationDiagnostics.cs's own
    // running-allocation comment: P6-007 used up through AIBT9024. This starts at the next free
    // code.
    internal static class McpTestingDiagnostics
    {
        internal static readonly DiagnosticCode CaseNotFound = new DiagnosticCode("AIBT9025");
        internal static readonly DiagnosticCode MalformedArguments = new DiagnosticCode("AIBT9026");
        internal static readonly DiagnosticCode UnknownScenario = new DiagnosticCode("AIBT9027");
        internal static readonly DiagnosticCode ScenarioNotImplemented = new DiagnosticCode("AIBT9028");
        internal static readonly DiagnosticCode UnknownPolicy = new DiagnosticCode("AIBT9029");
    }
}
