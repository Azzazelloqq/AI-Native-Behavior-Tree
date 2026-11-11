using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AIBT.Authoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.NodeDevelopment
{
    internal static class NativeNodeBehaviorChecks
    {
        internal static JObject Contract(GeneratedNodeDescriptor node) => new JObject
        {
            ["configuration"] = new JArray(node.Configuration.Where(field => field.Encoding != GeneratedFieldEncoding.GeneratedHandle)
                .Select(field => new JObject { ["id"] = field.FieldId, ["type"] = field.ValueTypeId })),
            ["inputs"] = new JArray(node.Bindings.Where(binding => binding.IsBlackboard || binding.Kind == GeneratedBindingKind.SnapshotRead)
                .Select(binding => new JObject { ["id"] = binding.BindingId, ["type"] = binding.Types[0].CanonicalTypeId })),
            ["effects"] = new JArray(node.Bindings.Where(binding => binding.Kind == GeneratedBindingKind.EffectCommand)
                .Select(binding => new JObject { ["id"] = binding.BindingId, ["type"] = binding.Types[0].CanonicalTypeId })),
        };

        internal static JArray TemplateCases(string source)
        {
            var field = Regex.Match(source, @"AibtConfigField\(""threshold"",\s*""(Bool|Int32|UInt32|Float32|Float64)""");
            if (!field.Success) return new JArray();
            var boolean = field.Groups[1].Value == "Bool";
            JToken one = boolean ? new JValue(true) : new JValue(1);
            JToken zero = boolean ? new JValue(false) : new JValue(0);
            return new JArray(
                new JObject { ["name"] = "at-threshold", ["configuration"] = new JObject { ["threshold"] = one.DeepClone() }, ["inputs"] = new JObject { ["current"] = one.DeepClone() }, ["expectedStatus"] = "Success" },
                new JObject { ["name"] = "below-threshold", ["configuration"] = new JObject { ["threshold"] = one.DeepClone() }, ["inputs"] = new JObject { ["current"] = zero }, ["expectedStatus"] = "Failure" });
        }

        internal static string GenerateSource(string className, JArray cases)
            => "#if UNITY_EDITOR\nnamespace AIBT.Generated.Staging\n{\n    // Executed by test_node through native dispatch; expectations are source-controlled.\n    public static class " + className
                + "\n    {\n        public const string BehaviorCasesJson = @\"" + cases.ToString(Formatting.None).Replace("\"", "\"\"") + "\";\n    }\n}\n#endif\n";

        internal static JArray ReadCases(string projectRoot)
        {
            foreach (var path in StagingSlot.ListStagedFiles(projectRoot))
            {
                var match = Regex.Match(File.ReadAllText(path), "BehaviorCasesJson\\s*=\\s*@\"((?:\"\"|[^\"])*)\"", RegexOptions.Singleline);
                if (match.Success) return JArray.Parse(match.Groups[1].Value.Replace("\"\"", "\""));
            }
            return new JArray();
        }

        internal static JObject Run(GeneratedShardMetadataArtifact artifact, Type catalogType, JArray cases)
        {
            var results = new JArray();
            foreach (var item in cases)
            {
                var scenario = item as JObject;
                var result = new JObject { ["name"] = (string)scenario?["name"], ["passed"] = false };
                try
                {
                    if (scenario == null) throw new ArgumentException("Each behavior case must be an object.");
                    var expected = (string)scenario["expectedStatus"];
                    if (expected != "Success" && expected != "Failure" && expected != "Running") throw new ArgumentException("Each case requires expectedStatus: Success, Failure or Running.");
                    var observed = GenericNodeDispatchRunner.Run(artifact, catalogType, scenario);
                    var expectedCallback = (string)scenario["expectedCallbackFailure"] ?? "Success";
                    var passed = observed.DispatchProven && observed.EnteredSuccessfully && observed.TickStatus == expected && observed.CallbackFailure == expectedCallback;
                    if (scenario["expectedEffects"] is JObject effects)
                    {
                        passed &= effects.Count == observed.Effects.Count;
                        foreach (var effect in effects.Properties())
                        {
                            var binding = artifact.Nodes[0].Bindings.Single(candidate => candidate.BindingId == effect.Name);
                            passed &= observed.Effects.TryGetValue(effect.Name, out var payload)
                                && payload == Convert.ToBase64String(NativeBehaviorValues.Encode(binding.Types[0].CanonicalTypeId, effect.Value));
                        }
                    }
                    result["passed"] = passed;
                    result["expectedStatus"] = expected;
                    result["actualStatus"] = observed.TickStatus;
                    result["actualCallbackFailure"] = observed.CallbackFailure;
                    result["effectsBase64"] = JObject.FromObject(observed.Effects);
                    result["reason"] = observed.Reason;
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException || exception is OverflowException || exception is FormatException)
                { result["reason"] = exception.Message; }
                results.Add(result);
            }
            return new JObject
            {
                ["behaviorProven"] = results.Count > 0 && results.All(result => (bool)result["passed"]),
                ["cases"] = results, ["caseCount"] = results.Count,
                ["scope"] = "Independent Enter/Tick cases with fresh zeroed memory, explicit scalar inputs/configuration and optional exact effects. Not a multi-update/async gameplay test.",
            };
        }
    }
}
