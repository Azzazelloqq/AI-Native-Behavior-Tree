using System;

namespace AIBT.Authoring
{
    public sealed class AuthoringDiagnostic : IEquatable<AuthoringDiagnostic>, IComparable<AuthoringDiagnostic>
    {
        public AuthoringDiagnostic(Diagnostic diagnostic, SuggestedDiagnosticOperation suggestedOperation = null)
        {
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
            SuggestedOperation = suggestedOperation;
        }

        public Diagnostic Diagnostic { get; }

        public SuggestedDiagnosticOperation SuggestedOperation { get; }

        public static AuthoringDiagnostic CreateValidated(
            DiagnosticCatalog catalog,
            Diagnostic diagnostic,
            SuggestedDiagnosticOperation suggestedOperation = null)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            catalog.Validate(diagnostic, suggestedOperation != null);
            return new AuthoringDiagnostic(diagnostic, suggestedOperation);
        }

        public int CompareTo(AuthoringDiagnostic other)
        {
            if (other == null)
            {
                return 1;
            }

            var result = Diagnostic.CompareTo(other.Diagnostic);
            if (result != 0)
            {
                return result;
            }

            if (SuggestedOperation == null)
            {
                return other.SuggestedOperation == null ? 0 : -1;
            }

            if (other.SuggestedOperation == null)
            {
                return 1;
            }

            return SuggestedOperation.CompareTo(other.SuggestedOperation);
        }

        public bool Equals(AuthoringDiagnostic other)
        {
            return other != null
                && Diagnostic.Equals(other.Diagnostic)
                && Equals(SuggestedOperation, other.SuggestedOperation);
        }

        public override bool Equals(object obj) => Equals(obj as AuthoringDiagnostic);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Diagnostic.GetHashCode() * 397) ^ (SuggestedOperation?.GetHashCode() ?? 0);
            }
        }
    }
}
