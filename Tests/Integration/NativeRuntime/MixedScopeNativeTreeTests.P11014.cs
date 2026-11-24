using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    public sealed partial class MixedScopeNativeTreeTests
    {
        /// <summary>
        /// P11-014 confirmation U4: how often <see cref="NativeTreeAgent.TryGetValue{T}"/> succeeds for a
        /// <c>PipelinedJobs</c> agent, over 100 frames of the looping mixed tree. "Before" is a script whose
        /// Update runs before the scheduler's; "after" is a script whose Update runs after it, or any
        /// LateUpdate (nothing of AIBT runs between the scheduler's Update and LateUpdate). The shares go
        /// into the evidence.
        /// <para>
        /// P11-014 review item 4: what decides the counts. The pipelined driver advances one stage per
        /// scheduler Update and completes the round's job inside that Update, so a group makes one logical
        /// update per (lifecycle steps + 1) frames, counted in frames, not in thread timing
        /// (<c>PipelinedJobsCadenceTests</c>). The agent is readable exactly when its host is not executing
        /// between frames. The test pins those relations instead of the measured 5 and 4: "after" is the
        /// frames whose Update left the host idle; "before" is the first frame (nothing in flight yet) plus
        /// the "after" of every frame but the last; both are above zero and at most one in three frames
        /// (a logical update takes at least three frames).
        /// </para>
        /// </summary>
        [Test]
        public void U4_APipelinedJobsAgent_ReadsBeforeAndAfterTheSchedulerUpdate_Over100Frames()
        {
            const int Frames = 100;
            var agent = Spawn(PrepareLoopAsset(), ProfileFor("forced-pipelined-jobs"));
            var before = 0;
            var after = 0;
            var executingAfter = 0;
            var readableAfterLast = false;
            for (var frame = 0; frame < Frames; frame++)
            {
                if (agent.TryGetValue("agent-counter", out int _)) before++;
                Frame();
                var readable = agent.TryGetValue("agent-counter", out int _);
                if (readable) after++;
                if (agent.Host != null && agent.Host.IsExecuting) executingAfter++;
                Assert.That(readable, Is.EqualTo(agent.Host != null && !agent.Host.IsExecuting), "frame " + frame + ": readable exactly when not executing");
                readableAfterLast = readable;
            }
            Debug.Log("P11-014 U4: PipelinedJobs TryGetValue succeeded " + before + "/" + Frames + " before the scheduler's Update and "
                + after + "/" + Frames + " after it (LateUpdate); the host was executing after " + executingAfter + "/" + Frames + " updates.");
            Assert.That(agent.LastError, Is.Null);
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            // Measured 2026-11-24: 5/100 before, 4/100 after, executing after 96/100 updates (P11-014 evidence).
            Assert.That(after, Is.EqualTo(Frames - executingAfter), "after the Update: readable exactly when idle");
            Assert.That(before, Is.EqualTo(1 + after - (readableAfterLast ? 1 : 0)), "before an Update: the previous frame's state");
            Assert.That(after, Is.InRange(1, (Frames + 2) / 3), "at least one logical update, each at least three frames");
        }
    }
}
