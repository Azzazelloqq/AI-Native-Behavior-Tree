using System;
using System.Text;

namespace AIBT.Mcp.NodeDevelopment
{
    /// <summary>Built-in scalar value types this card's templates support, per burst-node-abi-v1.md's closed allowlist.</summary>
    internal enum NodeValueType
    {
        Bool,
        Int32,
        UInt32,
        Float32,
        Float64,
    }

    internal static class NodeValueTypeExtensions
    {
        internal static string AbiTypeId(this NodeValueType value)
        {
            switch (value)
            {
                case NodeValueType.Bool: return "Bool";
                case NodeValueType.Int32: return "Int32";
                case NodeValueType.UInt32: return "UInt32";
                case NodeValueType.Float32: return "Float32";
                case NodeValueType.Float64: return "Float64";
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        internal static string CSharpType(this NodeValueType value)
        {
            switch (value)
            {
                case NodeValueType.Bool: return "bool";
                case NodeValueType.Int32: return "int";
                case NodeValueType.UInt32: return "uint";
                case NodeValueType.Float32: return "float";
                case NodeValueType.Float64: return "double";
                default: throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }

    /// <summary>Typed template input for a Condition node, mirroring ThresholdConditionNode's own shape.</summary>
    internal sealed class ConditionNodeSpec
    {
        internal string TypeId { get; set; }
        internal uint Version { get; set; } = 1u;
        internal string NodeTypeName { get; set; }
        internal string ConfigTypeName { get; set; }
        internal string ShardTypeName { get; set; }
        internal string ShardId { get; set; }
        internal uint ShardVersion { get; set; } = 1u;
        internal string Namespace { get; set; }

        internal string BlackboardReadKey { get; set; }
        internal NodeValueType BlackboardReadType { get; set; }
        internal string ThresholdFieldName { get; set; } = "Minimum";
        internal bool AsObserverCondition { get; set; }

        internal string Summary { get; set; }
        internal string Category { get; set; }
        internal string WhenToUse { get; set; }
        internal string WhenNotToUse { get; set; }
        internal string ExampleKey { get; set; }
        internal string CostHint { get; set; } = "BurstNodeCost.Trivial";
    }

    /// <summary>Typed template input for an Action node, mirroring AsyncWriteActionNode's own shape.</summary>
    internal sealed class ActionNodeSpec
    {
        internal string TypeId { get; set; }
        internal uint Version { get; set; } = 1u;
        internal string NodeTypeName { get; set; }
        internal string ConfigTypeName { get; set; }
        internal string MemoryTypeName { get; set; }
        internal string ShardTypeName { get; set; }
        internal string ShardId { get; set; }
        internal uint ShardVersion { get; set; } = 1u;
        internal string Namespace { get; set; }

        internal string BlackboardReadKey { get; set; }
        internal NodeValueType BlackboardReadType { get; set; }
        internal string BlackboardWriteKey { get; set; }
        internal NodeValueType BlackboardWriteType { get; set; }
        internal string CommandKey { get; set; }
        internal NodeValueType CommandType { get; set; }
        internal string AsyncOperationKey { get; set; }
        internal NodeValueType AsyncStartType { get; set; }
        internal NodeValueType AsyncCompletionType { get; set; }
        internal string CompletionKey { get; set; }
        internal NodeValueType CompletionType { get; set; }

        internal string Summary { get; set; }
        internal string Category { get; set; }
        internal string WhenToUse { get; set; }
        internal string WhenNotToUse { get; set; }
        internal string ExampleKey { get; set; }
        internal string CostHint { get; set; } = "BurstNodeCost.Low";
    }

    /// <summary>
    /// Generates node source text from the two maintained templates
    /// (Samples~/BurstNodes/Runtime/PublicBurstNodeSample.cs's own ThresholdConditionNode/
    /// AsyncWriteActionNode shapes) via plain string composition -- no codegen mechanism of its
    /// own, per this card's own Forbidden changes. The output is real, compilable v1-ABI-shaped
    /// C# source; CodeGen~/AIBT.CodeGen (via the packaged analyzer) is the only thing that ever
    /// validates or generates dispatch glue from it.
    /// </summary>
    internal static class NodeTemplateGenerator
    {
        internal static string GenerateCondition(ConditionNodeSpec spec)
        {
            var builder = new StringBuilder();
            builder.Append("using AIBT;\nusing AIBT.Burst;\n\n");
            builder.Append("namespace ").Append(spec.Namespace).Append("\n{\n");
            builder.Append("    [AibtCatalogShard(\"").Append(spec.ShardId).Append("\", ").Append(spec.ShardVersion).Append("u)]\n");
            builder.Append("    public partial struct ").Append(spec.ShardTypeName).Append(" { }\n\n");

            builder.Append("    public partial struct ").Append(spec.ConfigTypeName).Append("\n    {\n");
            builder.Append("        [AibtConfigField(\"current\", \"GeneratedHandle\", 1u)]\n");
            builder.Append("        [AibtBlackboardBinding(\"current\", BurstBlackboardAccess.Read, BlackboardScope.Tree, \"")
                .Append(spec.BlackboardReadType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public BlackboardReadHandle<").Append(spec.BlackboardReadType.CSharpType()).Append("> Current;\n\n");
            builder.Append("        [AibtConfigField(\"threshold\", \"").Append(spec.BlackboardReadType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public ").Append(spec.BlackboardReadType.CSharpType()).Append(' ').Append(spec.ThresholdFieldName).Append(";\n");
            builder.Append("    }\n\n");

            builder.Append("    public partial struct ").Append(spec.ConfigTypeName).Append("Memory { }\n\n");

            builder.Append("    [AibtNodeDocumentation(\n");
            builder.Append("        \"").Append(Escape(spec.Summary)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.Category)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.WhenToUse)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.WhenNotToUse)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.ExampleKey)).Append("\")]\n");
            if (spec.AsObserverCondition) builder.Append("    [AibtObserverCondition]\n");
            builder.Append("    [AibtBurstNode(\n");
            builder.Append("        \"").Append(spec.TypeId).Append("\", ").Append(spec.Version).Append("u, BurstNodeKind.Condition,\n");
            builder.Append("        typeof(").Append(spec.ConfigTypeName).Append("), typeof(").Append(spec.ConfigTypeName).Append("Memory), NodeMemoryLifetime.Activation,\n");
            builder.Append("        true, BurstCancellationMode.NotApplicable, ").Append(spec.CostHint).Append(",\n");
            builder.Append("        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]\n");
            builder.Append("    public partial struct ").Append(spec.NodeTypeName).Append("\n    {\n");
            builder.Append("        public static void Enter(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.ConfigTypeName).Append("Memory memory,\n");
            builder.Append("            ref BurstEnterContext context) { }\n\n");
            builder.Append("        public static NodeStatus Tick(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.ConfigTypeName).Append("Memory memory,\n");
            builder.Append("            ref BurstTickContext context)\n        {\n");
            builder.Append("            var result = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryRead(ref context, config.Current, out var current);\n");
            builder.Append("            return result == BurstContextResult.Success && current >= config.").Append(spec.ThresholdFieldName).Append('\n');
            builder.Append("                ? NodeStatus.Success\n                : NodeStatus.Failure;\n        }\n\n");
            builder.Append("        public static void Abort(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.ConfigTypeName).Append("Memory memory,\n");
            builder.Append("            ref BurstAbortContext context,\n            BurstNodeAbortReason reason) { }\n\n");
            builder.Append("        public static void Exit(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.ConfigTypeName).Append("Memory memory,\n");
            builder.Append("            ref BurstExitContext context,\n            BurstNodeExitReason reason) { }\n");
            if (spec.AsObserverCondition)
            {
                builder.Append("\n        public static ConditionResult Evaluate(\n");
                builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
                builder.Append("            ref BurstObserverContext context)\n        {\n");
                builder.Append("            var result = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryRead(ref context, config.Current, out var current);\n");
                builder.Append("            return result == BurstContextResult.Success && current >= config.").Append(spec.ThresholdFieldName).Append('\n');
                builder.Append("                ? ConditionResult.Success\n                : ConditionResult.Failure;\n        }\n");
            }
            builder.Append("    }\n}\n");
            return builder.ToString();
        }

        internal static string GenerateAction(ActionNodeSpec spec)
        {
            var builder = new StringBuilder();
            builder.Append("using AIBT;\nusing AIBT.Burst;\n\n");
            builder.Append("namespace ").Append(spec.Namespace).Append("\n{\n");
            builder.Append("    [AibtCatalogShard(\"").Append(spec.ShardId).Append("\", ").Append(spec.ShardVersion).Append("u)]\n");
            builder.Append("    public partial struct ").Append(spec.ShardTypeName).Append(" { }\n\n");

            builder.Append("    public partial struct ").Append(spec.ConfigTypeName).Append("\n    {\n");
            builder.Append("        [AibtConfigField(\"source\", \"GeneratedHandle\", 1u)]\n");
            builder.Append("        [AibtBlackboardBinding(\"source\", BurstBlackboardAccess.Read, BlackboardScope.Tree, \"")
                .Append(spec.BlackboardReadType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public BlackboardReadHandle<").Append(spec.BlackboardReadType.CSharpType()).Append("> Source;\n\n");
            builder.Append("        [AibtConfigField(\"destination\", \"GeneratedHandle\", 1u)]\n");
            builder.Append("        [AibtBlackboardBinding(\"destination\", BurstBlackboardAccess.Write, BlackboardScope.Tree, \"")
                .Append(spec.BlackboardWriteType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public BlackboardWriteHandle<").Append(spec.BlackboardWriteType.CSharpType()).Append("> Destination;\n\n");
            builder.Append("        [AibtConfigField(\"effect\", \"GeneratedHandle\", 1u)]\n");
            builder.Append("        [AibtCommandBinding(\"effect\", \"").Append(spec.CommandType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public CommandHandle<").Append(spec.CommandType.CSharpType()).Append("> Effect;\n\n");
            builder.Append("        [AibtConfigField(\"operation\", \"GeneratedHandle\", 1u)]\n");
            builder.Append("        [AibtAsyncOperationBinding(\"operation\", \"").Append(spec.AsyncStartType.AbiTypeId()).Append("\", 1u, \"")
                .Append(spec.AsyncCompletionType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public AsyncOperationHandle<").Append(spec.AsyncStartType.CSharpType()).Append(", ")
                .Append(spec.AsyncCompletionType.CSharpType()).Append("> Operation;\n\n");
            builder.Append("        [AibtConfigField(\"completion\", \"GeneratedHandle\", 1u)]\n");
            builder.Append("        [AibtCompletionBinding(\"completion\", \"").Append(spec.CompletionType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public CompletionHandle<").Append(spec.CompletionType.CSharpType()).Append("> Completion;\n");
            builder.Append("    }\n\n");

            builder.Append("    public partial struct ").Append(spec.MemoryTypeName).Append("\n    {\n");
            builder.Append("        [AibtMemoryField(\"operation-id\", \"OperationId\", 1u)]\n");
            builder.Append("        public OperationId OperationId;\n\n");
            builder.Append("        [AibtMemoryField(\"started\", \"Bool\", 1u)]\n");
            builder.Append("        public bool Started;\n\n");
            builder.Append("        [AibtMemoryField(\"value\", \"").Append(spec.AsyncStartType.AbiTypeId()).Append("\", 1u)]\n");
            builder.Append("        public ").Append(spec.AsyncStartType.CSharpType()).Append(" Value;\n");
            builder.Append("    }\n\n");

            builder.Append("    [AibtNodeDocumentation(\n");
            builder.Append("        \"").Append(Escape(spec.Summary)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.Category)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.WhenToUse)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.WhenNotToUse)).Append("\",\n");
            builder.Append("        \"").Append(Escape(spec.ExampleKey)).Append("\")]\n");
            builder.Append("    [AibtBurstNode(\n");
            builder.Append("        \"").Append(spec.TypeId).Append("\", ").Append(spec.Version).Append("u, BurstNodeKind.Action,\n");
            builder.Append("        typeof(").Append(spec.ConfigTypeName).Append("), typeof(").Append(spec.MemoryTypeName).Append("), NodeMemoryLifetime.Activation,\n");
            builder.Append("        true, BurstCancellationMode.Command, ").Append(spec.CostHint).Append(",\n");
            builder.Append("        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure | BurstNodeStatusMask.Running)]\n");
            builder.Append("    public partial struct ").Append(spec.NodeTypeName).Append("\n    {\n");

            builder.Append("        public static void Enter(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.MemoryTypeName).Append(" memory,\n");
            builder.Append("            ref BurstEnterContext context)\n        {\n");
            builder.Append("            memory.Started = false;\n            memory.OperationId = default;\n        }\n\n");

            builder.Append("        public static NodeStatus Tick(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.MemoryTypeName).Append(" memory,\n");
            builder.Append("            ref BurstTickContext context)\n        {\n");
            builder.Append("            if (!memory.Started)\n            {\n");
            builder.Append("                var read = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryRead(ref context, config.Source, out memory.Value);\n");
            builder.Append("                if (read != BurstContextResult.Success)\n                    return NodeStatus.Failure;\n\n");
            builder.Append("                var write = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryWrite(ref context, config.Destination, in memory.Value);\n");
            builder.Append("                var emit = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryEmit(ref context, config.Effect, in memory.Value);\n");
            builder.Append("                var start = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryStart(\n");
            builder.Append("                    ref context, config.Operation, in memory.Value, in memory.Value, out memory.OperationId);\n");
            builder.Append("                if (write != BurstContextResult.Success\n");
            builder.Append("                    || emit != BurstContextResult.Success\n");
            builder.Append("                    || start != BurstContextResult.Success)\n                    return NodeStatus.Failure;\n\n");
            builder.Append("                memory.Started = true;\n                return NodeStatus.Running;\n            }\n\n");
            builder.Append("            var consume = ").Append(spec.ShardTypeName).Append(".BurstAccess.TryConsume(\n");
            builder.Append("                ref context, config.Completion, memory.OperationId, out var outcome, out var completionValue);\n");
            builder.Append("            if (consume == BurstContextResult.StaleCompletion)\n                return NodeStatus.Running;\n");
            builder.Append("            if (consume != BurstContextResult.Success || outcome != BurstCompletionOutcome.Succeeded)\n                return NodeStatus.Failure;\n\n");
            builder.Append("            memory.Value = completionValue;\n");
            builder.Append("            return ").Append(spec.ShardTypeName).Append(".BurstAccess.TryWrite(\n");
            builder.Append("                ref context, config.Destination, in memory.Value) == BurstContextResult.Success\n");
            builder.Append("                ? NodeStatus.Success\n                : NodeStatus.Failure;\n        }\n\n");

            builder.Append("        public static void Abort(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.MemoryTypeName).Append(" memory,\n");
            builder.Append("            ref BurstAbortContext context,\n            BurstNodeAbortReason reason)\n        {\n");
            builder.Append("            if (memory.Started)\n");
            builder.Append("                ").Append(spec.ShardTypeName).Append(".BurstAccess.TryCancel(\n");
            builder.Append("                    ref context, config.Operation, memory.OperationId, in memory.Value);\n        }\n\n");

            builder.Append("        public static void Exit(\n");
            builder.Append("            in ").Append(spec.ConfigTypeName).Append(" config,\n");
            builder.Append("            ref ").Append(spec.MemoryTypeName).Append(" memory,\n");
            builder.Append("            ref BurstExitContext context,\n            BurstNodeExitReason reason) { }\n");
            builder.Append("    }\n}\n");
            return builder.ToString();
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
