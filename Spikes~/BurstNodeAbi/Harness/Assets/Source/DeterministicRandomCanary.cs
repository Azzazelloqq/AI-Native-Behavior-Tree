namespace AIBT.BurstAbi.Canary
{
    /// <summary>
    /// Four little-endian words containing the 32 raw semantic SHA-256 bytes in
    /// displayed hexadecimal order. This keeps the retained canary unmanaged.
    /// </summary>
    public readonly struct DeterministicSemanticHashCanary
    {
        internal readonly ulong Word0;
        internal readonly ulong Word1;
        internal readonly ulong Word2;
        internal readonly ulong Word3;

        public DeterministicSemanticHashCanary(ulong word0, ulong word1, ulong word2, ulong word3)
        {
            Word0 = word0;
            Word1 = word1;
            Word2 = word2;
            Word3 = word3;
        }
    }

    /// <summary>
    /// Disposable retained implementation of time-and-random-v1 stream
    /// derivation and PCG-XSH-RR 32. It contains only unmanaged state.
    /// </summary>
    public struct DeterministicRandomCanary
    {
        private const ulong PcgMultiplier = 6364136223846793005ul;

        private ulong rootSeed;
        private DeterministicSemanticHashCanary semanticHash;
        private ulong treeInstanceId;
        private uint runtimeNodeIndex;
        private ulong state;
        private ulong increment;
        private ulong initialStateWord;
        private ulong streamWord;
        private bool isCreated;

        public bool IsCreated => isCreated;
        internal ulong State => state;
        internal ulong Increment => increment;
        internal ulong InitialStateWord => initialStateWord;
        internal ulong StreamWord => streamWord;

        public static bool TryCreate(
            ulong rootSeed,
            in DeterministicSemanticHashCanary semanticHash,
            ulong treeInstanceId,
            uint runtimeNodeIndex,
            out DeterministicRandomCanary random)
        {
            random = default;
            if (treeInstanceId == 0ul || runtimeNodeIndex == uint.MaxValue)
                return false;

            random.rootSeed = rootSeed;
            random.semanticHash = semanticHash;
            random.treeInstanceId = treeInstanceId;
            random.runtimeNodeIndex = runtimeNodeIndex;
            random.InitializeState();
            random.isCreated = true;
            return true;
        }

        public bool TryNextUInt32(out uint value)
        {
            if (!isCreated)
            {
                value = 0u;
                return false;
            }

            value = Advance(ref state, increment);
            return true;
        }

        public bool TryNextUInt32(uint boundExclusive, out uint value)
        {
            value = 0u;
            if (!isCreated || boundExclusive == 0u)
                return false;

            uint threshold = unchecked(0u - boundExclusive) % boundExclusive;
            uint candidate;
            do
            {
                candidate = Advance(ref state, increment);
            }
            while (candidate < threshold);

            value = candidate % boundExclusive;
            return true;
        }

        public bool TryNextFloat32(out float value)
        {
            if (!TryNextUInt32(out uint bits))
            {
                value = 0.0f;
                return false;
            }

            value = (bits >> 8) / 16777216.0f;
            return true;
        }

        public bool TryRestart()
        {
            if (!isCreated)
                return false;

            InitializeState();
            return true;
        }

        public bool TryReseed(
            ulong newRootSeed,
            in DeterministicSemanticHashCanary newSemanticHash,
            ulong newTreeInstanceId,
            uint newRuntimeNodeIndex)
        {
            if (!TryCreate(
                    newRootSeed,
                    in newSemanticHash,
                    newTreeInstanceId,
                    newRuntimeNodeIndex,
                    out DeterministicRandomCanary replacement))
            {
                return false;
            }

            this = replacement;
            return true;
        }

        // These lifecycle notifications are deliberately non-consuming.
        public void NotifyAbort()
        {
        }

        public void NotifyBudgetSuspended()
        {
        }

        public void NotifyBudgetResumed()
        {
        }

        private void InitializeState()
        {
            ulong accumulator = 0x243f6a8885a308d3ul;

            // ASCII "AIBT-PCG-XSH-RR32-v1" followed by one zero byte.
            MixByte(ref accumulator, (byte)'A');
            MixByte(ref accumulator, (byte)'I');
            MixByte(ref accumulator, (byte)'B');
            MixByte(ref accumulator, (byte)'T');
            MixByte(ref accumulator, (byte)'-');
            MixByte(ref accumulator, (byte)'P');
            MixByte(ref accumulator, (byte)'C');
            MixByte(ref accumulator, (byte)'G');
            MixByte(ref accumulator, (byte)'-');
            MixByte(ref accumulator, (byte)'X');
            MixByte(ref accumulator, (byte)'S');
            MixByte(ref accumulator, (byte)'H');
            MixByte(ref accumulator, (byte)'-');
            MixByte(ref accumulator, (byte)'R');
            MixByte(ref accumulator, (byte)'R');
            MixByte(ref accumulator, (byte)'3');
            MixByte(ref accumulator, (byte)'2');
            MixByte(ref accumulator, (byte)'-');
            MixByte(ref accumulator, (byte)'v');
            MixByte(ref accumulator, (byte)'1');
            MixByte(ref accumulator, (byte)0);

            MixLittleEndian(ref accumulator, rootSeed, 8);
            MixLittleEndian(ref accumulator, semanticHash.Word0, 8);
            MixLittleEndian(ref accumulator, semanticHash.Word1, 8);
            MixLittleEndian(ref accumulator, semanticHash.Word2, 8);
            MixLittleEndian(ref accumulator, semanticHash.Word3, 8);
            MixLittleEndian(ref accumulator, treeInstanceId, 8);
            MixLittleEndian(ref accumulator, runtimeNodeIndex, 4);

            ulong initialState = Mix64(accumulator ^ 0xa0761d6478bd642ful);
            streamWord = Mix64(accumulator ^ 0xe7037ed1a0b428dbul);
            initialStateWord = initialState;
            ulong streamSelector = streamWord & 0x7ffffffffffffffful;
            increment = (streamSelector << 1) | 1ul;

            state = 0ul;
            Advance(ref state, increment);
            state = unchecked(state + initialState);
            Advance(ref state, increment);
        }

        private static void MixLittleEndian(ref ulong accumulator, ulong word, int byteCount)
        {
            for (int index = 0; index < byteCount; index++)
            {
                MixByte(ref accumulator, (byte)(word >> (index * 8)));
            }
        }

        private static void MixByte(ref ulong accumulator, byte value)
        {
            accumulator = Mix64(accumulator ^ value);
        }

        private static ulong Mix64(ulong value)
        {
            unchecked
            {
                ulong mixed = value + 0x9e3779b97f4a7c15ul;
                mixed = (mixed ^ (mixed >> 30)) * 0xbf58476d1ce4e5b9ul;
                mixed = (mixed ^ (mixed >> 27)) * 0x94d049bb133111ebul;
                return mixed ^ (mixed >> 31);
            }
        }

        private static uint Advance(ref ulong currentState, ulong streamIncrement)
        {
            unchecked
            {
                ulong oldState = currentState;
                currentState = oldState * PcgMultiplier + streamIncrement;
                uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
                int rotation = (int)(oldState >> 59);
                return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
            }
        }
    }
}
