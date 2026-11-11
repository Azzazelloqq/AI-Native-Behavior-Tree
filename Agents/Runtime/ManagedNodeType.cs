using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using AIBT.Authoring;

namespace AIBT.Agents
{
    /// <summary>
    /// Reflection-derived contract of one <see cref="BtNode"/> class: its node manifest and the
    /// field binders that deliver authored parameters to a node instance. Built once per type.
    /// </summary>
    internal sealed class ManagedNodeType
    {
        private readonly ParameterBinding[] _parameters;

        private ManagedNodeType(Type type, NodeManifest manifest, ParameterBinding[] parameters)
        {
            Type = type;
            Manifest = manifest;
            _parameters = parameters;
            NumericTypeId = StableHash.Fnv1A64(manifest.TypeId);
        }

        internal Type Type { get; }

        internal NodeManifest Manifest { get; }

        internal ulong NumericTypeId { get; }

        internal string TypeId => Manifest.TypeId;

        internal static bool TryCreate(Type type, out ManagedNodeType result, out string problem)
        {
            result = null;
            problem = null;
            var declaration = type.GetCustomAttribute<BtNodeAttribute>(false);
            if (declaration == null)
            {
                problem = type.FullName + " needs a [BtNode(\"project.node-name\")] attribute to be used in a tree.";
                return false;
            }

            if (type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) == null)
            {
                problem = type.FullName + " must be a non-generic class with a public parameterless constructor.";
                return false;
            }

            BtNode prototype;
            try
            {
                prototype = (BtNode)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                problem = type.FullName + " could not be constructed: " + Unwrap(exception).Message;
                return false;
            }

            var bindings = new List<ParameterBinding>();
            var contracts = new List<NodeParameterContract>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var example = new StringBuilder("{");
            foreach (var field in CollectFields(type))
            {
                if (!ParameterBinding.TryCreate(field, prototype, out var binding, out var contract, out var fieldProblem))
                {
                    problem = type.FullName + "." + field.Name + ": " + fieldProblem;
                    return false;
                }

                if (!names.Add(binding.Name))
                {
                    problem = type.FullName + " declares parameter '" + binding.Name + "' more than once.";
                    return false;
                }

                bindings.Add(binding);
                contracts.Add(contract);
                if (example.Length > 1) example.Append(',');
                example.Append('"').Append(binding.Name).Append("\":").Append(binding.ExampleJson);
            }

            example.Append('}');
            var isAction = prototype.Kind == NodeBehaviorKindTag.Action;
            var summary = string.IsNullOrWhiteSpace(declaration.Summary)
                ? Humanize(type.Name) + "."
                : declaration.Summary.Trim();
            try
            {
                var manifest = new NodeManifest(
                    declaration.TypeId,
                    declaration.Version,
                    summary,
                    string.IsNullOrWhiteSpace(declaration.Category) ? "Project" : declaration.Category.Trim(),
                    isAction ? NodeBehaviorKind.Action : NodeBehaviorKind.Condition,
                    declaration.WhenToUse ?? string.Empty,
                    declaration.WhenNotToUse ?? string.Empty,
                    NodeExecutionDomain.MainThread,
                    false,
                    contracts,
                    new NodeChildPolicy(0, 0, true),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    isAction
                        ? new[] { NodeStatus.Running, NodeStatus.Success, NodeStatus.Failure }
                        : new[] { NodeStatus.Success, NodeStatus.Failure },
                    new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                    new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                    isAction ? NodeCancellationMode.AbortOnly : NodeCancellationMode.NotApplicable,
                    NodeCostHint.Variable,
                    new[] { new NodeManifestExample(Humanize(type.Name), example.ToString(), summary) });
                result = new ManagedNodeType(type, manifest, bindings.ToArray());
                return true;
            }
            catch (ArgumentException exception)
            {
                problem = type.FullName + ": " + exception.Message;
                return false;
            }
        }

        internal BtNode CreateNode() => (BtNode)Activator.CreateInstance(Type);

        /// <summary>Assigns every authored parameter to <paramref name="node"/>; fields a tree omits keep their initializer.</summary>
        internal void Bind(BtNode node, NodeDocument document, ManagedTreeInstance instance)
        {
            for (var index = 0; index < _parameters.Length; index++)
            {
                var parameter = _parameters[index];
                SemanticValue value = null;
                var hasValue = document.Parameters != null && document.Parameters.TryGetValue(parameter.Name, out value);
                if (hasValue) parameter.Assign(node, value, document, instance);
            }
        }

        internal static string ToKebabCase(string name)
        {
            var builder = new StringBuilder(name.Length + 4);
            for (var index = 0; index < name.Length; index++)
            {
                var character = name[index];
                if (character == '_' || character == ' ')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != '-') builder.Append('-');
                    continue;
                }

