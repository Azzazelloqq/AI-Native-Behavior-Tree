using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Runtime.Scheduling.Production
{
    /// <summary>
    /// P7-033 step 2: behavior-first proof for <see cref="SchedulingProfile"/>/<see cref="SchedulingProfileAsset"/>
    /// before any scheduler/coordinator code exists. Every assertion maps to a stated ADR-P7-033
    /// acceptance criterion; none exercises a scheduler, since none exists yet at this step.
    /// </summary>
    public sealed class SchedulingProfileTests
    {
        [Test]
        public void Normal_MatchesCurrentProductionTreeHostBehaviorExactly()
        {
            var profile = SchedulingProfile.Normal;
            Assert.That(profile.Priority, Is.Zero);
            Assert.That(profile.UpdateCadence, Is.EqualTo(1u));
            Assert.That(profile.MaximumResponseLatencyFrames, Is.Null);
            Assert.That(profile.MaximumBudgetShare, Is.Null);
            Assert.That(profile.PipeliningPermitted, Is.False);
            Assert.That(profile.ForcedPolicy, Is.Null, "Unset ForcedPolicy is Auto -- current behavior before any profile existed.");
        }

        [Test]
        public void Interactive_OutranksNormal_OtherwiseIdentical()
        {
            Assert.That(SchedulingProfile.Interactive.Priority, Is.GreaterThan(SchedulingProfile.Normal.Priority));
            Assert.That(SchedulingProfile.Interactive.PipeliningPermitted, Is.False);
            Assert.That(SchedulingProfile.Interactive.UpdateCadence, Is.EqualTo(SchedulingProfile.Normal.UpdateCadence));
        }

        [Test]
        public void Background_RanksBelowNormal_AndPermitsPipelining()
        {
            Assert.That(SchedulingProfile.Background.Priority, Is.LessThan(SchedulingProfile.Normal.Priority));
            Assert.That(SchedulingProfile.Background.PipeliningPermitted, Is.True,
                "The only built-in field difference beyond ordering -- justified by the preset's own name, not a fabricated constant.");
        }

        [Test]
        public void BuiltInPresets_HaveDistinctStableIds()
        {
            var ids = new[] { SchedulingProfile.Normal.StableId, SchedulingProfile.Interactive.StableId, SchedulingProfile.Background.StableId };
            Assert.That(ids, Is.Unique);
        }

        [Test]
        public void TryCreate_CustomIdAndRank_ProducesMatchingStableIdAndFields()
        {
            Assert.That(SchedulingProfile.TryCreate(
                "game.ai.boss", 42, 2u, 8u, 0.5, true, SchedulingPolicy.PipelinedJobs,
                out var profile, out var error), Is.True, error.ToString());
            Assert.That(profile.Id, Is.EqualTo("game.ai.boss"));
            Assert.That(profile.StableId, Is.EqualTo(StableHash.Fnv1A64("game.ai.boss")));
            Assert.That(profile.Priority, Is.EqualTo(42));
            Assert.That(profile.UpdateCadence, Is.EqualTo(2u));
            Assert.That(profile.MaximumResponseLatencyFrames, Is.EqualTo(8u));
            Assert.That(profile.MaximumBudgetShare, Is.EqualTo(0.5));
            Assert.That(profile.PipeliningPermitted, Is.True);
            Assert.That(profile.ForcedPolicy, Is.EqualTo(SchedulingPolicy.PipelinedJobs));
            Assert.That(profile.Revision, Is.EqualTo(1u));
        }

        [Test]
        public void TryCreate_OptionalValuesLeftUnset_StayNullNotDefaulted()
        {
            Assert.That(SchedulingProfile.TryCreate(
                "game.ai.minion", 0, 1u, null, null, false, null,
                out var profile, out var error), Is.True, error.ToString());
            Assert.That(profile.MaximumResponseLatencyFrames, Is.Null);
            Assert.That(profile.MaximumBudgetShare, Is.Null);
            Assert.That(profile.ForcedPolicy, Is.Null);
        }

        [TestCase(null, SchedulingProfileValidationError.MissingId)]
        [TestCase("", SchedulingProfileValidationError.MissingId)]
        [TestCase("   ", SchedulingProfileValidationError.MissingId)]
        public void TryCreate_MissingId_FailsClosedWithoutClamping(string id, SchedulingProfileValidationError expected)
        {
            Assert.That(SchedulingProfile.TryCreate(id, 0, 1u, null, null, false, null, out var profile, out var error), Is.False);
            Assert.That(profile, Is.Null, "A rejected profile must not be silently constructed.");
            Assert.That(error, Is.EqualTo(expected));
        }

        [Test]
        public void TryCreate_ZeroUpdateCadence_Rejected()
        {
            Assert.That(SchedulingProfile.TryCreate("x", 0, 0u, null, null, false, null, out var profile, out var error), Is.False);
            Assert.That(profile, Is.Null);
            Assert.That(error, Is.EqualTo(SchedulingProfileValidationError.UpdateCadenceMustBeAtLeastOne));
        }

        [Test]
        public void TryCreate_MaximumResponseLatencyBelowCadence_Rejected()
        {
            Assert.That(SchedulingProfile.TryCreate("x", 0, 4u, 3u, null, false, null, out var profile, out var error), Is.False);
            Assert.That(profile, Is.Null);
            Assert.That(error, Is.EqualTo(SchedulingProfileValidationError.MaximumResponseLatencyFramesBelowUpdateCadence));
        }

        [TestCase(0.0)]
        [TestCase(-0.1)]
        [TestCase(1.0000001)]
        public void TryCreate_BudgetShareOutOfRange_Rejected(double share)
        {
            Assert.That(SchedulingProfile.TryCreate("x", 0, 1u, null, share, false, null, out var profile, out var error), Is.False);
            Assert.That(profile, Is.Null);
            Assert.That(error, Is.EqualTo(SchedulingProfileValidationError.MaximumBudgetShareOutOfRange));
        }

        [Test]
        public void Asset_FirstFreeze_StartsAtRevisionOne()
        {
            var asset = ScriptableObject.CreateInstance<SchedulingProfileAsset>();
            try
            {
                SetId(asset, "game.ai.turret");
                Assert.That(asset.TryFreeze(out var profile, out var error), Is.True, error.ToString());
                Assert.That(profile.Revision, Is.EqualTo(1u));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_RepeatedFreezeWithoutEdit_KeepsSameRevision()
        {
            var asset = ScriptableObject.CreateInstance<SchedulingProfileAsset>();
            try
            {
                SetId(asset, "game.ai.turret");
                Assert.That(asset.TryFreeze(out var first, out _), Is.True);
                Assert.That(asset.TryFreeze(out var second, out _), Is.True);
                Assert.That(second.Revision, Is.EqualTo(first.Revision));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_EditThenFreeze_BumpsRevisionAndValue()
        {
            var asset = ScriptableObject.CreateInstance<SchedulingProfileAsset>();
            try
            {
                SetId(asset, "game.ai.turret");
                Assert.That(asset.TryFreeze(out var first, out _), Is.True);

                SetPriority(asset, 9);
                InvokeOnValidate(asset);
                Assert.That(asset.TryFreeze(out var second, out _), Is.True);

                Assert.That(second.Revision, Is.EqualTo(first.Revision + 1u));
                Assert.That(second.Priority, Is.EqualTo(9));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Asset_InvalidId_FreezeFailsClosed()
        {
            var asset = ScriptableObject.CreateInstance<SchedulingProfileAsset>();
            try
            {
                Assert.That(asset.TryFreeze(out var profile, out var error), Is.False);
                Assert.That(profile, Is.Null);
                Assert.That(error, Is.EqualTo(SchedulingProfileValidationError.MissingId));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static void SetId(SchedulingProfileAsset asset, string id)
        {
            var field = typeof(SchedulingProfileAsset).GetField("_id",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(asset, id);
        }

        private static void SetPriority(SchedulingProfileAsset asset, int priority)
        {
            var field = typeof(SchedulingProfileAsset).GetField("_priority",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(asset, priority);
        }

        private static void InvokeOnValidate(SchedulingProfileAsset asset)
        {
            var method = typeof(SchedulingProfileAsset).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(asset, null);
        }
    }
}
