using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;

namespace AIBT.Tests.Integration.SemanticSlice
{
    internal static class SemanticSliceNodeContracts
    {
        internal const string AlertConditionTypeId = "aibt.test.alert-condition";
        internal const string RaiseAlertTypeId = "aibt.test.raise-alert";
        internal const string AsyncActionTypeId = "aibt.test.async-action";
        internal const string AlertKey = "alert";
        internal const string AsyncStartCommandType = "aibt.test.command.async-start";
        internal const string AsyncCancelCommandType = "aibt.test.command.async-cancel";

        internal static NodeRegistry CreateAuthoringRegistry()
        {
            var registrations = new List<Registration>();
            for (var index = 0; index < BuiltInNodeManifests.All.Count; index++)
                registrations.Add(new Registration(BuiltInNodeManifests.All[index], NodeManifestSource.BuiltIn));
            for (var index = 0; index < ReferenceFixtureNodeManifests.All.Count; index++)
                registrations.Add(new Registration(ReferenceFixtureNodeManifests.All[index], NodeManifestSource.TestFixture));
            registrations.Add(new Registration(CreateAlertConditionManifest(), NodeManifestSource.TestFixture));
            registrations.Add(new Registration(CreateRaiseAlertManifest(), NodeManifestSource.TestFixture));
            registrations.Add(new Registration(CreateAsyncActionManifest(), NodeManifestSource.TestFixture));
            registrations.Sort((left, right) => string.Compare(
                left.Manifest.TypeId,
                right.Manifest.TypeId,
                StringComparison.Ordinal));

            var entries = registrations.Select(registration => new NodeRegistryEntry(
                registration.Manifest,
                StableHash.Fnv1A64(registration.Manifest.TypeId),
                registration.Source,
                new NodeHandlerBindingContract(
                    "aibt.reference." + registration.Manifest.TypeId.Substring("aibt.".Length),
                    registration.Manifest.Version,
                    registration.Manifest.ExecutionDomain)))
                .ToArray();
            var hash = StableHash.Sha256Hex(NodeManifestCanonicalJson.SerializeRegistryUtf8(entries));
            return new NodeRegistry(
                entries,
                hash,
                NodeRegistryCapabilityFlags.Burst | NodeRegistryCapabilityFlags.ReferenceHandlerBindings);
        }

        internal static ReferenceLeafRegistry CreateLeafRegistry()
        {
            var commandContract = new ReferenceAsyncCommandContract(
                new CommandType(StableHash.Fnv1A64(AsyncStartCommandType), 1),
                new CommandType(StableHash.Fnv1A64(AsyncCancelCommandType), 1));
            return new ReferenceLeafRegistry(new[]
            {
                Binding(ReferenceFixtureNodeManifests.SuccessTypeId, new ConstantReferenceLeafHandler(NodeStatus.Success)),
                Binding(ReferenceFixtureNodeManifests.FailureTypeId, new ConstantReferenceLeafHandler(NodeStatus.Failure)),
                Binding(ReferenceFixtureNodeManifests.RunningTypeId, new ConstantReferenceLeafHandler(NodeStatus.Running)),
                Binding(AlertConditionTypeId, new AlertConditionHandler()),
                Binding(RaiseAlertTypeId, new RaiseAlertHandler()),
                Binding(AsyncActionTypeId, new ReferenceAsyncActionHandler(commandContract)),
            });
        }

        internal static ReferenceObserverConditionRegistry CreateObserverRegistry()
            => new ReferenceObserverConditionRegistry(new[]
            {
                new ReferenceObserverConditionBinding(
                    StableHash.Fnv1A64(AlertConditionTypeId),
                    1,
                    new AlertConditionEvaluator()),
            });

        private static ReferenceLeafBinding Binding(string typeId, IReferenceLeafHandler handler)
            => new ReferenceLeafBinding(StableHash.Fnv1A64(typeId), 1, handler);

        private static NodeManifest CreateAlertConditionManifest()
            => Leaf(
                AlertConditionTypeId,
                NodeBehaviorKind.Condition,
                new[] { AlertKey },
                Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                NodeCancellationMode.NotApplicable);

        private static NodeManifest CreateRaiseAlertManifest()
            => Leaf(
                RaiseAlertTypeId,
                NodeBehaviorKind.Action,
                Array.Empty<string>(),
                new[] { AlertKey },
                new[] { NodeStatus.Running },
                new NodeMemoryDescriptor(0, 1, NodeMemoryLifetime.Activation),
                NodeCancellationMode.AbortOnly);

        private static NodeManifest CreateAsyncActionManifest()
            => Leaf(
                AsyncActionTypeId,
                NodeBehaviorKind.Action,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { NodeStatus.Success, NodeStatus.Failure, NodeStatus.Running },
                new NodeMemoryDescriptor(16, 8, NodeMemoryLifetime.Activation),
                NodeCancellationMode.Command);

        private static NodeManifest Leaf(
            string typeId,
            NodeBehaviorKind kind,
            IEnumerable<string> reads,
            IEnumerable<string> writes,
            IEnumerable<NodeStatus> statuses,
            NodeMemoryDescriptor memory,
            NodeCancellationMode cancellation)
            => new NodeManifest(
                typeId,
                1,
                "Phase 1 semantic-slice fixture.",
                "Integration fixture",
                kind,
                "Use only in P1-018 integration tests.",
                "Do not ship in production registries.",
                NodeExecutionDomain.Burst,
                true,
                Array.Empty<NodeParameterContract>(),
                new NodeChildPolicy(0, 0, true),
                reads,
                writes,
                Array.Empty<string>(),
                statuses,
                memory,
                new NodeConfigurationDescriptor(0, 1, Array.Empty<NodeConfigurationField>()),
                cancellation,
                NodeCostHint.Trivial,
                new[] { new NodeManifestExample("Fixture", "{}", "Deterministic integration behavior.") });

        private readonly struct Registration
        {
            internal Registration(NodeManifest manifest, NodeManifestSource source)
            {
                Manifest = manifest;
                Source = source;
            }

            internal NodeManifest Manifest { get; }
            internal NodeManifestSource Source { get; }
        }

        private sealed class AlertConditionHandler : IReferenceLeafHandler
        {
            public void Enter(ref ReferenceNodeContext context) { }

            public NodeStatus Tick(ref ReferenceNodeContext context)
                => Read(ref context) ? NodeStatus.Success : NodeStatus.Failure;

            public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) { }
            public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) { }

            private static bool Read(ref ReferenceNodeContext context)
            {
                if (!context.TryReadBlackboard(0, out var value) || !value.TryGetBool(out var result))
                    throw new InvalidOperationException("Alert condition requires its declared Bool read.");
                return result;
            }
        }

        private sealed class AlertConditionEvaluator : IReferenceObserverConditionEvaluator
        {
            public NodeStatus Evaluate(ref ReferenceObserverConditionContext context)
            {
                if (!context.TryRead(0, out var value) || !value.TryGetBool(out var result))
                    throw new InvalidOperationException("Alert observer requires its declared Bool read.");
                return result ? NodeStatus.Success : NodeStatus.Failure;
            }
        }

        private sealed class RaiseAlertHandler : IReferenceLeafHandler
        {
            public void Enter(ref ReferenceNodeContext context) { }

            public NodeStatus Tick(ref ReferenceNodeContext context)
            {
                if (!context.TryWriteBlackboard(0, BlackboardValue.FromBool(true)))
                    throw new InvalidOperationException("Alert action requires its declared Bool write.");
                return NodeStatus.Running;
            }

            public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) { }
            public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) { }
        }
    }
}
