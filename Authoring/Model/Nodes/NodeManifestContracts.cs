using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    public enum NodeExecutionDomain : byte
    {
        Burst,
        Managed,
        MainThread,
    }

    public enum NodeBehaviorKind : byte
    {
        Composite,
        Decorator,
        Condition,
        Action,
    }

    public enum NodeCancellationMode : byte
    {
        NotApplicable,
        AbortOnly,
        Command,
    }

    public enum NodeCostHint : byte
    {
        Trivial,
        Low,
        Medium,
        High,
        Variable,
    }

    public enum NodeParameterType : byte
    {
        Boolean,
        UInt32,
        UInt64,
        StringEnum,
    }

    public enum NodeAccessMode : byte
    {
        Read,
        Write,
    }

    public sealed class NodeChildPolicy
    {
        public NodeChildPolicy(uint minimum, uint? maximum, bool ordered)
        {
            if (maximum.HasValue && maximum.Value < minimum)
            {
                throw new ArgumentException("The maximum child count cannot be smaller than the minimum.", nameof(maximum));
            }

            Minimum = minimum;
            Maximum = maximum;
            Ordered = ordered;
        }

        public uint Minimum { get; }

        public uint? Maximum { get; }

        public bool Ordered { get; }
    }

    public sealed class NodeMemoryDescriptor
    {
        public NodeMemoryDescriptor(uint size, byte alignment, NodeMemoryLifetime lifetime)
        {
            ValidateAlignment(alignment, nameof(alignment));
            if (!Enum.IsDefined(typeof(NodeMemoryLifetime), lifetime))
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }

            if (size % alignment != 0)
            {
                throw new ArgumentException("Memory size must be a multiple of its alignment.", nameof(size));
            }

            Size = size;
            Alignment = alignment;
            Lifetime = lifetime;
        }

        public uint Size { get; }

        public byte Alignment { get; }

        public NodeMemoryLifetime Lifetime { get; }

        internal static void ValidateAlignment(byte alignment, string parameterName)
        {
            if (alignment != 1 && alignment != 2 && alignment != 4 && alignment != 8 && alignment != 16)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Alignment must be 1, 2, 4, 8, or 16 bytes.");
            }
        }
    }

    public sealed class NodeConfigurationField
    {
        public NodeConfigurationField(string parameterName, uint offset, uint size, byte alignment)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                throw new ArgumentException("A configuration field requires a parameter name.", nameof(parameterName));
            }

            if (size == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "A configuration field must occupy at least one byte.");
            }

            NodeMemoryDescriptor.ValidateAlignment(alignment, nameof(alignment));
            if (size % alignment != 0)
            {
                throw new ArgumentException("Configuration size must be a multiple of its alignment.", nameof(size));
            }
            if (offset % alignment != 0)
            {
                throw new ArgumentException("The configuration field offset must satisfy its alignment.", nameof(offset));
            }

            ParameterName = parameterName;
            Offset = offset;
            Size = size;
            Alignment = alignment;
        }

        public string ParameterName { get; }

        public uint Offset { get; }

        public uint Size { get; }

        public byte Alignment { get; }
    }

    public sealed class NodeConfigurationDescriptor
    {
        private readonly ReadOnlyCollection<NodeConfigurationField> _fields;

        public NodeConfigurationDescriptor(uint size, byte alignment, IEnumerable<NodeConfigurationField> fields)
        {
            NodeMemoryDescriptor.ValidateAlignment(alignment, nameof(alignment));
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            var values = new List<NodeConfigurationField>(fields);
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] == null)
                {
                    throw new ArgumentException("Configuration fields cannot contain null.", nameof(fields));
                }
            }

            values.Sort((left, right) => left.Offset != right.Offset
                ? left.Offset.CompareTo(right.Offset)
                : string.Compare(left.ParameterName, right.ParameterName, StringComparison.Ordinal));

            var names = new HashSet<string>(StringComparer.Ordinal);
            ulong previousEnd = 0;
            for (var index = 0; index < values.Count; index++)
            {
                var field = values[index];
                if (field.Alignment > alignment)
                {
                    throw new ArgumentException("A field cannot require greater alignment than its configuration descriptor.", nameof(fields));
                }
                if (!names.Add(field.ParameterName))
                {
                    throw new ArgumentException("Configuration field names must be unique.", nameof(fields));
                }

                var end = (ulong)field.Offset + field.Size;
                if (end > size || (index > 0 && field.Offset < previousEnd))
                {
                    throw new ArgumentException("Configuration fields must be non-overlapping and within the descriptor size.", nameof(fields));
                }

                previousEnd = end;
            }

            Size = size;
            Alignment = alignment;
            _fields = values.AsReadOnly();
        }

        public uint Size { get; }

        public byte Alignment { get; }

        public IReadOnlyList<NodeConfigurationField> Fields => _fields;
    }

    public sealed class NodeParameterCondition
    {
        public NodeParameterCondition(string parameterName, string requiredValue)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                throw new ArgumentException("A parameter condition requires a parameter name.", nameof(parameterName));
            }

            if (string.IsNullOrWhiteSpace(requiredValue))
            {
                throw new ArgumentException("A parameter condition requires a value.", nameof(requiredValue));
            }

            ParameterName = parameterName;
            RequiredValue = requiredValue;
        }

        public string ParameterName { get; }

        public string RequiredValue { get; }
    }

    public sealed class NodeParameterContract
    {
        private readonly ReadOnlyCollection<string> _allowedValues;

        public NodeParameterContract(
            string name,
            NodeParameterType type,
            bool required,
            ulong? minimum = null,
            IEnumerable<string> allowedValues = null,
            NodeParameterCondition requiredWhen = null,
            NodeParameterCondition forbiddenUnless = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A parameter requires a name.", nameof(name));
            }

            if (!Enum.IsDefined(typeof(NodeParameterType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            if (minimum.HasValue && type != NodeParameterType.UInt32 && type != NodeParameterType.UInt64)
            {
                throw new ArgumentException("Only unsigned integer parameters can declare a minimum.", nameof(minimum));
            }

            var values = allowedValues == null ? new List<string>() : new List<string>(allowedValues);
            values.Sort(StringComparer.Ordinal);
            for (var index = 0; index < values.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(values[index]) || (index > 0 && values[index] == values[index - 1]))
                {
                    throw new ArgumentException("Allowed values must be non-empty and unique.", nameof(allowedValues));
                }
            }

            if (type == NodeParameterType.StringEnum && values.Count == 0)
            {
                throw new ArgumentException("A string-enum parameter requires at least one allowed value.", nameof(allowedValues));
            }

            if (type != NodeParameterType.StringEnum && values.Count != 0)
            {
                throw new ArgumentException("Allowed values are valid only for string-enum parameters.", nameof(allowedValues));
            }

            if (values.Count > 256)
            {
                throw new ArgumentException("Phase 1 string enums support at most 256 values.", nameof(allowedValues));
            }

            Name = name;
            Type = type;
            Required = required;
            Minimum = minimum;
            RequiredWhen = requiredWhen;
            ForbiddenUnless = forbiddenUnless;
            _allowedValues = values.AsReadOnly();
        }

        public string Name { get; }

        public NodeParameterType Type { get; }

        public bool Required { get; }

        public ulong? Minimum { get; }

        public IReadOnlyList<string> AllowedValues => _allowedValues;

        public NodeParameterCondition RequiredWhen { get; }

        public NodeParameterCondition ForbiddenUnless { get; }
    }

    public sealed class NodeAccessContract
    {
        public NodeAccessContract(string key, NodeAccessMode mode)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("An access declaration requires a key or key parameter.", nameof(key));
            }

            if (!Enum.IsDefined(typeof(NodeAccessMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            Key = key;
            Mode = mode;
        }

        public string Key { get; }

        public NodeAccessMode Mode { get; }
    }

    public sealed class NodeManifestExample
    {
        private readonly JObject _parameters;

        public NodeManifestExample(string title, string parametersJson, string expectedBehavior)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("An example requires a title.", nameof(title));
            }

            if (string.IsNullOrWhiteSpace(parametersJson))
            {
                throw new ArgumentException("An example requires a parameter object.", nameof(parametersJson));
            }

            if (string.IsNullOrWhiteSpace(expectedBehavior))
            {
                throw new ArgumentException("An example requires expected behavior.", nameof(expectedBehavior));
            }

            JObject parameters;
            try
            {
                parameters = JObject.Parse(parametersJson);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Example parameters must be a valid JSON object.", nameof(parametersJson), exception);
            }

            Title = title;
            _parameters = parameters;
            ExpectedBehavior = expectedBehavior;
        }

        public string Title { get; }

        public string ExpectedBehavior { get; }

        public JObject GetParametersCopy()
        {
            return (JObject)_parameters.DeepClone();
        }
    }
}
