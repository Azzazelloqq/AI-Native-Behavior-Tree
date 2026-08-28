using System.Collections.Generic;

namespace AIBT.Mcp
{
    /// <summary>
    /// The real enforcement path every tool dispatch goes through (ADR-P6-001): a call outside
    /// the categories granted to the current session is rejected with a structured diagnostic,
    /// never silently downgraded or silently allowed.
    /// </summary>
    public static class McpPermissionEnforcer
    {
        public static bool Require(
            ISet<McpPermissionCategory> granted,
            McpPermissionCategory required,
            out Diagnostic denialDiagnostic)
        {
            if (granted != null && granted.Contains(required))
            {
                denialDiagnostic = null;
                return true;
            }

            denialDiagnostic = new Diagnostic(
                McpDiagnostics.PermissionDenied,
                DiagnosticSeverity.Error,
                "This session is not granted the '" + required + "' permission category.");
            return false;
        }
    }
}
