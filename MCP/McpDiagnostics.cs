namespace AIBT.Mcp
{
    // ToolingAndTestInput range (AIBT9000-9999). AIBT9001-9007: BehaviorCaseJsonDiagnostics.
    // AIBT9008: P6-003's DiscoveryDiagnosticCodes. AIBT9009-9011: P6-004's
    // SemanticPatchDiagnostics/LayoutPatchDiagnostics. This starts at the next free code.
    internal static class McpDiagnostics
    {
        internal static readonly DiagnosticCode PermissionDenied = new DiagnosticCode("AIBT9012");
        internal static readonly DiagnosticCode UnknownTool = new DiagnosticCode("AIBT9013");
    }
}
