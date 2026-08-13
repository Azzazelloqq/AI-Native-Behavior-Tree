using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class NodeManifest
    {
        private readonly ReadOnlyCollection<NodeParameterContract> _parameters;
        private readonly ReadOnlyCollection<NodeAccessContract> _accesses;
        private readonly ReadOnlyCollection<string> _reads;
        private readonly ReadOnlyCollection<string> _writes;
        private readonly ReadOnlyCollection<string> _sideEffects;
        private readonly ReadOnlyCollection<NodeStatus> _possibleStatuses;
        private readonly ReadOnlyCollection<NodeManifestExample> _examples;

        public NodeManifest(
            string typeId,
            uint version,
            string summary,
            string category,
            NodeBehaviorKind kind,
            string whenToUse,
            string whenNotToUse,
            NodeExecutionDomain executionDomain,
            bool deterministic,
            IEnumerable<NodeParameterContract> parameters,
            NodeChildPolicy childPolicy,
            IEnumerable<string> reads,
            IEnumerable<string> writes,
            IEnumerable<string> sideEffects,
            IEnumerable<NodeStatus> possibleStatuses,
            NodeMemoryDescriptor memory,
            NodeConfigurationDescriptor configuration,
            NodeCancellationMode cancellation,
            NodeCostHint costHint,
            IEnumerable<NodeManifestExample> examples,
            bool deprecated = false,
            string replacementTypeId = null)
        {
            if (!NodeTypeIdRules.IsValid(typeId))
            {
                throw new ArgumentException("Node type IDs must be lowercase project-qualified canonical IDs.", nameof(typeId));
            }

            if (version == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(version), "Node type versions start at one.");
            }

            RequireText(summary, nameof(summary));
            RequireText(category, nameof(category));
            if (!Enum.IsDefined(typeof(NodeBehaviorKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (whenToUse == null)
            {
                throw new ArgumentNullException(nameof(whenToUse));
            }

            if (whenNotToUse == null)
            {
                throw new ArgumentNullException(nameof(whenNotToUse));
            }

            if (!Enum.IsDefined(typeof(NodeExecutionDomain), executionDomain))
            {
                throw new ArgumentOutOfRangeException(nameof(executionDomain));
            }

            if (!Enum.IsDefined(typeof(NodeCancellationMode), cancellation))
            {
                throw new ArgumentOutOfRangeException(nameof(cancellation));
            }

            if (!Enum.IsDefined(typeof(NodeCostHint), costHint))
            {
                throw new ArgumentOutOfRangeException(nameof(costHint));
            }

            ChildPolicy = childPolicy ?? throw new ArgumentNullException(nameof(childPolicy));
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            var parameterValues = CopyAndSortUnique(parameters, item => item.Name, nameof(parameters));
            var readValues = CopyAndSortUniqueStrings(reads, nameof(reads));
            var writeValues = CopyAndSortUniqueStrings(writes, nameof(writes));
            var effectValues = CopyAndSortUniqueStrings(sideEffects, nameof(sideEffects));
            var statusValues = CopyAndSortStatuses(possibleStatuses);
            var exampleValues = CopyExamples(examples);

            ValidateConfiguration(parameterValues, configuration);
            ValidateConditions(parameterValues);

            if (replacementTypeId != null && !NodeTypeIdRules.IsValid(replacementTypeId))
            {
                throw new ArgumentException("Replacement type IDs must be canonical node type IDs.", nameof(replacementTypeId));
            }

            if (!deprecated && replacementTypeId != null)
            {
                throw new ArgumentException("Only deprecated node types can declare a replacement.", nameof(replacementTypeId));
            }

            TypeId = typeId;
            Version = version;
            Summary = summary;
            Category = category;
            Kind = kind;
            WhenToUse = whenToUse;
            WhenNotToUse = whenNotToUse;
            ExecutionDomain = executionDomain;
            Deterministic = deterministic;
            Cancellation = cancellation;
            CostHint = costHint;
            Deprecated = deprecated;
            ReplacementTypeId = replacementTypeId;
            _parameters = parameterValues.AsReadOnly();
            _reads = readValues.AsReadOnly();
            _writes = writeValues.AsReadOnly();
            _sideEffects = effectValues.AsReadOnly();
            _possibleStatuses = statusValues.AsReadOnly();
            _examples = exampleValues.AsReadOnly();

            var accessValues = new List<NodeAccessContract>(readValues.Count + writeValues.Count);
            for (var index = 0; index < readValues.Count; index++)
            {
                accessValues.Add(new NodeAccessContract(readValues[index], NodeAccessMode.Read));
            }

            for (var index = 0; index < writeValues.Count; index++)
            {
                accessValues.Add(new NodeAccessContract(writeValues[index], NodeAccessMode.Write));
            }

            accessValues.Sort((left, right) =>
            {
                var result = string.Compare(left.Key, right.Key, StringComparison.Ordinal);
                return result != 0 ? result : left.Mode.CompareTo(right.Mode);
            });
            _accesses = accessValues.AsReadOnly();
        }

        public string TypeId { get; }

        public uint Version { get; }

        public string Summary { get; }

        public string Category { get; }

        public NodeBehaviorKind Kind { get; }

        public string WhenToUse { get; }

        public string WhenNotToUse { get; }

        public NodeExecutionDomain ExecutionDomain { get; }

        public bool Deterministic { get; }

        public IReadOnlyList<NodeParameterContract> Parameters => _parameters;

        public NodeChildPolicy ChildPolicy { get; }

        public IReadOnlyList<NodeAccessContract> Accesses => _accesses;

        public IReadOnlyList<string> Reads => _reads;

        public IReadOnlyList<string> Writes => _writes;

        public IReadOnlyList<string> SideEffects => _sideEffects;

        public IReadOnlyList<NodeStatus> PossibleStatuses => _possibleStatuses;

        public NodeMemoryDescriptor Memory { get; }

        public NodeConfigurationDescriptor Configuration { get; }

        public NodeCancellationMode Cancellation { get; }

        public NodeCostHint CostHint { get; }

        public IReadOnlyList<NodeManifestExample> Examples => _examples;

        public bool Deprecated { get; }

        public string ReplacementTypeId { get; }

        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty value is required.", parameterName);
            }
        }

        private static List<T> CopyAndSortUnique<T>(IEnumerable<T> source, Func<T, string> key, string parameterName)
            where T : class
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var result = new List<T>(source);
            for (var index = 0; index < result.Count; index++)
            {
                if (result[index] == null)
                {
                    throw new ArgumentException("Collections cannot contain null entries.", parameterName);
                }
            }

            result.Sort((left, right) => string.Compare(key(left), key(right), StringComparison.Ordinal));
            for (var index = 0; index < result.Count; index++)
            {
                if (index > 0 && string.Equals(key(result[index]), key(result[index - 1]), StringComparison.Ordinal))
                {
                    throw new ArgumentException("Collection keys must be unique.", parameterName);
                }
            }

            return result;
        }

        private static List<string> CopyAndSortUniqueStrings(IEnumerable<string> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var result = new List<string>(source);
            result.Sort(StringComparer.Ordinal);
            for (var index = 0; index < result.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(result[index]))
                {
                    throw new ArgumentException("Set values cannot be empty.", parameterName);
                }

                if (index > 0 && result[index] == result[index - 1])
                {
                    throw new ArgumentException("Set values must be unique.", parameterName);
                }
            }

            return result;
        }

        private static List<NodeStatus> CopyAndSortStatuses(IEnumerable<NodeStatus> statuses)
        {
            if (statuses == null)
            {
                throw new ArgumentNullException(nameof(statuses));
            }

            var result = new List<NodeStatus>(statuses);
            result.Sort((left, right) => string.Compare(StatusText(left), StatusText(right), StringComparison.Ordinal));
            for (var index = 0; index < result.Count; index++)
            {
                if (!Enum.IsDefined(typeof(NodeStatus), result[index]) || (index > 0 && result[index] == result[index - 1]))
                {
                    throw new ArgumentException("Possible statuses must be valid and unique.", nameof(statuses));
                }
            }

            if (result.Count == 0)
            {
                throw new ArgumentException("At least one possible status is required.", nameof(statuses));
            }

            return result;
        }

        private static string StatusText(NodeStatus status)
        {
            switch (status)
            {
                case NodeStatus.Success:
                    return "success";
                case NodeStatus.Failure:
                    return "failure";
                case NodeStatus.Running:
                    return "running";
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        private static List<NodeManifestExample> CopyExamples(IEnumerable<NodeManifestExample> examples)
        {
            if (examples == null)
            {
                throw new ArgumentNullException(nameof(examples));
            }

            var result = new List<NodeManifestExample>(examples);
            if (result.Count == 0)
            {
                throw new ArgumentException("At least one behavior example is required.", nameof(examples));
            }

            for (var index = 0; index < result.Count; index++)
            {
                if (result[index] == null)
                {
                    throw new ArgumentException("Examples cannot contain null entries.", nameof(examples));
                }
            }

            return result;
        }

        private static void ValidateConfiguration(
            IReadOnlyList<NodeParameterContract> parameters,
            NodeConfigurationDescriptor configuration)
        {
            if (parameters.Count != configuration.Fields.Count)
            {
                throw new ArgumentException("Every parameter must have exactly one configuration packing field.", nameof(configuration));
            }

            for (var index = 0; index < parameters.Count; index++)
            {
                var found = false;
                for (var fieldIndex = 0; fieldIndex < configuration.Fields.Count; fieldIndex++)
                {
                    if (parameters[index].Name == configuration.Fields[fieldIndex].ParameterName)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new ArgumentException("Configuration packing fields must match parameter names.", nameof(configuration));
                }

            }

            for (var fieldIndex = 0; fieldIndex < configuration.Fields.Count; fieldIndex++)
            {
                var field = configuration.Fields[fieldIndex];
                for (var parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
                {
                    if (parameters[parameterIndex].Name != field.ParameterName)
                    {
                        continue;
                    }

                    var expectedSize = parameters[parameterIndex].Type == NodeParameterType.UInt64 ? 8u
                        : parameters[parameterIndex].Type == NodeParameterType.UInt32 ? 4u
                        : 1u;
                    var expectedAlignment = (byte)expectedSize;
                    if (field.Size != expectedSize || field.Alignment != expectedAlignment)
                    {
                        throw new ArgumentException("Configuration field packing must match its parameter type.", nameof(configuration));
                    }

                    break;
                }
            }
        }

        private static void ValidateConditions(IReadOnlyList<NodeParameterContract> parameters)
        {
            for (var index = 0; index < parameters.Count; index++)
            {
                ValidateCondition(parameters, parameters[index].RequiredWhen);
                ValidateCondition(parameters, parameters[index].ForbiddenUnless);
            }
        }

        private static void ValidateCondition(IReadOnlyList<NodeParameterContract> parameters, NodeParameterCondition condition)
        {
            if (condition == null)
            {
                return;
            }

            for (var index = 0; index < parameters.Count; index++)
            {
                if (parameters[index].Name != condition.ParameterName)
                {
                    continue;
                }

                if (parameters[index].Type != NodeParameterType.StringEnum)
                {
                    throw new ArgumentException("Conditional parameters must reference a string-enum parameter.", nameof(parameters));
                }

                for (var valueIndex = 0; valueIndex < parameters[index].AllowedValues.Count; valueIndex++)
                {
                    if (parameters[index].AllowedValues[valueIndex] == condition.RequiredValue)
                    {
                        return;
                    }
                }

                break;
            }

            throw new ArgumentException("Parameter conditions must reference an existing allowed value.", nameof(parameters));
        }

    }

    internal static class NodeTypeIdRules
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 255 || value.IndexOf('.') <= 0 || value[value.Length - 1] == '.')
            {
                return false;
            }

            var segmentStart = true;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '.')
                {
                    if (segmentStart || value[index - 1] == '-')
                    {
                        return false;
                    }

                    segmentStart = true;
                    continue;
                }

                if ((character < 'a' || character > 'z') && (character < '0' || character > '9') && character != '-')
                {
                    return false;
                }

                if (segmentStart && character == '-')
                {
                    return false;
                }

                segmentStart = false;
            }

            return !segmentStart && value[value.Length - 1] != '-';
        }
    }
}
