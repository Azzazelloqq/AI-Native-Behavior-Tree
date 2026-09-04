using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    // Reference-executor counterpart of Authoring/Registry/Generated/BuiltInLeaves/ (P7-028). Unlike
    // a project extension, an "aibt.core." leaf's reference manifest is compile-time enforced
    // (RuntimeBuiltInCatalogAuthorityVerifier / AIBT.CodeGen's AIBT5012 catalog-handshake check) to
    // serialize to the EXACT SAME canonical JSON as the [AibtBurstNode] shard derives for the same
    // type from its own attributes -- summary/category/whenToUse/whenNotToUse text, parameter names,
    // configuration byte layout, and memory size/alignment must all match verbatim. Every field below
    // was cross-checked against Authoring/Registry/Generated/BuiltInLeaves/Runtime/BuiltInLeafNodes.cs
    // live through the generator's own AibtGeneratedMetadata output (Planning~/Evidence/P7-028/).
    public static class BuiltInLeafManifests
    {
        public const string WaitTypeId = "aibt.stdlib.wait";
        public const string RandomConditionTypeId = "aibt.stdlib.random-condition";

        private static readonly IReadOnlyList<IReferenceLeafBehaviorProvider> Providers = Array.AsReadOnly(
            new IReferenceLeafBehaviorProvider[]
            {
                new WaitProvider(),
                new RandomConditionProvider(),
            });

        public static IReadOnlyList<IReferenceLeafBehaviorProvider> All => Providers;

        private sealed class WaitProvider : IReferenceLeafBehaviorProvider
        {
            public NodeManifest Manifest { get; } = new NodeManifest(
                WaitTypeId,
                1,
                "Remains running for a configured number of ticks, then succeeds.",
                "Core/Actions",
                NodeBehaviorKind.Action,
                "Use for a simple tick-counted delay that does not depend on wall-clock time.",
                "Do not use when a wall-clock duration is required -- use aibt.core.timeout/aibt.core.cooldown instead.",
                NodeExecutionDomain.Burst,
                true,
                new[] { new NodeParameterContract("ticks", NodeParameterType.UInt32, true) },
                new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Running, NodeStatus.Success },
                new NodeMemoryDescriptor(4, 4, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("ticks", 0, 4, 4) }),
                NodeCancellationMode.AbortOnly,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample(
                    "stdlib-wait",
                    "{\"ticks\":0}",
                    "Remains running for a configured number of ticks, then succeeds.") });

            public IReferenceLeafBehavior CreateBehavior() => new WaitBehavior();
        }

        private sealed class WaitBehavior : IReferenceLeafBehavior
        {
            private uint _elapsed;

            public void Enter(ref ReferenceLeafContext context) => _elapsed = 0;

            public NodeStatus Tick(ref ReferenceLeafContext context)
            {
                var ticks = ReadUInt32(context.Configuration, 0);
                _elapsed++;
                return _elapsed >= ticks ? NodeStatus.Success : NodeStatus.Running;
            }

            public void Abort(ref ReferenceLeafContext context, NodeAbortReason reason) { }

            public void Exit(ref ReferenceLeafContext context, NodeExitReason reason) { }
        }

        private sealed class RandomConditionProvider : IReferenceLeafBehaviorProvider
        {
            public NodeManifest Manifest { get; } = new NodeManifest(
                RandomConditionTypeId,
                1,
                "Succeeds with a configured percentage probability, drawn from the per-instance random stream.",
                "Core/Conditions",
                NodeBehaviorKind.Condition,
                "Use for a lightweight probabilistic gate.",
                "Do not use when the same tree instance must reproduce an identical outcome sequence on the reference executor -- its managed System.Random draw is not bit-identical to this native stream.",
                NodeExecutionDomain.Burst,
                false,
                new[] { new NodeParameterContract("success-chance-percent", NodeParameterType.UInt32, true) },
                new NodeChildPolicy(0, 0, true),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Failure, NodeStatus.Success },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                new NodeConfigurationDescriptor(4, 4, new[] { new NodeConfigurationField("success-chance-percent", 0, 4, 4) }),
                NodeCancellationMode.NotApplicable,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample(
                    "stdlib-random-condition",
                    "{\"success-chance-percent\":0}",
                    "Succeeds with a configured percentage probability, drawn from the per-instance random stream.") });

            public IReferenceLeafBehavior CreateBehavior() => new RandomConditionBehavior();
        }

        private sealed class RandomConditionBehavior : IReferenceLeafBehavior
        {
            private readonly Random _random = new Random();

            public void Enter(ref ReferenceLeafContext context) { }

            public NodeStatus Tick(ref ReferenceLeafContext context)
            {
                var successChancePercent = ReadUInt32(context.Configuration, 0);
                var sample = _random.NextDouble() * 100.0;
                return sample < successChancePercent ? NodeStatus.Success : NodeStatus.Failure;
            }

            public void Abort(ref ReferenceLeafContext context, NodeAbortReason reason) { }

            public void Exit(ref ReferenceLeafContext context, NodeExitReason reason) { }
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset)
            => (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24));
    }
}
