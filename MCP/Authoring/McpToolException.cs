using System;

namespace AIBT.Mcp.Authoring
{
    /// <summary>
    /// A structured, tool-level failure (tree not found, malformed arguments, invalid create-tree
    /// path, ...) that <see cref="McpToolDispatcher"/> converts into the standard
    /// <c>{"error":{"code","message"}}</c> envelope -- distinct from the domain-patch
    /// accept/reject outcome (a rejected patch is still a successful call, just
    /// <c>accepted: false</c>).
    /// </summary>
    internal sealed class McpToolException : Exception
    {
        public McpToolException(DiagnosticCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public DiagnosticCode Code { get; }
    }
}
