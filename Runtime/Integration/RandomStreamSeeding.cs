using System;

namespace AIBT
{
    /// <summary>
    /// P11-012 (ADR-P11-008, AIBT-052): how a native host seeds the random streams of nodes that declare
    /// one (<c>[AibtRandomStream]</c>): a root seed and a stream identity, the word that
    /// <c>Documentation~/specifications/time-and-random-v1.md</c> mixes in after the semantic hash.
    /// <para>
    /// <see cref="StreamIdentity"/> 0 means automatic: the host uses its own <see cref="ProductionTreeHost.InstanceId"/>,
    /// which is process-wide, so two hosts never share streams but a run is not repeatable across sessions
    /// (another start order, another host bootstrapped first, Enter Play Mode without a domain reload).
    /// A nonzero identity is used as given: the same seed, program and identity give the same streams in
    /// any session. Equal streams do not make two sessions behave identically by themselves: the scheduler
    /// still orders hosts by start order and nodes draw as frame time lets them run; lockstep replay is
    /// <c>TreeSimulation</c>'s job.
    /// </para>
    /// </summary>
    public readonly struct RandomStreamSeeding : IEquatable<RandomStreamSeeding>
    {
        // ASCII "AIBTSTRM", big-endian: the domain word of the combine function and its value for the one
        // input whose mix is 0.
        private const ulong CombineDomain = 0x414942545354524dUL;

        /// <param name="rootSeed">The root seed (<see cref="ProductionTreeHost.RandomSeed"/>).</param>
        /// <param name="streamIdentity">The stream identity; 0 is automatic (the host's instance id).</param>
        public RandomStreamSeeding(ulong rootSeed, ulong streamIdentity)
        {
            RootSeed = rootSeed;
            StreamIdentity = streamIdentity;
        }

        /// <summary>The root seed.</summary>
        public ulong RootSeed { get; }

        /// <summary>The stream identity; 0 is automatic (the host's process-wide instance id).</summary>
        public ulong StreamIdentity { get; }

        /// <summary>True when the host picks the identity (its instance id).</summary>
        public bool IsAutomatic => StreamIdentity == 0;

        /// <summary>The seed with the automatic identity: what the seed-only bootstraps use.</summary>
        public static RandomStreamSeeding Automatic(ulong rootSeed) => new RandomStreamSeeding(rootSeed, 0UL);

        /// <summary>
        /// The seeding <see cref="NativeTreeAgent"/> uses for a run: automatic when
        /// <paramref name="streamId"/> is 0, otherwise the identity
        /// <see cref="CombineStreamIdentity"/>(<paramref name="streamId"/>, <paramref name="runIndex"/>).
        /// A bare host's caller can use it to follow the agent's policy.
        /// </summary>
        public static RandomStreamSeeding ForRun(ulong rootSeed, ulong streamId, ulong runIndex)
            => new RandomStreamSeeding(rootSeed, streamId == 0 ? 0UL : CombineStreamIdentity(streamId, runIndex));

        /// <summary>
        /// The combine function of <c>time-and-random-v1</c>: a nonzero stream identity from a stream id
        /// and a run index, <c>z = Mix64(Mix64(streamId xor 0x414942545354524d) + runIndex)</c> with
        /// unsigned wrap, and <c>0x414942545354524d</c> when <c>z</c> is 0. For one id it is a bijection of
        /// the run index; unlike <c>id + run</c> or <c>id xor run</c>, neighbouring pairs do not collide.
        /// </summary>
        public static ulong CombineStreamIdentity(ulong streamId, ulong runIndex)
        {
            ulong combined;
            unchecked { combined = Mix64(Mix64(streamId ^ CombineDomain) + runIndex); }
            return combined == 0 ? CombineDomain : combined;
        }

        // The Mix64 of time-and-random-v1 (as NativeRandomStreamDerivationV1 uses it).
        private static ulong Mix64(ulong value)
        {
            unchecked
            {
                var z = value + 0x9e3779b97f4a7c15UL;
                z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
                z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
                return z ^ (z >> 31);
            }
        }

        public bool Equals(RandomStreamSeeding other) => RootSeed == other.RootSeed && StreamIdentity == other.StreamIdentity;
        public override bool Equals(object obj) => obj is RandomStreamSeeding other && Equals(other);
        public override int GetHashCode() => unchecked((RootSeed.GetHashCode() * 397) ^ StreamIdentity.GetHashCode());
        public static bool operator ==(RandomStreamSeeding left, RandomStreamSeeding right) => left.Equals(right);
        public static bool operator !=(RandomStreamSeeding left, RandomStreamSeeding right) => !left.Equals(right);
        public override string ToString() => "seed " + RootSeed + ", stream identity " + (IsAutomatic ? "automatic" : StreamIdentity.ToString());
    }
}
