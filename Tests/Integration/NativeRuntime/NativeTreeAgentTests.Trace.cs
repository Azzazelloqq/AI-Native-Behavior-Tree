using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-016 item 2 (ADR-P7-010 amendment 2026-11-24): a <see cref="NativeTreeAgent"/>'s host has a trace
    /// channel by its Auto / Off / On field; Auto is on in the Editor and off in a Player, and old scenes
    /// and prefabs read as Auto.
    /// </summary>
    public sealed partial class NativeTreeAgentTests
    {
        [Test]
        public void Trace_ResolvesAutoOffAndOnForTheEditorAndAPlayer()
        {
            var auto = NativeTreeAgentTraceMode.Auto;
            var records = NativeTreeAgent.DefaultTraceRecords;
            var editor = NativeTreeAgent.ResolveTraceCapacity(auto, records, isEditor: true);
            Assert.That(editor.HasValue, Is.True);
            Assert.That(editor.Value.RecordCapacity, Is.EqualTo(256u));
            Assert.That(editor.Value.EmissionCapacity, Is.EqualTo(512u), "Auto in the Editor is today's default.");
            Assert.That(NativeTreeAgent.ResolveTraceCapacity(auto, records, isEditor: false).HasValue, Is.False, "Auto in a Player.");
            Assert.That(NativeTreeAgent.ResolveTraceCapacity(NativeTreeAgentTraceMode.Off, records, true).HasValue, Is.False);
            Assert.That(NativeTreeAgent.ResolveTraceCapacity(NativeTreeAgentTraceMode.Off, records, false).HasValue, Is.False);
            var player = NativeTreeAgent.ResolveTraceCapacity(NativeTreeAgentTraceMode.On, 8, isEditor: false);
            Assert.That(player.HasValue, Is.True, "On in a Player.");
            Assert.That(player.Value.RecordCapacity, Is.EqualTo(8u));
            Assert.That(player.Value.EmissionCapacity, Is.EqualTo(16u));
            Assert.That(NativeTreeAgent.ResolveTraceCapacity(NativeTreeAgentTraceMode.On, 0, true).Value.RecordCapacity,
                Is.EqualTo((uint)NativeTreeAgent.MinimumTraceRecords), "Below the minimum is clamped.");
        }

        [Test]
        public void Trace_AnAgentSavedBeforeTheFieldReadsAsAuto()
        {
            // A component serialized before P11-016 has neither field; the new defaults apply.
            var gameObject = Track(new GameObject("AIBT.Tests.P11016.OldAgent"));
            var old = gameObject.AddComponent<NativeTreeAgent>();
            JsonUtility.FromJsonOverwrite("{\"_restart\":1,\"_effectCapacity\":16}", old);
            Assert.That(old.RestartMode, Is.EqualTo(NativeTreeRestartMode.Never), "The old fields were read.");
            Assert.That(old.TraceMode, Is.EqualTo(NativeTreeAgentTraceMode.Auto));
            Assert.That(old.TraceRecordCapacity, Is.EqualTo(NativeTreeAgent.DefaultTraceRecords));
            Assert.That((int)NativeTreeAgentTraceMode.Auto, Is.Zero, "A zero-filled field reads as Auto.");
        }

        [Test]
        public void Trace_AutoInTheEditorOwnsTodaysChannel()
        {
            var agent = Spawn(Prepare(ReaderTree), NativeTreeRestartMode.Never);
            Assert.That(agent.Host, Is.Not.Null, agent.LastError);
            Assert.That(agent.Host.TraceChannelOwner, Is.Not.Null);
            Assert.That(agent.Host.TraceChannelOwner.Capacity.RecordCapacity, Is.EqualTo(256u));
            Assert.That(agent.Host.TraceChannelOwner.Capacity.EmissionCapacity, Is.EqualTo(512u));
        }

        [Test]
        public void Trace_OffRunsWithoutAChannel_AndOnTakesTheRecordCapacity()
        {
            var asset = Prepare(ReaderTree);
            var off = Spawn(asset, NativeTreeRestartMode.Never, activate: false);
            off.TraceMode = NativeTreeAgentTraceMode.Off;
            off.Activate();
            var on = Spawn(asset, NativeTreeRestartMode.Never, activate: false);
            on.TraceMode = NativeTreeAgentTraceMode.On;
            on.TraceRecordCapacity = 8;
            on.Activate();
            Assert.That(off.Host, Is.Not.Null, off.LastError);
            Assert.That(on.Host, Is.Not.Null, on.LastError);
            Assert.That(off.Host.TraceChannelOwner, Is.Null);
            Assert.That(on.Host.TraceChannelOwner.Capacity.RecordCapacity, Is.EqualTo(8u));
            Assert.That(on.Host.TraceChannelOwner.Capacity.EmissionCapacity, Is.EqualTo(16u));

            off.SetFact("live-target", 1f);
            on.SetFact("live-target", 1f);
            Frames(1);
            Assert.That(off.LastResult, Is.EqualTo(NodeStatus.Success), "An agent without a channel runs the same.");
            Assert.That(on.LastResult, Is.EqualTo(NodeStatus.Success));
        }
    }
}
