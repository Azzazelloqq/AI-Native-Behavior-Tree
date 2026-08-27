namespace AIBT.Authoring
{
    // Diagnostic codes owned by Authoring/Discovery/. AIBT9000-9999 is the reserved
    // "tooling and test-case input" range (specifications/diagnostics-v1.md). AIBT9001-9007
    // are already used by Tests/BehaviorCases/Framework/Serialization/BehaviorCaseJsonDiagnostics.cs;
    // this starts at the next free code.
    internal static class DiscoveryDiagnosticCodes
    {
        internal static readonly DiagnosticCode MalformedPolicyDocument = new DiagnosticCode("AIBT9008");
    }
}
