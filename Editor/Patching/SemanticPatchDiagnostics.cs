namespace AIBT.Editor.Patching
{
    // AIBT9001-9007 are BehaviorCaseJsonDiagnostics; AIBT9008 is P6-003's
    // DiscoveryDiagnosticCodes; this starts at the next free ToolingAndTestInput code.
    internal static class SemanticPatchDiagnostics
    {
        internal static readonly DiagnosticCode RevisionMismatch = new DiagnosticCode("AIBT9009");
    }
}
