using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp.NodeDevelopment
{
    /// <summary>
    /// Implements the 6 Node development tools this card owns: generate-node, preview-node-diff,
    /// generate-node-tests-and-manifest, analyze-and-compile-node, test-node, apply-node --
    /// scaffolding real, compilable node source from the two maintained templates
    /// (Samples~/BurstNodes/Runtime/PublicBurstNodeSample.cs's own Condition/Action shapes) and
    /// driving the real, already-accepted CodeGen~/AIBT.CodeGen analyzer + Authoring/Registry/
    /// Generated/ registry materializer -- no second generator or registry mechanism.
    /// <para>
    /// analyze-and-compile-node is a two-call (start/check), non-blocking design: a script write
    /// can trigger a real domain reload that destroys the request thread that started it (confirmed
    /// empirically this session; see McpBridgeAutoRestart), so no single call here ever waits on
    /// compilation -- the caller polls a separate check call, exactly as this project's own async
    /// job tools elsewhere expect a caller to do.
    /// </para>
    /// <para>
    /// apply-node never trusts a caller's claim that a prior check succeeded or tracks
    /// server-side session state (MCP calls in this project reload fresh every time by design,
    /// per ai-and-mcp.md's own domain-patch precondition philosophy) -- it re-verifies the current
    /// staged content's hash against the caller-supplied value and re-runs the registry
    /// materialization check itself, immediately before persisting.
    /// </para>
    /// </summary>
    internal static class McpNodeDevelopmentToolDispatcher
    {
        private const string StagingAssemblyName = "AIBT.Generated.Staging";

        // ---- generate-node ---------------------------------------------------------------------

        internal static JObject GenerateNode(string projectRoot, JObject args)
        {
            var kind = RequireString(args, "kind");
            string fileName;
            string source;
            string shardId;
            switch (kind)
            {
                case "condition":
                    var conditionSpec = ReadConditionSpec(args);
                    source = NodeTemplateGenerator.GenerateCondition(conditionSpec);
                    fileName = conditionSpec.NodeTypeName + ".cs";
                    shardId = conditionSpec.ShardId;
                    break;
                case "action":
                    var actionSpec = ReadActionSpec(args);
                    source = NodeTemplateGenerator.GenerateAction(actionSpec);
                    fileName = actionSpec.NodeTypeName + ".cs";
                    shardId = actionSpec.ShardId;
                    break;
                default:
                    throw new McpToolException(McpNodeDevelopmentDiagnostics.UnknownNodeKind, "'" + kind + "' is not a maintained template kind. Supported: condition, action.");
            }

            StagingSlot.WriteNode(projectRoot, fileName, source);
            return new JObject
            {
                ["fileName"] = fileName,
                ["source"] = source,
                ["shardId"] = shardId,
            };
        }

        // ---- preview-node-diff ------------------------------------------------------------------

        internal static JObject PreviewNodeDiff(string projectRoot, JObject args)
        {
            var files = new JArray();
            foreach (var path in StagingSlot.ListStagedFiles(projectRoot))
            {
                files.Add(new JObject
                {
                    ["fileName"] = Path.GetFileName(path),
                    ["content"] = File.ReadAllText(path),
                });
            }

            return new JObject { ["files"] = files };
        }

        // ---- generate-node-tests-and-manifest -----------------------------------------------------

        internal static JObject GenerateNodeTestsAndManifest(string projectRoot, JObject args)
        {
            var staged = StagingSlot.ListStagedFiles(projectRoot);
            if (staged.Count == 0)
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.NoPendingGeneration, "No pending node generation -- call generate-node first.");
            }

            var nodeFileName = Path.GetFileNameWithoutExtension(staged[0]);
            var testFileName = nodeFileName + "Tests.cs";
            // Manifest data needs no separate artifact: it is inherent in the [AibtBurstNode]/
            // [AibtNodeDocumentation] attributes generate-node already wrote (ai-and-mcp.md's own
            // "generated from the same metadata" rule) -- confirmed structurally once
            // analyze-and-compile-node/test-node succeed. Real dispatch-driven test assertions
            // need a generic native-dispatch workspace translator that does not exist yet
            // (see P6-022, spun off this session); this scaffold is an honest, disclosed
            // placeholder, not a fake pretend-working test.
            var source = "using NUnit.Framework;\n\n"
                + "namespace AIBT.Generated.Staging\n{\n"
                + "    [TestFixture]\n    public sealed class " + nodeFileName + "Tests\n    {\n"
                + "        // TODO: real dispatch-driven assertions need a generic native-dispatch\n"
                + "        // workspace translator (P6-022, not yet accepted/implemented). Until\n"
                + "        // then, this node's structural/registry validity is proven by\n"
                + "        // analyze-and-compile-node and test-node instead of by this file.\n"
                + "        [Test]\n        public void Placeholder_AwaitingP6022DispatchHarness()\n        {\n"
                + "            Assert.Inconclusive(\"Real dispatch execution needs P6-022's generic native-dispatch test harness.\");\n"
                + "        }\n    }\n}\n";

            StagingSlot.WriteTests(projectRoot, testFileName, source);
            return new JObject
            {
                ["fileName"] = testFileName,
                ["source"] = source,
                ["manifestNote"] = "Manifest data is inherent in generate-node's own attributes; no separate artifact is produced.",
            };
        }

        // ---- analyze-and-compile-node ------------------------------------------------------------

        internal static JObject AnalyzeAndCompileNode(string projectRoot, JObject args)
        {
            var mode = RequireString(args, "mode");
            switch (mode)
            {
                case "start":
                {
                    if (StagingSlot.ListStagedFiles(projectRoot).Count == 0)
                    {
                        throw new McpToolException(McpNodeDevelopmentDiagnostics.NoPendingGeneration, "No pending node generation -- call generate-node first.");
                    }

                    var logPosition = EditorLogCompileWatcher.CurrentLogPosition(projectRoot);
                    return new JObject { ["status"] = "pending", ["logPositionBefore"] = logPosition };
                }
                case "check":
                {
                    var logPositionBefore = RequireLong(args, "logPositionBefore");
                    var result = EditorLogCompileWatcher.Check(projectRoot, logPositionBefore);
                    switch (result.Status)
                    {
                        case CompileWatchStatus.NotYetObserved:
                            return new JObject { ["status"] = "not-yet-observed", ["logPositionBefore"] = logPositionBefore };
                        case CompileWatchStatus.StillCompiling:
                            return new JObject { ["status"] = "still-compiling", ["logPositionBefore"] = logPositionBefore };
                        case CompileWatchStatus.Failed:
                            return new JObject
                            {
                                ["status"] = "failed",
                                ["diagnostics"] = ExtractDiagnosticLines(result.LogTail),
                            };
                        default:
                            var contentHash = ComputeStagedContentHash(projectRoot);
                            return new JObject { ["status"] = "compiled", ["contentHash"] = contentHash };
                    }
                }
                default:
                    throw new McpToolException(McpNodeDevelopmentDiagnostics.MalformedArguments, "'mode' must be 'start' or 'check'.");
            }
        }

        // ---- test-node -------------------------------------------------------------------------

        internal static JObject TestNode(string projectRoot, JObject args)
        {
            var expectedContentHash = RequireString(args, "expectedContentHash");
            RequireFreshHash(projectRoot, expectedContentHash);

            if (!GeneratedNodeReflectionHarness.TryFindShardType(StagingAssemblyName, out var shardType, out var reason)
                || !GeneratedNodeReflectionHarness.TryReflectMetadata(shardType, out var reflection, out reason))
            {
                return new JObject { ["valid"] = false, ["reason"] = reason };
            }

            if (!GeneratedNodeReflectionHarness.TryBuildRegistry(reflection, out _, out reason))
            {
                return new JObject { ["valid"] = false, ["reason"] = reason };
            }

            return new JObject
            {
                ["valid"] = true,
                ["shardId"] = reflection.ShardId,
                ["contentHash"] = expectedContentHash,
                ["scopeNote"] = "Proves compiled metadata is structurally valid and registry-materializable. Does not prove dispatch execution -- see P6-022.",
            };
        }

        // ---- apply-node ------------------------------------------------------------------------

        internal static JObject ApplyNode(string projectRoot, JObject args)
        {
            var expectedContentHash = RequireString(args, "expectedContentHash");
            var destinationRelativePath = RequireString(args, "destinationPath");
            RequireFreshHash(projectRoot, expectedContentHash);

            if (!GeneratedNodeReflectionHarness.TryFindShardType(StagingAssemblyName, out var shardType, out var reason)
                || !GeneratedNodeReflectionHarness.TryReflectMetadata(shardType, out var reflection, out reason)
                || !GeneratedNodeReflectionHarness.TryBuildRegistry(reflection, out _, out reason))
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.TestFailed, "Refusing to apply: the staged node is not currently compile-clean and registry-valid (" + reason + "). Re-run analyze-and-compile-node/test-node.");
            }

            string[] moved;
            try
            {
                moved = StagingSlot.MoveTo(projectRoot, destinationRelativePath).ToArray();
            }
            catch (InvalidOperationException ex)
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.ApplyDestinationExists, ex.Message);
            }

            return new JObject
            {
                ["applied"] = true,
                ["destination"] = destinationRelativePath,
                ["shardId"] = reflection.ShardId,
                ["files"] = new JArray(moved.Select(Path.GetFileName)),
            };
        }

        // ---- shared plumbing --------------------------------------------------------------------

        private static void RequireFreshHash(string projectRoot, string expectedContentHash)
        {
            var actualHash = ComputeStagedContentHash(projectRoot);
            if (!string.Equals(actualHash, expectedContentHash, StringComparison.Ordinal))
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.NoPendingGeneration, "The staged content has changed since the supplied content hash was produced (or there is no pending generation) -- re-run analyze-and-compile-node.");
            }
        }

        private static string ComputeStagedContentHash(string projectRoot)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var path in StagingSlot.ListStagedFiles(projectRoot))
            {
                builder.Append(Path.GetFileName(path)).Append('\n').Append(File.ReadAllText(path)).Append('\n');
            }

            return StableHash.Sha256Hex(builder.ToString());
        }

        private static JArray ExtractDiagnosticLines(string logTail)
        {
            var array = new JArray();
            if (logTail == null)
            {
                return array;
            }

            foreach (var line in logTail.Split('\n'))
            {
                if (line.Contains("error AIBT50") || line.Contains("error CS"))
                {
                    array.Add(line.Trim());
                }
            }

            return array;
        }

        private static ConditionNodeSpec ReadConditionSpec(JObject args)
        {
            var typeId = RequireString(args, "typeId");
            var typeName = TypeNameFromId(typeId);
            return new ConditionNodeSpec
            {
                TypeId = typeId,
                Version = OptionalUInt(args, "version") ?? 1u,
                NodeTypeName = typeName,
                ConfigTypeName = typeName + "Config",
                ShardTypeName = typeName + "Shard",
                ShardId = OptionalString(args, "shardId") ?? typeId + "-shard",
                ShardVersion = OptionalUInt(args, "shardVersion") ?? 1u,
                Namespace = RequireString(args, "namespace"),
                BlackboardReadKey = RequireString(args, "blackboardReadKey"),
                BlackboardReadType = ParseValueType(RequireString(args, "blackboardReadType")),
                AsObserverCondition = OptionalBool(args, "asObserverCondition") ?? false,
                Summary = RequireString(args, "summary"),
                Category = RequireString(args, "category"),
                WhenToUse = RequireString(args, "whenToUse"),
                WhenNotToUse = RequireString(args, "whenNotToUse"),
                ExampleKey = OptionalString(args, "exampleKey") ?? typeId,
            };
        }

        private static ActionNodeSpec ReadActionSpec(JObject args)
        {
            var typeId = RequireString(args, "typeId");
            var typeName = TypeNameFromId(typeId);
            return new ActionNodeSpec
            {
                TypeId = typeId,
                Version = OptionalUInt(args, "version") ?? 1u,
                NodeTypeName = typeName,
                ConfigTypeName = typeName + "Config",
                MemoryTypeName = typeName + "Memory",
                ShardTypeName = typeName + "Shard",
                ShardId = OptionalString(args, "shardId") ?? typeId + "-shard",
                ShardVersion = OptionalUInt(args, "shardVersion") ?? 1u,
                Namespace = RequireString(args, "namespace"),
                BlackboardReadKey = RequireString(args, "blackboardReadKey"),
                BlackboardReadType = ParseValueType(RequireString(args, "blackboardReadType")),
                BlackboardWriteKey = RequireString(args, "blackboardWriteKey"),
                BlackboardWriteType = ParseValueType(RequireString(args, "blackboardWriteType")),
                CommandKey = RequireString(args, "commandKey"),
                CommandType = ParseValueType(RequireString(args, "commandType")),
                AsyncOperationKey = RequireString(args, "asyncOperationKey"),
                AsyncStartType = ParseValueType(RequireString(args, "asyncStartType")),
                AsyncCompletionType = ParseValueType(RequireString(args, "asyncCompletionType")),
                CompletionKey = RequireString(args, "completionKey"),
                CompletionType = ParseValueType(RequireString(args, "asyncCompletionType")),
                Summary = RequireString(args, "summary"),
                Category = RequireString(args, "category"),
                WhenToUse = RequireString(args, "whenToUse"),
                WhenNotToUse = RequireString(args, "whenNotToUse"),
                ExampleKey = OptionalString(args, "exampleKey") ?? typeId,
            };
        }

        private static NodeValueType ParseValueType(string value)
        {
            switch (value)
            {
                case "Bool": return NodeValueType.Bool;
                case "Int32": return NodeValueType.Int32;
                case "UInt32": return NodeValueType.UInt32;
                case "Float32": return NodeValueType.Float32;
                case "Float64": return NodeValueType.Float64;
                default: throw new McpToolException(McpNodeDevelopmentDiagnostics.MalformedArguments, "'" + value + "' is not a supported blackboard value type. Supported: Bool, Int32, UInt32, Float32, Float64.");
            }
        }

        private static string TypeNameFromId(string typeId)
        {
            var lastSegment = typeId.Split('.').Last();
            var parts = lastSegment.Split('-');
            var builder = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length == 0) continue;
                builder.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1));
            }

            builder.Append("Node");
            return builder.ToString();
        }

        private static string RequireString(JObject json, string property)
        {
            var value = json[property]?.Value<string>();
            if (string.IsNullOrEmpty(value))
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.MalformedArguments, "Missing required string property '" + property + "'.");
            }

            return value;
        }

        private static string OptionalString(JObject json, string property) => json[property]?.Value<string>();

        private static bool? OptionalBool(JObject json, string property) => json[property]?.Value<bool>();

        private static uint? OptionalUInt(JObject json, string property)
        {
            var token = json[property];
            return token == null ? (uint?)null : token.Value<uint>();
        }

        private static long RequireLong(JObject json, string property)
        {
            var token = json[property];
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.MalformedArguments, "Missing required integer property '" + property + "'.");
            }

            return token.Value<long>();
        }
    }
}