                if (char.IsUpper(character))
                {
                    var previousLowerOrDigit = index > 0 && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1]));
                    var acronymEnd = index > 0 && char.IsUpper(name[index - 1])
                        && index + 1 < name.Length && char.IsLower(name[index + 1]);
                    if ((previousLowerOrDigit || acronymEnd) && builder.Length > 0 && builder[builder.Length - 1] != '-')
                        builder.Append('-');
                    builder.Append(char.ToLowerInvariant(character));
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString().Trim('-');
        }

        internal static string Humanize(string name)
        {
            var kebab = ToKebabCase(name);
            if (kebab.Length == 0) return name;
            var text = kebab.Replace('-', ' ');
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static IEnumerable<FieldInfo> CollectFields(Type type)
        {
            var chain = new List<Type>();
            // BtCondition itself is included: its public 'interrupt' field is a parameter of every condition.
            for (var current = type; current != null && current != typeof(BtAction) && current != typeof(BtNode);
                 current = current.BaseType)
            {
                chain.Add(current);
            }

            // Base-class parameters first, then declaration order within each class.
            for (var index = chain.Count - 1; index >= 0; index--)
            {
                var fields = chain[index].GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    var field = fields[fieldIndex];
                    if (field.IsInitOnly || field.IsLiteral || field.IsDefined(typeof(BtIgnoreAttribute), true)
                        || field.IsDefined(typeof(NonSerializedAttribute), true))
                    {
                        continue;
                    }

                    yield return field;
                }
            }
        }

        private static Exception Unwrap(Exception exception)
            => exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;

        private sealed class ParameterBinding
        {
            private readonly FieldInfo _field;
            private readonly NodeParameterType _type;
            private readonly Dictionary<string, object> _enumValues;

            private ParameterBinding(FieldInfo field, string name, NodeParameterType type, Dictionary<string, object> enumValues, string exampleJson)
            {
                _field = field;
                Name = name;
                _type = type;
                _enumValues = enumValues;
                ExampleJson = exampleJson;
            }

            internal string Name { get; }

            internal string ExampleJson { get; }

            internal static bool TryCreate(
                FieldInfo field,
                BtNode prototype,
                out ParameterBinding binding,
                out NodeParameterContract contract,
                out string problem)
            {
                binding = null;
                contract = null;
                problem = null;
                var settings = field.GetCustomAttribute<BtParamAttribute>(true);
                var name = settings != null && !string.IsNullOrWhiteSpace(settings.Name) ? settings.Name : ToKebabCase(field.Name);
                if (name.Length == 0)
                {
                    problem = "the parameter name is empty.";
                    return false;
                }

                var fieldType = field.FieldType;
                var required = settings != null && settings.Required;
                var defaultValue = field.GetValue(prototype);
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(BbKey<>))
                {
                    var valueType = fieldType.GetGenericArguments()[0];
                    if (!BbTypes.TryGetSlotType(valueType, out var slotType))
                    {
                        problem = "BbKey<" + valueType.Name + "> is not supported. Use bool, int, long, float, double, Vector2, Vector3, Quaternion or string.";
                        return false;
                    }

                    var optional = settings != null && settings.Optional;
                    contract = new NodeParameterContract(name, NodeParameterType.BlackboardKey, !optional);
                    if (slotType != BlackboardValueType.Invalid)
                        contract = contract.WithBlackboardValueType(BlackboardTypeReference.BuiltIn(slotType).CanonicalTypeId);
                    binding = new ParameterBinding(field, name, NodeParameterType.BlackboardKey, null, "\"" + name + "\"");
                    return true;
                }

                if (fieldType == typeof(bool))
                {
                    var value = (bool)defaultValue;
                    contract = WithDefault(new NodeParameterContract(name, NodeParameterType.Boolean, required), required, SemanticValue.FromBoolean(value));
                    binding = new ParameterBinding(field, name, NodeParameterType.Boolean, null, value ? "true" : "false");
                    return true;
                }

                if (fieldType == typeof(int))
                {
                    var value = (int)defaultValue;
                    contract = WithDefault(new NodeParameterContract(name, NodeParameterType.Int32, required), required, SemanticValue.FromInt64(value));
                    binding = new ParameterBinding(field, name, NodeParameterType.Int32, null, value.ToString(CultureInfo.InvariantCulture));
                    return true;
                }

                if (fieldType == typeof(float))
                {
                    var value = (float)defaultValue;
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        problem = "a float parameter's default must be a finite number.";
                        return false;
                    }

                    // Round-trip through the shortest float text so 0.1f documents as 0.1, not 0.100000001490116.
                    var shortest = value.ToString("R", CultureInfo.InvariantCulture);
                    var number = double.Parse(shortest, NumberStyles.Float, CultureInfo.InvariantCulture);
                    contract = WithDefault(new NodeParameterContract(name, NodeParameterType.Float32, required), required, SemanticValue.FromNumber(number));
                    binding = new ParameterBinding(field, name, NodeParameterType.Float32, null, number.ToString("R", CultureInfo.InvariantCulture));
                    return true;
                }

                if (fieldType == typeof(string))
                {
                    var value = (string)defaultValue ?? string.Empty;
                    contract = WithDefault(new NodeParameterContract(name, NodeParameterType.String, required), required, SemanticValue.FromString(value));
                    binding = new ParameterBinding(field, name, NodeParameterType.String, null, Quote(value));
                    return true;
                }

                if (fieldType.IsEnum)
                {
                    var values = new Dictionary<string, object>(StringComparer.Ordinal);
                    string defaultText = null;
                    foreach (var enumName in Enum.GetNames(fieldType))
                    {
                        var text = ToKebabCase(enumName);
                        var enumValue = Enum.Parse(fieldType, enumName);
                        if (values.ContainsKey(text))
                        {
                            problem = "enum " + fieldType.Name + " has two members that map to '" + text + "'.";
                            return false;
                        }

                        values.Add(text, enumValue);
                        if (defaultText == null && Equals(enumValue, defaultValue)) defaultText = text;
                    }

                    if (values.Count == 0 || values.Count > 256)
                    {
                        problem = "enum " + fieldType.Name + " must have between 1 and 256 members.";
                        return false;
                    }

                    contract = new NodeParameterContract(name, NodeParameterType.StringEnum, required, allowedValues: values.Keys);
                    if (!required && defaultText != null) contract = contract.WithDefault(SemanticValue.FromString(defaultText));
                    binding = new ParameterBinding(field, name, NodeParameterType.StringEnum, values, Quote(defaultText ?? FirstKey(values)));
                    return true;
                }

                problem = "field type " + fieldType.Name + " is not a supported parameter type. Use bool, int, float, string, an enum or BbKey<T>, or mark the field [BtIgnore].";
                return false;
            }

            internal void Assign(BtNode node, SemanticValue value, NodeDocument document, ManagedTreeInstance instance)
            {
                switch (_type)
                {
                    case NodeParameterType.Boolean:
                        if (value.TryGetBoolean(out var boolean)) _field.SetValue(node, boolean);
                        break;
                    case NodeParameterType.Int32:
                        if (value.TryGetInt64(out var signed)) _field.SetValue(node, checked((int)signed));
                        else if (value.TryGetUInt64(out var unsigned)) _field.SetValue(node, checked((int)unsigned));
                        break;
                    case NodeParameterType.Float32:
                        if (value.TryGetNumber(out var number)) _field.SetValue(node, (float)number);
                        else if (value.TryGetInt64(out var signedNumber)) _field.SetValue(node, (float)signedNumber);
                        else if (value.TryGetUInt64(out var unsignedNumber)) _field.SetValue(node, (float)unsignedNumber);
                        break;
                    case NodeParameterType.String:
                        if (value.TryGetString(out var text)) _field.SetValue(node, text);
                        break;
                    case NodeParameterType.StringEnum:
                        if (value.TryGetString(out var enumText) && _enumValues.TryGetValue(enumText, out var enumValue))
                            _field.SetValue(node, enumValue);
                        break;
                    case NodeParameterType.BlackboardKey:
                        if (!value.TryGetString(out var keyName)) break;
                        var key = (IBbKeyBinding)Activator.CreateInstance(_field.FieldType);
                        if (!instance.TryResolveKey(keyName, out var slot, out var slotType))
                        {
                            throw new InvalidOperationException("Node '" + document.Id.Value + "' parameter '" + Name
                                + "' names blackboard key '" + keyName + "', which the tree does not declare.");
                        }

                        if (!BbTypes.Accepts(key.ValueType, slotType))
                        {
                            throw new InvalidOperationException("Node '" + document.Id.Value + "' parameter '" + Name
                                + "' needs " + BbTypes.Describe(key.ValueType) + " but blackboard key '" + keyName + "' is " + slotType + ".");
                        }

                        key.Bind(keyName, slot);
                        _field.SetValue(node, key);
                        break;
                }
            }

            private static NodeParameterContract WithDefault(NodeParameterContract contract, bool required, SemanticValue value)
                => required ? contract : contract.WithDefault(value);

            private static string FirstKey(Dictionary<string, object> values)
            {
                foreach (var key in values.Keys) return key;
                return string.Empty;
            }

            private static string Quote(string value)
            {
                var builder = new StringBuilder(value.Length + 2);
                builder.Append('"');
                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    switch (character)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (character < ' ') builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            else builder.Append(character);
                            break;
                    }
                }

                builder.Append('"');
                return builder.ToString();
            }
        }
    }
}
