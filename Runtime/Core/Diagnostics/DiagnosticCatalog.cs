using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    [Flags]
    public enum DiagnosticField
    {
        None = 0,
        TreeId = 1 << 0,
        NodeId = 1 << 1,
        TreeInstanceId = 1 << 2,
        DocumentId = 1 << 3,
        JsonPointer = 1 << 4,
        LineAndColumn = 1 << 5,
        RelatedLocations = 1 << 6,
        SuggestedOperation = 1 << 7,
    }

    public enum DiagnosticSubsystem
    {
        CoreRuntime,
        SyntaxAndSerialization,
        SemanticValidation,
        RegistryAndCompiler,
        Execution,
        ToolingAndTestInput,
    }

    public readonly struct DiagnosticCodeRange
    {
        internal DiagnosticCodeRange(int first, int last)
        {
            First = first;
            Last = last;
        }

        public int First { get; }

        public int Last { get; }

        public bool Contains(DiagnosticCode code)
        {
            if (!code.IsValid)
            {
                return false;
            }

            var number = ((code.Value[4] - '0') * 1000)
                + ((code.Value[5] - '0') * 100)
                + ((code.Value[6] - '0') * 10)
                + (code.Value[7] - '0');
            return number >= First && number <= Last;
        }
    }

    public static class DiagnosticCodeRanges
    {
        public static DiagnosticCodeRange For(DiagnosticSubsystem subsystem)
        {
            switch (subsystem)
            {
                case DiagnosticSubsystem.CoreRuntime:
                    return new DiagnosticCodeRange(1, 999);
                case DiagnosticSubsystem.SyntaxAndSerialization:
                    return new DiagnosticCodeRange(1000, 1999);
                case DiagnosticSubsystem.SemanticValidation:
                    return new DiagnosticCodeRange(2000, 2999);
                case DiagnosticSubsystem.RegistryAndCompiler:
                    return new DiagnosticCodeRange(3000, 3999);
                case DiagnosticSubsystem.Execution:
                    return new DiagnosticCodeRange(4000, 4999);
                case DiagnosticSubsystem.ToolingAndTestInput:
                    return new DiagnosticCodeRange(9000, 9999);
                default:
                    throw new ArgumentOutOfRangeException(nameof(subsystem), subsystem, null);
            }
        }
    }

    public sealed class DiagnosticDescriptor
    {
        private const DiagnosticField AllFields = DiagnosticField.TreeId
            | DiagnosticField.NodeId
            | DiagnosticField.TreeInstanceId
            | DiagnosticField.DocumentId
            | DiagnosticField.JsonPointer
            | DiagnosticField.LineAndColumn
            | DiagnosticField.RelatedLocations
            | DiagnosticField.SuggestedOperation;

        public DiagnosticDescriptor(
            DiagnosticCode code,
            DiagnosticSubsystem subsystem,
            DiagnosticSeverity defaultSeverity,
            DiagnosticField requiredFields = DiagnosticField.None,
            DiagnosticField optionalFields = DiagnosticField.None)
        {
            if (!code.IsValid)
            {
                throw new ArgumentException("A valid diagnostic code is required.", nameof(code));
            }

            if (!Enum.IsDefined(typeof(DiagnosticSeverity), defaultSeverity))
            {
                throw new ArgumentOutOfRangeException(nameof(defaultSeverity));
            }

            if (!DiagnosticCodeRanges.For(subsystem).Contains(code))
            {
                throw new ArgumentException("The diagnostic code is outside its subsystem range.", nameof(code));
            }

            if ((requiredFields & optionalFields) != 0)
            {
                throw new ArgumentException("A diagnostic field cannot be both required and optional.");
            }

            if (((requiredFields | optionalFields) & ~AllFields) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredFields),
                    "Diagnostic field masks can contain only defined contract fields.");
            }

            Code = code;
            Subsystem = subsystem;
            DefaultSeverity = defaultSeverity;
            RequiredFields = requiredFields;
            OptionalFields = optionalFields;
        }

        public DiagnosticCode Code { get; }

        public DiagnosticSubsystem Subsystem { get; }

        public DiagnosticSeverity DefaultSeverity { get; }

        public DiagnosticField RequiredFields { get; }

        public DiagnosticField OptionalFields { get; }
    }

    public sealed class DiagnosticCatalog
    {
        private readonly ReadOnlyDictionary<DiagnosticCode, DiagnosticDescriptor> _descriptors;

        public DiagnosticCatalog(IEnumerable<DiagnosticDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            var entries = new Dictionary<DiagnosticCode, DiagnosticDescriptor>();
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    throw new ArgumentException("Diagnostic catalogs cannot contain null descriptors.", nameof(descriptors));
                }

                if (entries.ContainsKey(descriptor.Code))
                {
                    throw new ArgumentException("Diagnostic codes must be unique within a catalog.", nameof(descriptors));
                }

                entries.Add(descriptor.Code, descriptor);
            }

            _descriptors = new ReadOnlyDictionary<DiagnosticCode, DiagnosticDescriptor>(entries);
        }

        public int Count => _descriptors.Count;

        public bool TryGet(DiagnosticCode code, out DiagnosticDescriptor descriptor)
        {
            return _descriptors.TryGetValue(code, out descriptor);
        }

        public Diagnostic Create(
            DiagnosticCode code,
            string message,
            DiagnosticLocation location = default,
            IEnumerable<DiagnosticLocation> relatedLocations = null)
        {
            if (!TryGet(code, out var descriptor))
            {
                throw new ArgumentException("The diagnostic code is not registered in this catalog.", nameof(code));
            }

            var diagnostic = new Diagnostic(code, descriptor.DefaultSeverity, message, location, relatedLocations);
            Validate(diagnostic, false);
            return diagnostic;
        }

        public void Validate(Diagnostic diagnostic, bool hasSuggestedOperation = false)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            if (!TryGet(diagnostic.Code, out var descriptor))
            {
                throw new ArgumentException("The diagnostic code is not registered in this catalog.", nameof(diagnostic));
            }

            var location = diagnostic.Location;
            if (location.Line.HasValue != location.Column.HasValue)
            {
                throw new ArgumentException("Catalog-backed diagnostics must provide line and column together.", nameof(diagnostic));
            }

            var presentFields = DiagnosticField.None;
            if (location.TreeId.IsValid) presentFields |= DiagnosticField.TreeId;
            if (location.NodeId.IsValid) presentFields |= DiagnosticField.NodeId;
            if (location.TreeInstanceId.IsValid) presentFields |= DiagnosticField.TreeInstanceId;
            if (location.HasDocumentId) presentFields |= DiagnosticField.DocumentId;
            if (location.HasJsonPointer) presentFields |= DiagnosticField.JsonPointer;
            if (location.Line.HasValue) presentFields |= DiagnosticField.LineAndColumn;
            if (diagnostic.RelatedLocations.Count > 0) presentFields |= DiagnosticField.RelatedLocations;
            if (hasSuggestedOperation) presentFields |= DiagnosticField.SuggestedOperation;

            var allowedFields = descriptor.RequiredFields | descriptor.OptionalFields;
            var missingFields = descriptor.RequiredFields & ~presentFields;
            var undeclaredFields = presentFields & ~allowedFields;
            if (missingFields != DiagnosticField.None)
            {
                throw new ArgumentException($"Diagnostic is missing required fields: {missingFields}.", nameof(diagnostic));
            }

            if (undeclaredFields != DiagnosticField.None)
            {
                throw new ArgumentException($"Diagnostic contains fields not declared by its catalog entry: {undeclaredFields}.", nameof(diagnostic));
            }
        }
    }
}
