namespace AIBT.Mcp.CustomTools
{
    // ToolingAndTestInput range (AIBT9000-9999), continuing McpNodeDevelopmentDiagnostics.cs's own
    // running-allocation comment: P6-009 used up through AIBT9037. This starts at the next free
    // code.
    internal static class McpCustomToolsDiagnostics
    {
        internal static readonly DiagnosticCode DuplicateToolName = new DiagnosticCode("AIBT9038");
        internal static readonly DiagnosticCode ReservedToolName = new DiagnosticCode("AIBT9039");
        internal static readonly DiagnosticCode ProviderInstantiationFailed = new DiagnosticCode("AIBT9040");
        internal static readonly DiagnosticCode UnknownCustomTool = new DiagnosticCode("AIBT9041");
        internal static readonly DiagnosticCode MalformedArguments = new DiagnosticCode("AIBT9042");
        internal static readonly DiagnosticCode ProviderInvocationFailed = new DiagnosticCode("AIBT9043");
    }
}
