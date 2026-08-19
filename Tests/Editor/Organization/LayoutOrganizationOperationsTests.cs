using System.Linq;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using AIBT.Tests.Editor.Layout;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Organization
{
    public sealed class LayoutOrganizationOperationsTests
    {
        [Test]
        public void PinnedNodePositionSurvivesAutoLayoutRerun()
        {
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");
            var initial = DeterministicAutoLayoutService.Layout(tree);

            var customPosition = new LayoutPoint(999, 111);
            var withMove = LayoutOrganizationOperations.SetNodePosition(initial, new NodeId("a"), customPosition);
            var pinned = LayoutOrganizationOperations.Pin(withMove, new NodeId("a"));

            // Re-run auto-layout "on the rest of the tree" -- same tree, existing layout passed back in.
            var relaidOut = DeterministicAutoLayoutService.Layout(tree, pinned);

            Assert.That(relaidOut.Nodes[new NodeId("a")].Position, Is.EqualTo(customPosition));
            Assert.That(relaidOut.Nodes[new NodeId("a")].Pinned, Is.True);
        }

        [Test]
        public void AddOrUpdateGroupRejectsNodeAlreadyInAnotherGroup()
        {
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");
            var layout = DeterministicAutoLayoutService.Layout(tree);
            var withGroup = LayoutOrganizationOperations.AddOrUpdateGroup(layout, "g1", "G1", new[] { new NodeId("a") });

            Assert.Throws<System.ArgumentException>(() =>
                LayoutOrganizationOperations.AddOrUpdateGroup(withGroup, "g2", "G2", new[] { new NodeId("a") }));
        }

        [Test]
        public void GroupNoteRerouteAddAndRemoveRoundTrip()
        {
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");
            var layout = DeterministicAutoLayoutService.Layout(tree);

            var withGroup = LayoutOrganizationOperations.AddOrUpdateGroup(layout, "g1", "Group", new[] { new NodeId("a"), new NodeId("b") });
            Assert.That(withGroup.Groups.ContainsKey("g1"), Is.True);
            var withoutGroup = LayoutOrganizationOperations.RemoveGroup(withGroup, "g1");
            Assert.That(withoutGroup.Groups.ContainsKey("g1"), Is.False);

            var withNote = LayoutOrganizationOperations.AddOrUpdateNote(layout, "n1", "Hello", new LayoutPoint(1, 1), new LayoutPoint(10, 10));
            Assert.That(withNote.Notes.ContainsKey("n1"), Is.True);
            var withoutNote = LayoutOrganizationOperations.RemoveNote(withNote, "n1");
            Assert.That(withoutNote.Notes.ContainsKey("n1"), Is.False);

            var withReroute = LayoutOrganizationOperations.AddOrUpdateReroute(layout, new NodeId("root"), new NodeId("c"), new[] { new LayoutPoint(2, 2) });
            Assert.That(withReroute.Reroutes.Count, Is.EqualTo(1));
            var withoutReroute = LayoutOrganizationOperations.RemoveReroute(withReroute, new NodeId("root"), new NodeId("c"));
            Assert.That(withoutReroute.Reroutes.Count, Is.EqualTo(0));
        }

        [Test]
        public void HistoryUndoRedoRestoresExactSnapshots()
        {
            var tree = CanonicalLayoutJsonTests.ParseTreeFixture("shallow-wide-4.aibt.json");
            var initial = DeterministicAutoLayoutService.Layout(tree);
            var history = new LayoutHistory(initial);

            var pinned = LayoutOrganizationOperations.Pin(initial, new NodeId("a"));
            history.Do(pinned);

            var grouped = LayoutOrganizationOperations.AddOrUpdateGroup(pinned, "g1", "Group", new[] { new NodeId("a") });
            history.Do(grouped);

            Assert.That(history.Current.Groups.Count, Is.EqualTo(1));

            var afterFirstUndo = history.Undo();
            Assert.That(afterFirstUndo.Groups.Count, Is.EqualTo(0));
            Assert.That(afterFirstUndo.Nodes[new NodeId("a")].Pinned, Is.True);

            var afterSecondUndo = history.Undo();
            Assert.That(afterSecondUndo.Nodes[new NodeId("a")].Pinned, Is.False);
            Assert.That(history.CanUndo, Is.False);

            var afterRedo = history.Redo();
            Assert.That(afterRedo.Nodes[new NodeId("a")].Pinned, Is.True);

            var afterSecondRedo = history.Redo();
            Assert.That(afterSecondRedo.Groups.Count, Is.EqualTo(1));
            Assert.That(history.CanRedo, Is.False);
        }
    }
}
