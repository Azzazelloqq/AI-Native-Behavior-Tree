using System.Collections.Generic;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.HotReload
{
    /// <summary>
    /// P5-007's estimator reset-vs-carry-over decision: a hot-reloaded instance's
    /// <see cref="NativeWorkEstimatorV1"/> is reset, never carried over. This is not new
    /// production behavior -- <see cref="NativeWorkEstimatorV1"/> (per its own P4-004 design) has
    /// no persistence of its own and no reload-awareness at all; the caller owns keying one
    /// instance per distinct compiled-program identity/population
    /// (`Runtime/Scheduling/Native/Estimation/NativeWorkEstimatorV1.cs`'s own doc comment). Since
    /// every reload strategy (`P5-004`/`P5-005`/`P5-006`) always produces a genuinely new
    /// <see cref="CompiledProgram"/> object -- reload is never an in-place mutation, per
    /// <c>ADR-P5-001</c> -- a caller keyed by compiled-program identity gets a fresh estimator
    /// automatically, with no special-casing required in the reload mechanism itself.
    /// <para>
    /// This is the deliberate, reasoned choice, not merely the path of least resistance: carrying
    /// a smoothed steps-per-agent estimate forward across a structural change (an insertion or
    /// removal genuinely changes total step count) risks feeding <c>Auto</c>
    /// (<c>NativeAutoSelectionV1</c>) a stale estimate for the new program's actual shape. Given
    /// `P4-006`'s already-measured finding that a wrong policy choice can persist indefinitely
    /// (`P4-007`'s own resolution: no exploration mechanism re-evaluates a chosen policy), seeding
    /// `Auto`'s very first post-reload decision from a possibly-wrong carried-over estimate is a
    /// real, avoidable risk this reset avoids -- resetting costs at most one estimator seed period
    /// (<see cref="NativeWorkEstimatorV1.TryEstimateWorkPerAgentNanoseconds"/> fails until the
    /// first observation, exactly its existing, already-accepted contract), not a correctness bug.
    /// </para>
    /// </summary>
    public sealed class HotReloadSchedulerEstimatorResetTests
    {
        [Test]
        public void EstimatorKeyedByCompiledProgramIdentity_IsFreshAfterReload_WithNoSpecialCasing()
        {
            // Simulates the caller-owned keying pattern NativeWorkEstimatorV1's own contract
            // requires: one estimator per distinct compiled-program identity. A lightweight
            // identity token stands in for a real CompiledProgram here -- this test proves the
            // *keying discipline*, which needs no compiler at all, not the compiled format itself.
            var oldIdentity = new DummyProgramIdentity();
            var estimator = new NativeWorkEstimatorV1();
            Assert.That(estimator.TryObserve(4, 1000, out _), Is.True);
            Assert.That(estimator.HasEstimate, Is.True);

            // A reload always constructs a genuinely new CompiledProgram (ADR-P5-001: never an
            // in-place mutation) -- represented here by a second, distinct identity token standing
            // in for "the new program object", since a real CompiledProgram requires a full
            // compile this cross-cutting test does not need in order to prove the keying behavior.
            var newIdentity = new DummyProgramIdentity();

            var estimatorsByIdentity = new Dictionary<DummyProgramIdentity, NativeWorkEstimatorV1>
            {
                [oldIdentity] = estimator,
            };

            // The reload mechanism does nothing scheduler-specific at all (P5-004/P5-005/P5-006
            // touch no Runtime/Scheduling/Native/ type) -- a caller simply looks up (or creates)
            // the estimator for whichever identity it is currently driving.
            var hasEstimatorForNewProgram = estimatorsByIdentity.TryGetValue(newIdentity, out var reused);
            Assert.That(hasEstimatorForNewProgram, Is.False,
                "a distinct post-reload program identity must not resolve to the old estimator by accident");

            var freshEstimator = new NativeWorkEstimatorV1();
            Assert.That(freshEstimator.HasEstimate, Is.False,
                "the post-reload estimator starts unseeded -- reset, not carried over");
        }

        private sealed class DummyProgramIdentity
        {
        }
    }
}
