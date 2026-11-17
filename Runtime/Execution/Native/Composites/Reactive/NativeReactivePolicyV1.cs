namespace AIBT
{
    internal static class NativeReactivePolicyV1
    {
        private const ulong ReactiveSequenceTypeId = 0x0b89403843fe37abUL;
        private const ulong ReactiveSelectorTypeId = 0xe35ed63d091a98e7UL;

        internal static bool IsReactive(NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.ReactiveSequence
                || kind == NativeLifecycleNodeKindV1.ReactiveSelector;

        // ADR-P10-013 stage 1: the retaining reactive composites exist in the registry, but the
        // native lifecycle machine does not implement them yet (stage 2). They have no native kind,
        // so the native preparation paths refuse them instead of running them as a leaf or with v1
        // reactive semantics.
        private const ulong RetainingReactiveSequenceTypeId = 0xec59856b5626d4c9UL;
        private const ulong RetainingReactiveSelectorTypeId = 0xc80727feddaf1835UL;

        internal static bool IsUnsupportedRetaining(ulong typeId)
            => typeId == RetainingReactiveSequenceTypeId || typeId == RetainingReactiveSelectorTypeId;

        internal static bool Matches(ulong typeId, NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.ReactiveSequence
                ? typeId == ReactiveSequenceTypeId
                : kind == NativeLifecycleNodeKindV1.ReactiveSelector && typeId == ReactiveSelectorTypeId;
    }
}
