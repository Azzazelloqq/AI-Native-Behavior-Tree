namespace AIBT.Mcp
{
    // Verbatim from ADR-P6-001's permission-model taxonomy (Documentation~/ai-and-mcp.md's
    // "Safe mutation protocol" list). No category implies another.
    public enum McpPermissionCategory
    {
        Read,
        SemanticEdit,
        LayoutEdit,
        CodeGeneration,
        Compilation,
        TestExecution,
        BenchmarkExecution,
        ArbitraryProjectIntegration,
    }
}
