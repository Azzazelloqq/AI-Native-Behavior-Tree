namespace AIBT
{
    internal readonly struct NativeRandomStreamV1
    {
        internal NativeRandomStreamV1(ulong state, ulong increment)
        {
            State = state;
            Increment = increment;
        }

        internal ulong State { get; }
        internal ulong Increment { get; }
    }

    internal static class NativeRandomStreamDerivationV1
    {
        private const ulong PcgMultiplier = 6364136223846793005UL;

        internal static bool TryDerive(
            ulong rootSeed,
            NativeHash256V1 semanticHash,
            ulong treeInstanceId,
            uint runtimeNodeIndex,
            out NativeRandomStreamV1 stream)
        {
            stream = default;
            if (treeInstanceId == 0 || runtimeNodeIndex == uint.MaxValue) return false;

            var accumulator = 0x243f6a8885a308d3UL;
            // ASCII "AIBT-PCG-XSH-RR32-v1" followed by the required zero byte.
            accumulator = MixPrefix(accumulator);
            accumulator = MixU64(accumulator, rootSeed);
            for (var index = 0; index < 32; index++) accumulator = Mix64(accumulator ^ semanticHash.GetByte(index));
            accumulator = MixU64(accumulator, treeInstanceId);
            accumulator = MixU32(accumulator, runtimeNodeIndex);

            var initialState = Mix64(accumulator ^ 0xa0761d6478bd642fUL);
            var streamWord = Mix64(accumulator ^ 0xe7037ed1a0b428dbUL);
            var increment = ((streamWord & 0x7fffffffffffffffUL) << 1) | 1UL;
            var state = 0UL;
            Advance(ref state, increment);
            unchecked { state += initialState; }
            Advance(ref state, increment);
            stream = new NativeRandomStreamV1(state, increment);
            return true;
        }

        internal static uint NextUInt32(ref ulong state, ulong increment)
        {
            var oldState = state;
            unchecked { state = oldState * PcgMultiplier + increment; }
            var shifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            var rotation = (int)(oldState >> 59);
            return (shifted >> rotation) | (shifted << ((-rotation) & 31));
        }

        private static void Advance(ref ulong state, ulong increment) => NextUInt32(ref state, increment);

        private static ulong MixPrefix(ulong accumulator)
        {
            accumulator = Mix64(accumulator ^ 0x41); accumulator = Mix64(accumulator ^ 0x49);
            accumulator = Mix64(accumulator ^ 0x42); accumulator = Mix64(accumulator ^ 0x54);
            accumulator = Mix64(accumulator ^ 0x2d); accumulator = Mix64(accumulator ^ 0x50);
            accumulator = Mix64(accumulator ^ 0x43); accumulator = Mix64(accumulator ^ 0x47);
            accumulator = Mix64(accumulator ^ 0x2d); accumulator = Mix64(accumulator ^ 0x58);
            accumulator = Mix64(accumulator ^ 0x53); accumulator = Mix64(accumulator ^ 0x48);
            accumulator = Mix64(accumulator ^ 0x2d); accumulator = Mix64(accumulator ^ 0x52);
            accumulator = Mix64(accumulator ^ 0x52); accumulator = Mix64(accumulator ^ 0x33);
            accumulator = Mix64(accumulator ^ 0x32); accumulator = Mix64(accumulator ^ 0x2d);
            accumulator = Mix64(accumulator ^ 0x76); accumulator = Mix64(accumulator ^ 0x31);
            accumulator = Mix64(accumulator);
            return accumulator;
        }

        private static ulong MixU64(ulong accumulator, ulong value)
        {
            for (var index = 0; index < 8; index++) accumulator = Mix64(accumulator ^ (byte)(value >> (index * 8)));
            return accumulator;
        }

        private static ulong MixU32(ulong accumulator, uint value)
        {
            for (var index = 0; index < 4; index++) accumulator = Mix64(accumulator ^ (byte)(value >> (index * 8)));
            return accumulator;
        }

        private static ulong Mix64(ulong value)
        {
            unchecked
            {
                var mixed = value + 0x9e3779b97f4a7c15UL;
                mixed = (mixed ^ (mixed >> 30)) * 0xbf58476d1ce4e5b9UL;
                mixed = (mixed ^ (mixed >> 27)) * 0x94d049bb133111ebUL;
                return mixed ^ (mixed >> 31);
            }
        }
    }
}
