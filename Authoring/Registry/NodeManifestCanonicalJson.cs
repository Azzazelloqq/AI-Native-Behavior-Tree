using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AIBT.Authoring
{
    internal static class NodeManifestCanonicalJson
    {
        internal const uint RegistryFormatVersion = 1;

        internal static string SerializeRegistry(NodeRegistryEntry[] entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var orderedEntries = new List<NodeRegistryEntry>(entries);
            for (var index = 0; index < orderedEntries.Count; index++)
            {
                if (orderedEntries[index] == null)
                {
                    throw new ArgumentException("Registry entries cannot contain null.", nameof(entries));
                }
            }

            orderedEntries.Sort((left, right) =>
            {
                var comparison = string.Compare(left.Manifest.TypeId, right.Manifest.TypeId, StringComparison.Ordinal);
                return comparison != 0 ? comparison : left.Manifest.Version.CompareTo(right.Manifest.Version);
            });

            var manifests = new JArray();
            for (var index = 0; index < orderedEntries.Count; index++)
            {
                manifests.Add(ToJson(orderedEntries[index].Manifest));
            }

            var root = new JObject
            {
                ["format"] = "aibt-node-registry",
                ["formatVersion"] = RegistryFormatVersion,
                ["manifests"] = manifests,
            };
            return SerializeCanonical(root);
        }

        internal static byte[] SerializeRegistryUtf8(NodeRegistryEntry[] entries)
        {
            return Encoding.UTF8.GetBytes(SerializeRegistry(entries));
        }

        internal static JObject ToJson(NodeManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var parameters = new JObject();
            var orderedParameters = new List<NodeParameterContract>(manifest.Parameters);
            orderedParameters.Sort((left, right) => CanonicalJsonText.CompareUtf8(left.Name, right.Name));
            for (var index = 0; index < orderedParameters.Count; index++)
            {
                var parameter = orderedParameters[index];
                var field = FindField(manifest, parameter.Name);
                var contract = new JObject
                {
                    ["type"] = ParameterTypeText(parameter.Type),
                    ["required"] = parameter.Required,
                };
                if (parameter.Minimum.HasValue)
                {
                    contract["minimum"] = parameter.Minimum.Value;
                }

                if (parameter.AllowedValues.Count > 0)
                {
                    contract["allowedValues"] = StringArray(parameter.AllowedValues);
                }

                if (parameter.RequiredWhen != null)
                {
                    contract["requiredWhen"] = ConditionJson(parameter.RequiredWhen);
                }

                if (parameter.ForbiddenUnless != null)
                {
                    contract["forbiddenUnless"] = ConditionJson(parameter.ForbiddenUnless);
                }

                contract["packing"] = new JObject
                {
                    ["offset"] = field.Offset,
                    ["size"] = field.Size,
                    ["alignment"] = field.Alignment,
                };
                parameters.Add(parameter.Name, contract);
            }

            var childPolicy = new JObject
            {
                ["minimum"] = manifest.ChildPolicy.Minimum,
                ["maximum"] = manifest.ChildPolicy.Maximum.HasValue
                    ? new JValue(manifest.ChildPolicy.Maximum.Value)
                    : JValue.CreateNull(),
                ["ordered"] = manifest.ChildPolicy.Ordered,
            };

            var memory = new JObject
            {
                ["size"] = manifest.Memory.Size,
                ["alignment"] = manifest.Memory.Alignment,
                ["lifetime"] = MemoryLifetimeText(manifest.Memory.Lifetime),
            };

            var configuration = new JObject
            {
                ["size"] = manifest.Configuration.Size,
                ["alignment"] = manifest.Configuration.Alignment,
            };

            var examples = new JArray();
            for (var index = 0; index < manifest.Examples.Count; index++)
            {
                var example = manifest.Examples[index];
                examples.Add(new JObject
                {
                    ["title"] = example.Title,
                    ["parameters"] = DiagnosticJson.CanonicalizePayload(example.GetParametersCopy()),
                    ["expectedBehavior"] = example.ExpectedBehavior,
                });
            }

            var result = new JObject
            {
                ["typeId"] = manifest.TypeId,
                ["version"] = manifest.Version,
                ["summary"] = manifest.Summary,
                ["category"] = manifest.Category,
                ["kind"] = BehaviorKindText(manifest.Kind),
                ["whenToUse"] = manifest.WhenToUse,
                ["whenNotToUse"] = manifest.WhenNotToUse,
                ["parameters"] = parameters,
                ["childPolicy"] = childPolicy,
                ["reads"] = StringArray(manifest.Reads),
                ["writes"] = StringArray(manifest.Writes),
                ["sideEffects"] = StringArray(manifest.SideEffects),
                ["possibleStatuses"] = StatusArray(manifest),
                ["memory"] = memory,
                ["configuration"] = configuration,
                ["cancellation"] = CancellationText(manifest.Cancellation),
                ["executionDomain"] = ExecutionDomainText(manifest.ExecutionDomain),
                ["deterministic"] = manifest.Deterministic,
                ["costHint"] = CostText(manifest.CostHint),
                ["examples"] = examples,
            };

            if (manifest.Deprecated)
            {
                result["deprecated"] = true;
            }

            if (manifest.ReplacementTypeId != null)
            {
                result["replacementTypeId"] = manifest.ReplacementTypeId;
            }

            return result;
        }

        private static NodeConfigurationField FindField(NodeManifest manifest, string parameterName)
        {
            for (var index = 0; index < manifest.Configuration.Fields.Count; index++)
            {
                if (manifest.Configuration.Fields[index].ParameterName == parameterName)
                {
                    return manifest.Configuration.Fields[index];
                }
            }

            throw new InvalidOperationException("The manifest configuration descriptor is incomplete.");
        }

        private static JObject ConditionJson(NodeParameterCondition condition)
        {
            return new JObject
            {
                ["parameter"] = condition.ParameterName,
                ["equals"] = condition.RequiredValue,
            };
        }

        private static JArray StringArray(System.Collections.Generic.IReadOnlyList<string> values)
        {
            var ordered = new List<string>(values);
            ordered.Sort(CanonicalJsonText.CompareUtf8);
            var array = new JArray();
            for (var index = 0; index < ordered.Count; index++)
            {
                array.Add(ordered[index]);
            }

            return array;
        }

        private static string SerializeCanonical(JToken token)
        {
            var builder = new StringBuilder(4096);
            WriteToken(builder, token, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        private static void WriteToken(StringBuilder builder, JToken token, int indent)
        {
            if (token is JObject objectValue)
            {
                WriteObject(builder, objectValue, indent);
                return;
            }

            if (token is JArray arrayValue)
            {
                WriteArray(builder, arrayValue, indent);
                return;
            }

            if (!(token is JValue value))
            {
                throw new ArgumentException("Only canonical JSON token kinds are supported.", nameof(token));
            }

            switch (value.Type)
            {
                case JTokenType.Null:
                    builder.Append("null");
                    break;
                case JTokenType.Boolean:
                    builder.Append((bool)value.Value ? "true" : "false");
                    break;
                case JTokenType.Integer:
                    builder.Append(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                    break;
                case JTokenType.Float:
                    if (value.Value is float floatValue)
                    {
                        builder.Append(CanonicalJsonNumber.Format(floatValue));
                    }
                    else
                    {
                        builder.Append(CanonicalJsonNumber.Format(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture)));
                    }
                    break;
                case JTokenType.String:
                    CanonicalJsonText.WriteString(builder, (string)value.Value);
                    break;
                default:
                    throw new ArgumentException("Only null, Boolean, integer, finite float, string, array, and object JSON values are supported.", nameof(token));
            }
        }

        private static void WriteObject(StringBuilder builder, JObject value, int indent)
        {
            var properties = new List<JProperty>(value.Properties());
            if (properties.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');
            for (var index = 0; index < properties.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
                AppendIndent(builder, indent + 1);
                CanonicalJsonText.WriteString(builder, properties[index].Name);
                builder.Append(": ");
                WriteToken(builder, properties[index].Value, indent + 1);
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, JArray value, int indent)
        {
            if (value.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append('[');
            for (var index = 0; index < value.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
                AppendIndent(builder, indent + 1);
                WriteToken(builder, value[index], indent + 1);
            }

            builder.Append('\n');
            AppendIndent(builder, indent);
            builder.Append(']');
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }

        private static JArray StatusArray(NodeManifest manifest)
        {
            var array = new JArray();
            for (var index = 0; index < manifest.PossibleStatuses.Count; index++)
            {
                switch (manifest.PossibleStatuses[index])
                {
                    case NodeStatus.Success:
                        array.Add("success");
                        break;
                    case NodeStatus.Failure:
                        array.Add("failure");
                        break;
                    case NodeStatus.Running:
                        array.Add("running");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return array;
        }

        private static string ParameterTypeText(NodeParameterType value)
        {
            switch (value)
            {
                case NodeParameterType.Boolean:
                    return "boolean";
                case NodeParameterType.UInt32:
                    return "uint32";
                case NodeParameterType.UInt64:
                    return "uint64";
                case NodeParameterType.StringEnum:
                    return "string-enum";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        private static string ExecutionDomainText(NodeExecutionDomain value)
        {
            switch (value)
            {
                case NodeExecutionDomain.Burst:
                    return "burst";
                case NodeExecutionDomain.Managed:
                    return "managed";
                case NodeExecutionDomain.MainThread:
                    return "main-thread";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        private static string MemoryLifetimeText(NodeMemoryLifetime value)
        {
            switch (value)
            {
                case NodeMemoryLifetime.Activation:
                    return "activation";
                case NodeMemoryLifetime.Instance:
                    return "instance";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        private static string BehaviorKindText(NodeBehaviorKind value)
        {
            switch (value)
            {
                case NodeBehaviorKind.Composite: return "composite";
                case NodeBehaviorKind.Decorator: return "decorator";
                case NodeBehaviorKind.Condition: return "condition";
                case NodeBehaviorKind.Action: return "action";
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        private static string CancellationText(NodeCancellationMode value)
        {
            switch (value)
            {
                case NodeCancellationMode.NotApplicable:
                    return "not-applicable";
                case NodeCancellationMode.AbortOnly:
                    return "abort-only";
                case NodeCancellationMode.Command:
                    return "command";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        private static string CostText(NodeCostHint value)
        {
            switch (value)
            {
                case NodeCostHint.Trivial:
                    return "trivial";
                case NodeCostHint.Low:
                    return "low";
                case NodeCostHint.Medium:
                    return "medium";
                case NodeCostHint.High:
                    return "high";
                case NodeCostHint.Variable:
                    return "variable";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }
    }
}
