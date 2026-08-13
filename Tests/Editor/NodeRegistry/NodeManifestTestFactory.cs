using System;
using System.Collections.Generic;

namespace AIBT.Tests.Editor.NodeRegistry
{
    internal static class NodeManifestTestFactory
    {
        internal static Authoring.NodeManifest Create(
            string typeId,
            uint version = 1,
            uint configurationOffset = 0,
            IEnumerable<string> reads = null,
            IEnumerable<string> writes = null,
            IEnumerable<string> sideEffects = null,
            bool omitPacking = false,
            uint? configurationSize = null,
            byte? configurationAlignment = null,
            NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation)
        {
            var parameters = new[]
            {
                new Authoring.NodeParameterContract("enabled", Authoring.NodeParameterType.Boolean, true),
            };
            var configuration = omitPacking
                ? new Authoring.NodeConfigurationDescriptor(0, 1, Array.Empty<Authoring.NodeConfigurationField>())
                : new Authoring.NodeConfigurationDescriptor(
                    configurationSize ?? (configurationOffset == 0 ? 1u : 8u),
                    configurationAlignment ?? (configurationOffset == 0 ? (byte)1 : (byte)4),
                    new[] { new Authoring.NodeConfigurationField("enabled", configurationOffset, 1, 1) });
            var childPolicy = new Authoring.NodeChildPolicy(0, 0, true);
            return new Authoring.NodeManifest(
                typeId,
                version,
                "A test manifest.",
                "Test",
                Authoring.NodeBehaviorKind.Action,
                "Use in registry tests.",
                "Do not use in production.",
                Authoring.NodeExecutionDomain.Burst,
                true,
                parameters,
                childPolicy,
                reads ?? Array.Empty<string>(),
                writes ?? Array.Empty<string>(),
                sideEffects ?? Array.Empty<string>(),
                new[] { NodeStatus.Success },
                new Authoring.NodeMemoryDescriptor(0, 1, memoryLifetime),
                configuration,
                Authoring.NodeCancellationMode.AbortOnly,
                Authoring.NodeCostHint.Trivial,
                new[] { new Authoring.NodeManifestExample("Success", "{\"enabled\":true}", "Returns success.") });
        }
    }
}
