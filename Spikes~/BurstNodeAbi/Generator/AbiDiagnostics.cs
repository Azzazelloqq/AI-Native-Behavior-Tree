using Microsoft.CodeAnalysis;

namespace AIBT.BurstNodeAbi.Feasibility
{
    internal static class AbiDiagnostics
    {
        private const string Category = "AIBT.BurstNodeAbi";
        internal static readonly DiagnosticDescriptor DeclarationShape = Error("AIBT5001", "Invalid Burst node declaration", "'{0}' must be a public top-level non-generic fieldless partial struct");
        internal static readonly DiagnosticDescriptor Storage = Error("AIBT5002", "Invalid Burst storage", "'{0}' has a non-allowlisted or unstable configuration/memory layout: {1}");
        internal static readonly DiagnosticDescriptor Callback = Error("AIBT5003", "Invalid Burst callback", "'{0}' requires the exact public static callback '{1}'");
        internal static readonly DiagnosticDescriptor Kind = Error("AIBT5004", "Invalid Burst node capability", "'{0}' has an unsupported kind or observer capability");
        internal static readonly DiagnosticDescriptor Duplicate = Error("AIBT5005", "Duplicate Burst node identity", "Burst node identity '{0}' version {1} is declared more than once");
        internal static readonly DiagnosticDescriptor UndeclaredAccess = Error("AIBT5006", "Undeclared or forged access", "Context operation '{0}' must use a binding field on the callback configuration parameter");
        internal static readonly DiagnosticDescriptor WrongAccess = Error("AIBT5007", "Invalid typed access", "Context operation '{0}' is incompatible with binding '{1}' or this callback phase");
        internal static readonly DiagnosticDescriptor Forbidden = Error("AIBT5008", "Forbidden Burst callback operation", "Burst callback uses forbidden operation or API '{0}'");
        internal static readonly DiagnosticDescriptor Identity = Error("AIBT5009", "Invalid identity or documentation", "'{0}' requires canonical identity, positive version, and complete documentation metadata");
        internal static readonly DiagnosticDescriptor NumericCollision = Error("AIBT5010", "Numeric identity collision", "Different canonical identities '{0}' and '{1}' produce the same FNV-1a 64 value");
        internal static readonly DiagnosticDescriptor Catalog = Error("AIBT5011", "Invalid shard or catalog set", "Catalog declaration '{0}' has invalid shard selection, authority, or global identity set: {1}");
        internal static readonly DiagnosticDescriptor Handshake = Error("AIBT5012", "Catalog handshake mismatch", "Catalog validation failed: {0}");

        private static DiagnosticDescriptor Error(string id, string title, string message)
            => new DiagnosticDescriptor(id, title, message, Category, DiagnosticSeverity.Error, true,
                customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });
    }
}
