using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 2 (ADR-P11-002 §7): the timing and signal nodes on the real ProductionTreeHost,
    /// prepared against a project catalog set that lists the package shard, with a controlled host
    /// clock. Each case drives updates at chosen time points: before, exactly at and after deadlines
    /// and timeouts; the wait aborted and started again; signals drained from the host's committed
    /// effect queue after every update (delivered exactly once, with the package channel's stable id).
    /// </summary>
    public sealed class NativeLibraryTimingTests
    {
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        private static IEnumerable<TestCaseData> Cases()
            => TimingCases.All.Select(c => new TestCaseData(c).SetName("Host_" + c.Name.Replace(' ', '_')));

        [TestCaseSource(nameof(Cases))]
        public void EveryTimingAndSignalNodeKeepsItsContractOnProductionTreeHost(TimingCase timingCase)
        {
            var now = 0L;
            var host = _kit.Start(_kit.Prepare(timingCase.Json), () => now, effectCapacity: 8);
            var signalId = StableHash.Fnv1A64(TimingCases.Signal);
            for (var index = 0; index < timingCase.Steps.Count; index++)
            {
                var step = timingCase.Steps[index];
                var context = timingCase.Name + " @" + step.Clock;
                foreach (var (key, value) in step.Facts)
                {
                    switch (value)
                    {
                        case bool flag: host.Fact(key, flag); break;
                        case long number: host.Fact(key, number); break;
                        default: Assert.Fail("Unsupported fact type " + value.GetType()); break;
                    }
                }
                now = step.Clock;
                host.Update();
                Assert.That(host.Root, Is.EqualTo(step.Root), context + ": root result (null = still running)");
                foreach (var (key, value) in step.Values)
                {
                    if (value is long number) Assert.That(host.ReadLong(key), Is.EqualTo(number), context + ": " + key);
                    else NativeLibraryNodeTests.AssertValue(host, key, value, context);
                }
                var effects = host.DrainEffects();
                Assert.That(effects.Select(effect => effect.Id), Is.All.EqualTo(signalId), context + ": every effect is on aibt.native.signal");
                Assert.That(effects.Select(effect => effect.Value), Is.EqualTo(step.Signals), context + ": signals delivered in this update");
                Assert.That(host.DrainEffects(), Is.Empty, context + ": a second drain delivers nothing again");
            }
        }

        [Test]
        public void EveryStage2NodeHasBehaviourCases()
        {
            var covered = new HashSet<string>(TimingCases.CoveredNodes());
            Assert.That(StandardNativeLibraryIds.Stage2.Where(id => !covered.Contains(id)), Is.Empty);
            Assert.That(StandardNativeLibraryIds.All, Is.Unique);
            Assert.That(StandardNativeLibraryIds.All, Has.Length.EqualTo(33));
        }
    }
}
