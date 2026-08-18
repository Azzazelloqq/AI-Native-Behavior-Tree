namespace AIBT
{
    internal static class NativeReactivePolicyV1
    {
        private const ulong ReactiveSequenceTypeId = 0x0b89403843fe37abUL;
        private const ulong ReactiveSelectorTypeId = 0xe35ed63d091a98e7UL;

        internal static bool IsReactive(NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.ReactiveSequence
                || kind == NativeLifecycleNodeKindV1.ReactiveSelector;

        internal static bool Matches(ulong typeId, NativeLifecycleNodeKindV1 kind)
            => kind == NativeLifecycleNodeKindV1.ReactiveSequence
                ? typeId == ReactiveSequenceTypeId
                : kind == NativeLifecycleNodeKindV1.ReactiveSelector && typeId == ReactiveSelectorTypeId;
    }
}
