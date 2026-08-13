using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class BlackboardSchemaValidator
    {
        public static DiagnosticCollection Validate(IEnumerable<BlackboardKeyDefinition> keys)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            var diagnostics = new List<Diagnostic>();
            var ids = new Dictionary<string, DiagnosticLocation>(StringComparer.Ordinal);
            var namesByScope = new Dictionary<ScopedName, DiagnosticLocation>();
            foreach (var key in keys)
            {
                if (key == null)
                {
                    throw new ArgumentException("Blackboard schemas cannot contain null keys.", nameof(keys));
                }

                diagnostics.AddRange(Validate(key));
                var location = CreateLocation(key.Id);
                if (IsAuthoringId(key.Id))
                {
                    if (ids.TryGetValue(key.Id, out var originalLocation))
                    {
                        diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                            BlackboardDiagnosticCodes.InvalidKeyId,
                            "Blackboard key IDs must be unique.",
                            location,
                            new[] { originalLocation }));
                    }
                    else
                    {
                        ids.Add(key.Id, location);
                    }
                }

                if (!string.IsNullOrWhiteSpace(key.Name) && Enum.IsDefined(typeof(BlackboardScope), key.Scope))
                {
                    var scopedName = new ScopedName(key.Scope, key.Name);
                    if (namesByScope.TryGetValue(scopedName, out var originalLocation))
                    {
                        diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                            BlackboardDiagnosticCodes.InvalidKeyName,
                            "Blackboard key names must be unique within their scope.",
                            location,
                            new[] { originalLocation }));
                    }
                    else
                    {
                        namesByScope.Add(scopedName, location);
                    }
                }
            }

            return new DiagnosticCollection(diagnostics);
        }

        public static DiagnosticCollection Validate(BlackboardKeyDefinition key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var diagnostics = new List<Diagnostic>();
            var location = CreateLocation(key.Id);

            if (!IsAuthoringId(key.Id))
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidKeyId,
                    "Blackboard key ID is invalid.",
                    location));
            }

            if (string.IsNullOrWhiteSpace(key.Name))
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidKeyName,
                    "Blackboard key name is required.",
                    location));
            }

            if (!key.Type.IsValid)
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidType,
                    "Blackboard key type is invalid.",
                    location));
            }

            if (key.Scope == BlackboardScope.NodeLocal)
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.NodeLocalTreeDeclaration,
                    "NodeLocal memory cannot be declared as a tree blackboard key.",
                    location));
            }

            if (!Enum.IsDefined(typeof(BlackboardScope), key.Scope))
            {
                diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                    BlackboardDiagnosticCodes.InvalidScope,
                    "Blackboard key scope is invalid.",
                    location));
            }

            if (key.DefaultValue != null)
            {
                if (key.Type.IsValid && key.DefaultValue.ValueType != key.Type.ValueType)
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.DefaultTypeMismatch,
                        "Blackboard default type does not match the declared key type.",
                        location));
                }

                if (key.Type.IsValid
                    && key.Type.ValueType == BlackboardValueType.Enum32
                    && key.DefaultValue.ValueType == BlackboardValueType.Enum32
                    && !string.Equals(key.DefaultValue.EnumContract, key.Type.EnumContract, StringComparison.Ordinal))
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.DefaultTypeMismatch,
                        "Enum32 default contract does not match the declared key enum contract.",
                        location));
                }

                if (!key.DefaultValue.IsCanonical)
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.InvalidDefaultValue,
                        key.DefaultValue.ValueType == BlackboardValueType.Registered
                            ? "Registered default source is unvalidated until canonical schema validation in P1-006."
                            : "Blackboard default is not a valid canonical value.",
                        location));
                }

                if (key.Type.IsRegistered
                    && (key.DefaultValue.RegisteredTypeVersion != key.Type.RegisteredDescriptor.Version
                        || !string.Equals(
                            key.DefaultValue.RegisteredTypeId,
                            key.Type.CanonicalTypeId,
                            StringComparison.Ordinal)))
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.DefaultTypeMismatch,
                        "Registered blackboard default does not match the declared type ID and version.",
                        location));
                }

                if (key.Type.IsRegistered && !key.Type.RegisteredDescriptor.HasCanonicalSchema)
                {
                    diagnostics.Add(BlackboardDiagnosticCatalog.Create(
                        BlackboardDiagnosticCodes.MissingCanonicalSchema,
                        "Registered blackboard defaults require a canonical JSON schema.",
                        location));
                }
            }

            return new DiagnosticCollection(diagnostics);
        }

        private static DiagnosticLocation CreateLocation(string keyId)
        {
            if (string.IsNullOrEmpty(keyId))
            {
                return new DiagnosticLocation(jsonPointer: "/blackboard");
            }

            return new DiagnosticLocation(jsonPointer: "/blackboard/" + EscapeJsonPointer(keyId));
        }

        private static string EscapeJsonPointer(string value)
            => value.Replace("~", "~0").Replace("/", "~1");

        private static bool IsAuthoringId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsAlphaNumeric(value[0]))
            {
                return false;
            }

            for (var index = 1; index < value.Length; index++)
            {
                var character = value[index];
                if (!IsAlphaNumeric(character)
                    && character != '.'
                    && character != '_'
                    && character != ':'
                    && character != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAlphaNumeric(char value)
        {
            return value >= 'A' && value <= 'Z'
                || value >= 'a' && value <= 'z'
                || value >= '0' && value <= '9';
        }

        private readonly struct ScopedName : IEquatable<ScopedName>
        {
            public ScopedName(BlackboardScope scope, string name)
            {
                Scope = scope;
                Name = name;
            }

            public BlackboardScope Scope { get; }

            public string Name { get; }

            public bool Equals(ScopedName other)
                => Scope == other.Scope && string.Equals(Name, other.Name, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is ScopedName other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)Scope * 397) ^ StringComparer.Ordinal.GetHashCode(Name);
                }
            }
        }
    }

    public static class BlackboardDiagnosticCodes
    {
        public static readonly DiagnosticCode InvalidKeyId = new DiagnosticCode("AIBT2001");
        public static readonly DiagnosticCode InvalidKeyName = new DiagnosticCode("AIBT2002");
        public static readonly DiagnosticCode InvalidType = new DiagnosticCode("AIBT2003");
        public static readonly DiagnosticCode InvalidScope = new DiagnosticCode("AIBT2004");
        public static readonly DiagnosticCode NodeLocalTreeDeclaration = new DiagnosticCode("AIBT2005");
        public static readonly DiagnosticCode DefaultTypeMismatch = new DiagnosticCode("AIBT2006");
        public static readonly DiagnosticCode InvalidDefaultValue = new DiagnosticCode("AIBT2007");
        public static readonly DiagnosticCode MissingCanonicalSchema = new DiagnosticCode("AIBT2008");
    }

    public static class BlackboardDiagnosticCatalog
    {
        private const DiagnosticField RequiredFields = DiagnosticField.JsonPointer;
        private const DiagnosticField OptionalFields = DiagnosticField.DocumentId
            | DiagnosticField.TreeId
            | DiagnosticField.LineAndColumn
            | DiagnosticField.RelatedLocations;

        public static DiagnosticCatalog Catalog { get; } = new DiagnosticCatalog(new[]
        {
            Descriptor(BlackboardDiagnosticCodes.InvalidKeyId),
            Descriptor(BlackboardDiagnosticCodes.InvalidKeyName),
            Descriptor(BlackboardDiagnosticCodes.InvalidType),
            Descriptor(BlackboardDiagnosticCodes.InvalidScope),
            Descriptor(BlackboardDiagnosticCodes.NodeLocalTreeDeclaration),
            Descriptor(BlackboardDiagnosticCodes.DefaultTypeMismatch),
            Descriptor(BlackboardDiagnosticCodes.InvalidDefaultValue),
            Descriptor(BlackboardDiagnosticCodes.MissingCanonicalSchema),
        });

        public static Diagnostic Create(
            DiagnosticCode code,
            string message,
            DiagnosticLocation location,
            IEnumerable<DiagnosticLocation> relatedLocations = null)
        {
            if (!Catalog.TryGet(code, out var descriptor))
            {
                throw new ArgumentException("The code is not registered in the blackboard diagnostic catalog.", nameof(code));
            }

            if ((descriptor.RequiredFields & DiagnosticField.JsonPointer) != 0 && !location.HasJsonPointer)
            {
                throw new ArgumentException("A blackboard diagnostic requires a JSON Pointer location.", nameof(location));
            }

            return new Diagnostic(code, descriptor.DefaultSeverity, message, location, relatedLocations);
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.SemanticValidation,
                DiagnosticSeverity.Error,
                RequiredFields,
                OptionalFields);
        }
    }
}
