namespace AIBT
{
    internal static class NativeReactivePolicyV1
    {
        private const ulong ReactiveSequenceTypeId = 0x0b89403843fe37abUL;
        private const ulong ReactiveSelectorTypeId = 0xe35ed63d091a98e7UL;

        // ADR-P10-013: aibt.core.reactive-sequence-retaining / aibt.core.reactive-selector-retaining.
        private const ulong RetainingReactiveSequenceTypeId = 0xec59856b5626d4c9UL;
        private const ulong RetainingReactiveSelectorTypeId = 0xc80727feddaf1835UL;

        /// <summary>The v1 reactive composites, which restart their running child on every update.</summary>
        internal static bool IsReactive(NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.ReactiveSequence
                || kind == NativeLifecycleNodeKindV1.ReactiveSelector;

        /// <summary>The retaining reactive composites, which keep their running child (guard pass).</summary>
        internal static bool IsRetaining(NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.RetainingReactiveSequence
                || kind == NativeLifecycleNodeKindV1.RetainingReactiveSelector;

        internal static bool IsRetainingTypeId(ulong typeId)
            => typeId == RetainingReactiveSequenceTypeId || typeId == RetainingReactiveSelectorTypeId;

        internal static bool Matches(ulong typeId, NativeLifecycleNodeKindV1 kind)
        {
            switch (kind)
            {
                case NativeLifecycleNodeKindV1.ReactiveSequence: return typeId == ReactiveSequenceTypeId;
                case NativeLifecycleNodeKindV1.ReactiveSelector: return typeId == ReactiveSelectorTypeId;
                case NativeLifecycleNodeKindV1.RetainingReactiveSequence: return typeId == RetainingReactiveSequenceTypeId;
                case NativeLifecycleNodeKindV1.RetainingReactiveSelector: return typeId == RetainingReactiveSelectorTypeId;
                default: return false;
            }
        }
    }
}
