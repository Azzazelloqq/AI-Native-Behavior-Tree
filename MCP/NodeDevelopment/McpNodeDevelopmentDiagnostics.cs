namespace AIBT.Mcp.NodeDevelopment
{
    // ToolingAndTestInput range (AIBT9000-9999), continuing McpTestingDiagnostics.cs's own
    // running-allocation comment: P6-008 used up through AIBT9029. This starts at the next free
    // code.
    internal static class McpNodeDevelopmentDiagnostics
    {
        internal static readonly DiagnosticCode MalformedArguments = new DiagnosticCode("AIBT9030");
        internal static readonly DiagnosticCode UnknownNodeKind = new DiagnosticCode("AIBT9031");
        internal static readonly DiagnosticCode NoPendingGeneration = new DiagnosticCode("AIBT9032");
        internal static readonly DiagnosticCode CompileNotObserved = new DiagnosticCode("AIBT9033");
        internal static readonly DiagnosticCode CompileFailed = new DiagnosticCode("AIBT9034");
        internal static readonly DiagnosticCode TestFailed = new DiagnosticCode("AIBT9035");
        internal static readonly DiagnosticCode ApplyDestinationExists = new DiagnosticCode("AIBT9036");
        internal static readonly DiagnosticCode ShardNotFound = new DiagnosticCode("AIBT9037");
    }
}
