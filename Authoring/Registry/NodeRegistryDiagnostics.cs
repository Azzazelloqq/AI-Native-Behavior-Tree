using System;

namespace AIBT.Authoring
{
    internal static class NodeRegistryDiagnostics
    {
        internal static readonly DiagnosticCode DuplicateCode = new DiagnosticCode("AIBT3001");
        internal static readonly DiagnosticCode IncompatibleVersionsCode = new DiagnosticCode("AIBT3002");
        internal static readonly DiagnosticCode NumericCollisionCode = new DiagnosticCode("AIBT3003");
        internal static readonly DiagnosticCode InvalidSourceCode = new DiagnosticCode("AIBT3004");
        internal static readonly DiagnosticCode InvalidBindingCode = new DiagnosticCode("AIBT3005");

        internal static DiagnosticCatalog Catalog { get; } = new DiagnosticCatalog(new[]
        {
            Descriptor(DuplicateCode),
            Descriptor(IncompatibleVersionsCode),
            Descriptor(NumericCollisionCode),
            Descriptor(InvalidSourceCode),
            Descriptor(InvalidBindingCode),
        });

        internal static Diagnostic Duplicate(string typeId, uint version)
        {
            return Error(
                DuplicateCode,
                "Duplicate node manifest '" + typeId + "' version " + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        internal static Diagnostic IncompatibleVersions(string typeId, uint firstVersion, uint secondVersion)
        {
            return Error(
                IncompatibleVersionsCode,
                "Node manifest '" + typeId + "' has incompatible active versions "
                + firstVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + " and "
                + secondVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        internal static Diagnostic NumericCollision(ulong numericTypeId, string firstCanonicalId, string secondCanonicalId)
        {
            return Error(
                NumericCollisionCode,
                "Numeric node type ID " + numericTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " collides for canonical IDs '" + firstCanonicalId + "' and '" + secondCanonicalId + "'.");
        }

        internal static Diagnostic InvalidSource(string typeId, string reason)
        {
            return Error(InvalidSourceCode, "Node manifest '" + typeId + "' has an invalid source: " + reason);
        }

        internal static Diagnostic InvalidBinding(string typeId, string reason)
        {
            return Error(InvalidBindingCode, "Node manifest '" + typeId + "' has an invalid handler binding: " + reason);
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(code, DiagnosticSubsystem.RegistryAndCompiler, DiagnosticSeverity.Error);
        }

        private static Diagnostic Error(DiagnosticCode code, string message)
        {
            if (!Catalog.TryGet(code, out var descriptor))
            {
                throw new InvalidOperationException("The registry diagnostic catalog is incomplete.");
            }

            return new Diagnostic(code, descriptor.DefaultSeverity, message);
        }
    }
}
