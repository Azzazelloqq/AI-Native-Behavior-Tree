using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using static AIBT.Tests.Integration.Simulation.SimulationTestPrograms;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-012 (ADR-P11-008, AIBT-052): the stream identity combine function of
    /// <c>time-and-random-v1</c> and the host/program overloads that take a <see cref="RandomStreamSeeding"/>.
    /// The vectors come from an independent Python reference
    /// (<c>_aibt_acceptance/P11-012/stream-identity-vectors.py</c>), written from the specification.
    /// </summary>
    public sealed class RandomStreamSeedingTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<ProductionTreeProgram> _programs = new List<ProductionTreeProgram>();
        private readonly List<ProductionTreeHost> _hosts = new List<ProductionTreeHost>();

        [TearDown]
        public void TearDown()
        {
            foreach (var host in _hosts) if (host != null) host.Stop();
            _hosts.Clear();
            foreach (var program in _programs) program.Dispose();
            _programs.Clear();
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (_objects[index] != null) Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [TestCase(0x0000000000000001UL, 0x0000000000000000UL, 0x17c7da942a926088UL)]
        [TestCase(0x0000000000000001UL, 0x0000000000000001UL, 0x380638fd2ee43a95UL)]
        [TestCase(0x0000000000000002UL, 0x0000000000000000UL, 0x4b94756ef81e1100UL)]
        [TestCase(0x00000000075bcd15UL, 0x0000000000000003UL, 0x8f47c4ae40c92095UL)]
        [TestCase(0xffffffffffffffffUL, 0xffffffffffffffffUL, 0xbd8b4b5991a381efUL)]
        public void TheCombineFunction_MatchesThePublishedVectors(ulong streamId, ulong runIndex, ulong identity)
            => Assert.That(RandomStreamSeeding.CombineStreamIdentity(streamId, runIndex), Is.EqualTo(identity));

        /// <summary>The one input whose mix is 0 yields the domain constant ("AIBTSTRM") instead: never 0.</summary>
        [Test]
        public void TheCombineFunction_NeverYieldsZero_ThePublishedZeroCaseYieldsTheConstant()
        {
            Assert.That(RandomStreamSeeding.CombineStreamIdentity(1UL, 0x0fa0ecdca6488920UL), Is.EqualTo(0x414942545354524dUL));
        }

        /// <summary>Unlike id + run or id ^ run, neighbouring (id, run) pairs do not collide; none is 0.</summary>
        [Test]
        public void TheCombineFunction_HasNoCollisionOrZeroOnAGrid_WhereSumAndXorCollide()
        {
            Assert.That(1UL + 1UL, Is.EqualTo(2UL + 0UL), "the sum collides");
            Assert.That(3UL ^ 1UL, Is.EqualTo(2UL ^ 0UL), "the xor collides");
            Assert.That(RandomStreamSeeding.CombineStreamIdentity(1UL, 1UL), Is.Not.EqualTo(RandomStreamSeeding.CombineStreamIdentity(2UL, 0UL)));
            Assert.That(RandomStreamSeeding.CombineStreamIdentity(3UL, 1UL), Is.Not.EqualTo(RandomStreamSeeding.CombineStreamIdentity(2UL, 0UL)));
            var seen = new HashSet<ulong>();
            for (ulong id = 1; id <= 256; id++)
                for (ulong run = 0; run < 256; run++)
                {
                    var identity = RandomStreamSeeding.CombineStreamIdentity(id, run);
                    Assert.That(identity, Is.Not.Zero);
                    Assert.That(seen.Add(identity), Is.True, "collision at id " + id + ", run " + run);
                }
        }

        /// <summary>The full published stream row: seed, hash 00..1f, identity of (7, 2), node 42.</summary>
        [Test]
        public void TheDerivedStreamOfACombinedIdentity_MatchesThePublishedRow()
        {
            var hash = new CompiledHash("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
            var identity = RandomStreamSeeding.CombineStreamIdentity(7UL, 2UL);
            Assert.That(identity, Is.EqualTo(0x89076cda0e1cdaafUL));
            Assert.That(NativeRandomStreamDerivationV1.TryDerive(0x0123456789abcdefUL, new NativeHash256V1(hash), identity, 42u, out var stream), Is.True);
            Assert.That(stream.Increment, Is.EqualTo(0x31325367a6526d8fUL));
            Assert.That(stream.State, Is.EqualTo(0x061ebb045d6c22ddUL));
            var state = stream.State;
            var outputs = new uint[6];
            for (var index = 0; index < outputs.Length; index++) outputs[index] = NativeRandomStreamDerivationV1.NextUInt32(ref state, stream.Increment);
            Assert.That(outputs, Is.EqualTo(new[] { 0xc3d7507eu, 0x7ba12ba7u, 0xb2818e51u, 0xbb6beb1cu, 0xe8a5545cu, 0x327c490fu }));
        }

        [Test]
        public void TheSeedingValue_KeepsItsWords_AndForRunCombinesOnlyANonzeroId()
        {
            var explicitIdentity = new RandomStreamSeeding(5UL, 99UL);
            Assert.That(explicitIdentity.RootSeed, Is.EqualTo(5UL));
            Assert.That(explicitIdentity.StreamIdentity, Is.EqualTo(99UL));
            Assert.That(explicitIdentity.IsAutomatic, Is.False);
            var automatic = RandomStreamSeeding.Automatic(5UL);
            Assert.That(automatic.RootSeed, Is.EqualTo(5UL));
            Assert.That(automatic.StreamIdentity, Is.Zero);
            Assert.That(automatic.IsAutomatic, Is.True);
            Assert.That(RandomStreamSeeding.ForRun(5UL, 0UL, 3UL), Is.EqualTo(automatic), "id 0 stays automatic whatever the run");
            Assert.That(RandomStreamSeeding.ForRun(5UL, 7UL, 2UL).StreamIdentity, Is.EqualTo(0x89076cda0e1cdaafUL));
            Assert.That(default(RandomStreamSeeding).IsAutomatic, Is.True);
        }

        /// <summary>
        /// A bare host bootstrapped with an identity derives every stream from it and reports it; its
        /// instance id stays process-wide. Bootstraps without one (the seed-only overloads, or identity 0)
        /// use the instance id, as before.
        /// </summary>
        [Test]
        public void AHostBootstrappedWithAnIdentity_DerivesFromIt_TheOtherOverloadsUseTheInstanceId()
        {
            const ulong Seed = 0xB00757UL, Identity = 0xDEC1DEDUL;
            var program = Program(RollerTree);

            var withIdentity = NewHost();
            Assert.That(program.TryBootstrap(withIdentity, new RandomStreamSeeding(Seed, Identity), out var failure, clock: () => 0L),
                Is.True, failure.ToString());
            Assert.That(withIdentity.RandomSeed, Is.EqualTo(Seed));
            Assert.That(withIdentity.RandomStreamIdentity, Is.EqualTo(Identity));
            Assert.That(withIdentity.InstanceId, Is.Not.EqualTo(Identity));
            AssertDerived(program, withIdentity, Seed, Identity);

            var seedOnly = NewHost();
            Assert.That(program.TryBootstrap(seedOnly, Seed, out failure, clock: () => 0L), Is.True, failure.ToString());
            Assert.That(seedOnly.RandomStreamIdentity, Is.EqualTo(seedOnly.InstanceId));
            AssertDerived(program, seedOnly, Seed, seedOnly.InstanceId);

            var automatic = NewHost();
            Assert.That(program.TryBootstrap(automatic, RandomStreamSeeding.Automatic(Seed), out failure, clock: () => 0L), Is.True, failure.ToString());
            Assert.That(automatic.RandomStreamIdentity, Is.EqualTo(automatic.InstanceId));
            AssertDerived(program, automatic, Seed, automatic.InstanceId);

            var seedless = NewHost();
            Assert.That(program.TryBootstrap(seedless, out failure, clock: () => 0L), Is.True, failure.ToString());
            Assert.That(seedless.RandomSeed, Is.EqualTo(ProductionTreeHost.DefaultRandomSeed));
            Assert.That(seedless.RandomStreamIdentity, Is.EqualTo(seedless.InstanceId));

            var direct = NewHost();
            Assert.That(direct.TryBootstrap(program.Definition, program.Catalog, new NativeTraceChannelCapacityV1(256, 0, 0, 512), () => 0L,
                new RandomStreamSeeding(Seed, Identity), out failure), Is.True, failure.ToString());
            Assert.That(direct.RandomStreamIdentity, Is.EqualTo(Identity));
            AssertDerived(program, direct, Seed, Identity);
        }

        /// <summary>
        /// Two hosts with one seed and one identity draw the same values update by update; they keep
        /// distinct instance ids (handles and traces still tell them apart).
        /// </summary>
        [Test]
        public void TwoHostsWithOneSeedAndIdentity_RunAlike()
        {
            var program = Program(RollerTree);
            long now = 0;
            var first = NewHost();
            var second = NewHost();
            var seeding = new RandomStreamSeeding(0x2EEDUL, 4242UL);
            Assert.That(program.TryBootstrap(first, seeding, out var failure, clock: () => now), Is.True, failure.ToString());
            Assert.That(program.TryBootstrap(second, seeding, out failure, clock: () => now), Is.True, failure.ToString());
            Assert.That(second.InstanceId, Is.Not.EqualTo(first.InstanceId));
            var node = 0u;
            while (!first.GeneratedDispatchAdapter.TryReadRandomStream(node, out _, out _)) node++;
            var floatType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
            Assert.That(first.TryResolveExternalTreeRead("score", floatType, out ProductionTreeHost.ExternalTreeReadHandle<float> readA, out failure), Is.True);
            Assert.That(second.TryResolveExternalTreeRead("score", floatType, out ProductionTreeHost.ExternalTreeReadHandle<float> readB, out failure), Is.True);
            var advanced = 0;
            for (var update = 1; update <= 60; update++)
            {
                now = update * 50000L;
                first.DriveOneUpdate();
                second.DriveOneUpdate();
                Assert.That(first.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                Assert.That(second.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
                first.GeneratedDispatchAdapter.TryReadRandomStream(node, out var a, out _);
                second.GeneratedDispatchAdapter.TryReadRandomStream(node, out var b, out _);
                Assert.That(b, Is.EqualTo(a), "update " + update);
                Assert.That(first.TryReadExternalTreeValue(readA, out var scoreA, out _), Is.True);
                Assert.That(second.TryReadExternalTreeValue(readB, out var scoreB, out _), Is.True);
                Assert.That(scoreB, Is.EqualTo(scoreA), "update " + update);
                if (scoreA != 0.5f) advanced++;
            }
            Assert.That(advanced, Is.GreaterThan(0), "the roller rolled");
        }

        private ProductionTreeProgram Program(string json)
        {
            var asset = PrepareAsset(json);
            _objects.Add(asset);
            var program = CreateProgram(asset);
            _programs.Add(program);
            return program;
        }

        private ProductionTreeHost NewHost()
        {
            var gameObject = new GameObject("AIBT.Tests.P11012.Host");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<ProductionTreeHost>();
            _hosts.Add(host);
            return host;
        }

        private static void AssertDerived(ProductionTreeProgram program, ProductionTreeHost host, ulong seed, ulong identity)
        {
            var hash = new NativeHash256V1(program.Program.Header.CanonicalSemanticHash);
            var found = 0;
            for (uint node = 0; node < program.Program.Nodes.Count; node++)
            {
                if (!host.GeneratedDispatchAdapter.TryReadRandomStream(node, out var state, out var increment)) continue;
                found++;
                Assert.That(NativeRandomStreamDerivationV1.TryDerive(seed, hash, identity, node, out var expected), Is.True);
                Assert.That(state, Is.EqualTo(expected.State), "node " + node);
                Assert.That(increment, Is.EqualTo(expected.Increment), "node " + node);
            }
            Assert.That(found, Is.GreaterThan(0));
        }
    }
}
