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
        /// into the evidence; the test pins that the agent stays healthy and that both
        /// counts are small but above zero ("mostly unreadable" in production-host.md holds).
        /// </summary>
        [Test]
        public void U4_APipelinedJobsAgent_ReadsBeforeAndAfterTheSchedulerUpdate_Over100Frames()
        {
            const int Frames = 100;
            var agent = Spawn(PrepareLoopAsset(), ProfileFor("forced-pipelined-jobs"));
            var before = 0;
            var after = 0;
            var executingAfter = 0;
            for (var frame = 0; frame < Frames; frame++)
            {
                if (agent.TryGetValue("agent-counter", out int _)) before++;
                Frame();
                if (agent.TryGetValue("agent-counter", out int _)) after++;
                if (agent.Host != null && agent.Host.IsExecuting) executingAfter++;
            }
            Debug.Log("P11-014 U4: PipelinedJobs TryGetValue succeeded " + before + "/" + Frames + " before the scheduler's Update and "
                + after + "/" + Frames + " after it (LateUpdate); the host was executing after " + executingAfter + "/" + Frames + " updates.");
            Assert.That(agent.LastError, Is.Null);
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            // Measured 2026-11-24: 5/100 before, 4/100 after, executing after 96/100 updates (P11-014 evidence).
            Assert.That(before, Is.InRange(1, 20), "rarely readable, but not never");
            Assert.That(after, Is.InRange(1, 20), "rarely readable, but not never");
        }
    }
}
