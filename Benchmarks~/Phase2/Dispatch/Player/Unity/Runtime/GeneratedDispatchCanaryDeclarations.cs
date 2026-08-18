using AIBT.Burst;

namespace AIBT.Tests.Runtime.NativeExecution.Dispatch
{
    using AIBT.Tests.CodeGen.Generation;

    [AibtCatalogSet("aibt.tests.dispatch-canary", 1u, typeof(GenerationShard))]
    public static partial class GeneratedDispatchCanaryCatalog
    {
        internal static BurstCatalogHandshake HandshakeForPlayerAot()
        {
            return new BurstCatalogHandshake(
                2u,
                Fingerprint,
                NodeRegistryFingerprint,
                1u,
                1u,
                ConfigurationLayoutFingerprint,
                MemoryLayoutFingerprint,
                AccessLayoutFingerprint);
        }
    }
}
