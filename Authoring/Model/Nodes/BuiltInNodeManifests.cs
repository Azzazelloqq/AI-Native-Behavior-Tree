using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class BuiltInNodeManifests
    {
        public const string MemorySequenceTypeId = "aibt.core.memory-sequence";
        public const string ReactiveSequenceTypeId = "aibt.core.reactive-sequence";
        public const string MemorySelectorTypeId = "aibt.core.memory-selector";
        public const string ReactiveSelectorTypeId = "aibt.core.reactive-selector";
        public const string ParallelTypeId = "aibt.core.parallel";
        public const string InverterTypeId = "aibt.core.inverter";
        public const string SucceederTypeId = "aibt.core.succeeder";
        public const string FailerTypeId = "aibt.core.failer";
        public const string RepeaterTypeId = "aibt.core.repeater";
        public const string TimeoutTypeId = "aibt.core.timeout";
        public const string CooldownTypeId = "aibt.core.cooldown";

        private static readonly IReadOnlyList<NodeManifest> Manifests = Array.AsReadOnly(new[]
        {
            CreateMemorySequence(),
            CreateReactiveSequence(),
            CreateMemorySelector(),
            CreateReactiveSelector(),
            CreateParallel(),
            CreateInverter(),
            CreateSucceeder(),
            CreateFailer(),
            CreateRepeater(),
            CreateTimeout(),
            CreateCooldown(),
        });

        public static IReadOnlyList<NodeManifest> All => Manifests;

        private static NodeManifest CreateMemorySequence()
        {
            return Composite(
                MemorySequenceTypeId,
                "Memory sequence",
                "Run ordered children until one fails or runs, retaining the running child.",
                "Use for ordered multi-step behavior whose completed steps should not be reevaluated.",
                "Do not use when earlier conditions must be checked on every update.",
                new NodeMemoryDescriptor(4, 4, NodeMemoryLifetime.Activation),
                "An empty memory sequence succeeds.");
        }

        private static NodeManifest CreateReactiveSequence()
        {
            return Composite(
                ReactiveSequenceTypeId,
                "Reactive sequence",
                "Reevaluate ordered children from the first child on every eligible update.",
                "Use when earlier conditions must continuously guard a later action.",
                "Do not use when restarting the selected running branch is undesirable.",
                new NodeMemoryDescriptor(4, 4, NodeMemoryLifetime.Activation),
                "An empty reactive sequence succeeds.");
        }

        private static NodeManifest CreateMemorySelector()
        {
            return Composite(
                MemorySelectorTypeId,
                "Memory selector",
                "Run ordered children until one succeeds or runs, retaining the running child.",
                "Use for fallback behavior whose earlier failed choices need not be reevaluated.",
                "Do not use when a higher-priority choice must preempt a running lower-priority choice.",
                new NodeMemoryDescriptor(4, 4, NodeMemoryLifetime.Activation),
                "An empty memory selector fails.");
        }

        private static NodeManifest CreateReactiveSelector()
        {
            return Composite(
                ReactiveSelectorTypeId,
                "Reactive selector",
                "Reevaluate ordered choices from the highest priority on every eligible update.",
                "Use for priority behavior in which earlier choices can replace a running later choice.",
                "Do not use when the chosen branch must run without priority reevaluation.",
                new NodeMemoryDescriptor(4, 4, NodeMemoryLifetime.Activation),
                "An empty reactive selector fails.");
        }

        private static NodeManifest CreateParallel()
        {
            var thresholdCondition = new NodeParameterCondition("policy", "threshold");
            var parameters = new[]
            {
                new NodeParameterContract("policy", NodeParameterType.StringEnum, true, allowedValues: new[]
                {
                    "require-all-success", "require-any-success", "threshold",
                }),
                new NodeParameterContract("successThreshold", NodeParameterType.UInt32, false, 1, requiredWhen: thresholdCondition, forbiddenUnless: thresholdCondition),
                new NodeParameterContract("failureThreshold", NodeParameterType.UInt32, false, 1, requiredWhen: thresholdCondition, forbiddenUnless: thresholdCondition),
                new NodeParameterContract("tieBreak", NodeParameterType.StringEnum, false, allowedValues: new[]
                {
                    "failure-first", "success-first",
                }, requiredWhen: thresholdCondition, forbiddenUnless: thresholdCondition),
            };
            var fields = new[]
            {
                new NodeConfigurationField("policy", 0, 1, 1),
                new NodeConfigurationField("successThreshold", 4, 4, 4),
                new NodeConfigurationField("failureThreshold", 8, 4, 4),
                new NodeConfigurationField("tieBreak", 12, 1, 1),
            };
            var childPolicy = new NodeChildPolicy(1, null, true);
            return Create(
                ParallelTypeId,
                "Parallel",
                NodeBehaviorKind.Composite,
                "Visit each non-terminal child in order and complete according to an explicit policy.",
                "Use when several child behaviors must progress during the same activation.",
                "Do not use to imply simultaneous execution or worker-thread concurrency.",
                parameters,
                childPolicy,
                new NodeMemoryDescriptor(8, 4, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(16, 4, fields),
                NodeCostHint.Medium,
                new NodeManifestExample("Require all", "{\"policy\":\"require-all-success\"}", "Succeeds after every child succeeds and fails after any child fails."));
        }

        private static NodeManifest CreateInverter()
        {
            return Decorator(
                InverterTypeId,
                "Inverter",
                "Invert terminal success and failure while preserving running.",
                "Use to negate a child's terminal meaning.",
                "Do not use when either terminal result must be preserved.",
                Array.Empty<NodeParameterContract>(),
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                EmptyConfiguration(),
                "A successful child makes the inverter fail.");
        }

        private static NodeManifest CreateSucceeder()
        {
            return Decorator(
                SucceederTypeId,
                "Succeeder",
                "Convert either terminal child result to success while preserving running.",
                "Use when child completion, rather than its terminal result, is what matters.",
                "Do not use to hide a failure that callers need to handle.",
                Array.Empty<NodeParameterContract>(),
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                EmptyConfiguration(),
                "A failed child makes the succeeder succeed.",
                possibleStatuses: new[] { NodeStatus.Success, NodeStatus.Running });
        }

        private static NodeManifest CreateFailer()
        {
            return Decorator(
                FailerTypeId,
                "Failer",
                "Convert either terminal child result to failure while preserving running.",
                "Use when child completion must force a fallback path.",
                "Do not use to discard a success that callers need to observe.",
                Array.Empty<NodeParameterContract>(),
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                EmptyConfiguration(),
                "A successful child makes the failer fail.",
                possibleStatuses: new[] { NodeStatus.Failure, NodeStatus.Running });
        }

        private static NodeManifest CreateRepeater()
        {
            var parameters = new[]
            {
                new NodeParameterContract("count", NodeParameterType.UInt32, true, 1),
                new NodeParameterContract("stopOnFailure", NodeParameterType.Boolean, true),
            };
            return Decorator(
                RepeaterTypeId,
                "Repeater",
                "Run a child for a positive finite number of complete iterations.",
                "Use for bounded repetition with explicit failure behavior.",
                "Do not use for an unbounded loop.",
                parameters,
                new NodeMemoryDescriptor(4, 4, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(8, 4, new[]
                {
                    new NodeConfigurationField("count", 0, 4, 4),
                    new NodeConfigurationField("stopOnFailure", 4, 1, 1),
                }),
                "Three successful child iterations complete with success.",
                "{\"count\":3,\"stopOnFailure\":true}");
        }

        private static NodeManifest CreateTimeout()
        {
            var parameters = new[]
            {
                new NodeParameterContract("durationMicroseconds", NodeParameterType.UInt64, true, 1),
                new NodeParameterContract("terminalResult", NodeParameterType.StringEnum, true, allowedValues: new[] { "failure", "success" }),
            };
            return Decorator(
                TimeoutTypeId,
                "Timeout",
                "Abort a running child at a positive injected-clock deadline.",
                "Use to bound how long a child activation may remain running.",
                "Do not use wall-clock time directly or a zero duration.",
                parameters,
                new NodeMemoryDescriptor(8, 8, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(16, 8, new[]
                {
                    new NodeConfigurationField("durationMicroseconds", 0, 8, 8),
                    new NodeConfigurationField("terminalResult", 8, 1, 1),
                }),
                "After one second the running child is aborted and the decorator fails.",
                "{\"durationMicroseconds\":1000000,\"terminalResult\":\"failure\"}");
        }

        private static NodeManifest CreateCooldown()
        {
            var parameters = new[]
            {
                new NodeParameterContract("durationMicroseconds", NodeParameterType.UInt64, true, 1),
                new NodeParameterContract("blockedResult", NodeParameterType.StringEnum, true, allowedValues: new[] { "failure", "success" }),
                new NodeParameterContract("startPolicy", NodeParameterType.StringEnum, true, allowedValues: new[] { "on-enter", "on-successful-exit" }),
            };
            return Decorator(
                CooldownTypeId,
                "Cooldown",
                "Block child entry until the per-instance cooldown deadline has passed.",
                "Use to rate-limit child activation without a tree blackboard key.",
                "Do not use when cooldown state must be shared across tree instances.",
                parameters,
                new NodeMemoryDescriptor(8, 8, NodeMemoryLifetime.Instance),
                new NodeConfigurationDescriptor(16, 8, new[]
                {
                    new NodeConfigurationField("durationMicroseconds", 0, 8, 8),
                    new NodeConfigurationField("blockedResult", 8, 1, 1),
                    new NodeConfigurationField("startPolicy", 9, 1, 1),
                }),
                "A blocked activation fails until one second after child entry.",
                "{\"blockedResult\":\"failure\",\"durationMicroseconds\":1000000,\"startPolicy\":\"on-enter\"}");
        }

        private static NodeManifest Composite(
            string typeId,
            string category,
            string summary,
            string whenToUse,
            string whenNotToUse,
            NodeMemoryDescriptor memory,
            string behavior)
        {
            return Create(
                typeId,
                category,
                NodeBehaviorKind.Composite,
                summary,
                whenToUse,
                whenNotToUse,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, null, true),
                memory,
                EmptyConfiguration(),
                NodeCostHint.Low,
                new NodeManifestExample("Base behavior", "{}", behavior));
        }

        private static NodeManifest Decorator(
            string typeId,
            string category,
            string summary,
            string whenToUse,
            string whenNotToUse,
            IReadOnlyList<NodeParameterContract> parameters,
            NodeMemoryDescriptor memory,
            NodeConfigurationDescriptor configuration,
            string behavior,
            string parametersJson = "{}",
            IReadOnlyList<NodeStatus> possibleStatuses = null)
        {
            return Create(
                typeId,
                category,
                NodeBehaviorKind.Decorator,
                summary,
                whenToUse,
                whenNotToUse,
                parameters,
                new NodeChildPolicy(1, 1, true),
                memory,
                configuration,
                NodeCostHint.Low,
                new NodeManifestExample("Base behavior", parametersJson, behavior),
                possibleStatuses);
        }

        private static NodeManifest Create(
            string typeId,
            string category,
            NodeBehaviorKind kind,
            string summary,
            string whenToUse,
            string whenNotToUse,
            IEnumerable<NodeParameterContract> parameters,
            NodeChildPolicy childPolicy,
            NodeMemoryDescriptor memory,
            NodeConfigurationDescriptor configuration,
            NodeCostHint costHint,
            NodeManifestExample example,
            IReadOnlyList<NodeStatus> possibleStatuses = null)
        {
            return new NodeManifest(
                typeId,
                1,
                summary,
                category,
                kind,
                whenToUse,
                whenNotToUse,
                NodeExecutionDomain.Burst,
                true,
                parameters,
                childPolicy,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                possibleStatuses ?? new[] { NodeStatus.Success, NodeStatus.Failure, NodeStatus.Running },
                memory,
                configuration,
                NodeCancellationMode.AbortOnly,
                costHint,
                new[] { example });
        }

        private static NodeConfigurationDescriptor EmptyConfiguration()
        {
            return new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>());
        }
    }

    internal static class ReferenceFixtureNodeManifests
    {
        internal const string SuccessTypeId = "aibt.test.success";
        internal const string FailureTypeId = "aibt.test.failure";
        internal const string RunningTypeId = "aibt.test.running";

        internal static IReadOnlyList<NodeManifest> All { get; } = Array.AsReadOnly(new[]
        {
            CreateLeaf(SuccessTypeId, new[] { NodeStatus.Success }, "Returns success."),
            CreateLeaf(FailureTypeId, new[] { NodeStatus.Failure }, "Returns failure."),
            CreateLeaf(RunningTypeId, new[] { NodeStatus.Running }, "Remains running."),
        });

        private static NodeManifest CreateLeaf(string typeId, IEnumerable<NodeStatus> statuses, string behavior)
        {
            var childPolicy = new NodeChildPolicy(0, 0, true);
            return new NodeManifest(
                typeId,
                1,
                behavior,
                "Test fixture",
                NodeBehaviorKind.Action,
                "Use only in reference executor behavior tests.",
                "Do not include in production tree documents.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                childPolicy,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                statuses,
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Fixture result", "{}", behavior) });
        }
    }
}
