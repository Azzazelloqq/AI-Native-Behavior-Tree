using System;

namespace AIBT.Editor.Validation
{
    public enum DiagnosticGraphLocationKind
    {
        /// <summary>No specific node owns this diagnostic (e.g. a document-wide policy check).</summary>
        Document,
        Node,
        Field,
    }

    /// <summary>
    /// Classifies one <see cref="Diagnostic"/> to a stable graph location, per this card's
    /// acceptance criterion that every diagnostic code renders with a location, not just a raw
    /// code/message dump. Every AIBT.Authoring.TreeValidator diagnostic that concerns a node sets
    /// Location.NodeId directly (confirmed by inspection of TreeValidator.Location/Create), so
    /// resolution never needs to parse the tree structure itself -- only the diagnostic's own
    /// Location.
    /// </summary>
    public sealed class DiagnosticGraphLocation
    {
        private const string ParameterPointerMarker = "/parameters/";

        private DiagnosticGraphLocation(DiagnosticGraphLocationKind kind, NodeId nodeId, string fieldName, Diagnostic diagnostic)
        {
            Kind = kind;
            NodeId = nodeId;
            FieldName = fieldName;
            Diagnostic = diagnostic;
        }

        public DiagnosticGraphLocationKind Kind { get; }

        /// <summary>Valid only when <see cref="Kind"/> is <see cref="DiagnosticGraphLocationKind.Node"/> or <see cref="DiagnosticGraphLocationKind.Field"/>.</summary>
        public NodeId NodeId { get; }

        /// <summary>The parameter name, set only when <see cref="Kind"/> is <see cref="DiagnosticGraphLocationKind.Field"/>.</summary>
        public string FieldName { get; }

        public Diagnostic Diagnostic { get; }

        public static DiagnosticGraphLocation Resolve(Diagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            if (!diagnostic.Location.NodeId.IsValid)
            {
                return new DiagnosticGraphLocation(DiagnosticGraphLocationKind.Document, default, null, diagnostic);
            }

            var fieldName = TryExtractParameterName(diagnostic.Location.JsonPointer);
            return fieldName != null
                ? new DiagnosticGraphLocation(DiagnosticGraphLocationKind.Field, diagnostic.Location.NodeId, fieldName, diagnostic)
                : new DiagnosticGraphLocation(DiagnosticGraphLocationKind.Node, diagnostic.Location.NodeId, null, diagnostic);
        }

        private static string TryExtractParameterName(string pointer)
        {
            if (string.IsNullOrEmpty(pointer))
            {
                return null;
            }

            var markerIndex = pointer.IndexOf(ParameterPointerMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return null;
            }

            var name = pointer.Substring(markerIndex + ParameterPointerMarker.Length);
            var nextSlash = name.IndexOf('/');
            if (nextSlash >= 0)
            {
                name = name.Substring(0, nextSlash);
            }

            // RFC 6901 unescaping: authoring identities/parameter names cannot contain '/' or '~'
            // (identity-and-hashing-v1.md's grammar), so this is defensive, not load-bearing.
            return name.Length == 0 ? null : name.Replace("~1", "/").Replace("~0", "~");
        }
    }
}
