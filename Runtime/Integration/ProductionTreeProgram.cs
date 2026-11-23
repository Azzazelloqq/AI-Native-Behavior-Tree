using System;
using AIBT.Burst;

namespace AIBT
{
    /// <summary>Shared compiled definition and generated catalog. Dispose after its hosts are destroyed.</summary>
    public sealed class ProductionTreeProgram : IDisposable
    {
        private readonly GeneratedTreeRuntimeDefinitionV2 _definition;
        private readonly GeneratedBurstCatalogV2 _catalog;

        internal ProductionTreeProgram(GeneratedTreeRuntimeDefinitionV2 definition, GeneratedBurstCatalogV2 catalog)
        {
            _definition = definition;
            _catalog = catalog;
        }

        public CompiledProgram Program => _definition.Binding.SemanticProgram;

        /// <summary>P10-009: the definition and catalog the simulation container bootstraps its agents from.</summary>
        internal GeneratedTreeRuntimeDefinitionV2 Definition => _definition;
        internal GeneratedBurstCatalogV2 Catalog => _catalog;

        /// <summary>Bootstraps a host with bounded tracing, optionally enabling its committed-effect queue.</summary>
        public bool TryBootstrap(ProductionTreeHost host, out NativeRuntimeFailureV1 failure, int effectCapacity = 0,
            Func<long> clock = null, NativeTraceChannelCapacityV1? traceCapacity = null)
            => TryBootstrap(host, ProductionTreeHost.DefaultRandomSeed, out failure, effectCapacity, clock, traceCapacity);

        /// <summary>
        /// P11-002: bootstraps a host whose random streams derive from <paramref name="randomSeed"/>
        /// (see <see cref="ProductionTreeHost.RandomSeed"/>); otherwise as the overload without a seed.
        /// The stream identity is automatic (the host's process-wide instance id), so a seed alone is not
        /// repeatable across sessions; pass a <see cref="RandomStreamSeeding"/> with a nonzero identity for
        /// that (P11-012).
        /// </summary>
        public bool TryBootstrap(ProductionTreeHost host, ulong randomSeed, out NativeRuntimeFailureV1 failure, int effectCapacity = 0,
            Func<long> clock = null, NativeTraceChannelCapacityV1? traceCapacity = null)
            => TryBootstrap(host, RandomStreamSeeding.Automatic(randomSeed), out failure, effectCapacity, clock, traceCapacity);

        /// <summary>
        /// P11-012 (ADR-P11-008): bootstraps a host whose random streams derive from the seed and stream
        /// identity of <paramref name="randomSeeding"/> (identity 0: automatic); otherwise as the overload
        /// without a seed. See <see cref="ProductionTreeHost.RandomStreamIdentity"/>.
        /// </summary>
        public bool TryBootstrap(ProductionTreeHost host, RandomStreamSeeding randomSeeding, out NativeRuntimeFailureV1 failure,
            int effectCapacity = 0, Func<long> clock = null, NativeTraceChannelCapacityV1? traceCapacity = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (effectCapacity < 0) throw new ArgumentOutOfRangeException(nameof(effectCapacity));
            if (effectCapacity > 0 && !host.TryConfigureEffectDelivery(effectCapacity, out failure)) return false;
            return host.TryBootstrap(_definition, _catalog,
                traceCapacity ?? new NativeTraceChannelCapacityV1(256, 0, 0, 512), clock, randomSeeding, out failure);
        }

        /// <summary>Refuses disposal while live hosts retain the shared catalog; the caller can retry.</summary>
        public bool TryDispose(out BurstContextResult failure) => _catalog.TryDispose(out failure);

        public void Dispose()
        {
            if (_catalog.IsCreated && !TryDispose(out var failure))
                throw new InvalidOperationException("Destroy the program's hosts before disposal: " + failure);
        }
    }
}
