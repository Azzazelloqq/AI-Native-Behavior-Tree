using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Layout;
using AIBT.Editor.Organization;
using AIBT.Editor.Patching;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Patching
{
    public sealed class LayoutPatchTransactionTests
    {
        [Test]
        public void ValidPatchIsAcceptedAndDiffedCorrectly()
        {
            var before = MinimalLayout();
            var expectedHash = ComputeHash(before);

            var result = LayoutPatchTransaction.Apply(
                before,
                expectedHash,
                new Func<LayoutDocument, LayoutDocument>[]
                {
                    doc => LayoutOrganizationOperations.Pin(doc, new NodeId("guard")),
                });

            Assert.That(result.Accepted, Is.True, DiagnosticsText(result.Diagnostics));
            Assert.That(result.Document.Nodes[new NodeId("guard")].Pinned, Is.True);
            Assert.That(result.Diff.Entries.Select(e => e.Target + ":" + e.Key + ":" + e.Kind),
                Is.EquivalentTo(new[] { "Node:guard:PinChanged" }));
            Assert.That(result.ResultHash, Is.Not.EqualTo(expectedHash));
        }

        [Test]
        public void RealOperationConflictExceptionIsRejectedWithTheDocumentUnchanged()
        {
            var before = MinimalLayout();
            before = LayoutOrganizationOperations.AddOrUpdateGroup(before, "groupA", "Group A", new[] { new NodeId("guard") });
            var expectedHash = ComputeHash(before);

            // AddOrUpdateGroup throws ArgumentException when a member already belongs to another group.
            var result = LayoutPatchTransaction.Apply(
                before,
                expectedHash,
                new Func<LayoutDocument, LayoutDocument>[]
                {
                    doc => LayoutOrganizationOperations.AddOrUpdateGroup(doc, "groupB", "Group B", new[] { new NodeId("guard") }),
                });

            Assert.That(result.Accepted, Is.False);
            Assert.That(ReferenceEquals(result.Document, before), Is.True, "A rejected layout patch must return the exact input reference.");
            Assert.That(result.Diagnostics.Single().Code.Value, Is.EqualTo("AIBT9011"));
            Assert.That(result.Diff.IsEmpty, Is.True);
        }

        [Test]
        public void HashMismatchIsRejectedBeforeAnyOperationRuns()
        {
            var before = MinimalLayout();
            var operationRan = false;

            var result = LayoutPatchTransaction.Apply(
                before,
                "not-the-real-hash",
                new Func<LayoutDocument, LayoutDocument>[]
                {
                    doc => { operationRan = true; return LayoutOrganizationOperations.Pin(doc, new NodeId("guard")); },
                });

            Assert.That(result.Accepted, Is.False);
            Assert.That(operationRan, Is.False, "A hash mismatch must reject before invoking any operation.");
            Assert.That(result.Diagnostics.Single().Code.Value, Is.EqualTo("AIBT9010"));
            Assert.That(ReferenceEquals(result.Document, before), Is.True);
        }

        [Test]
        public void RepeatedCallsWithTheSameInputProduceTheSameHashAndNeverPersist()
        {
            var before = MinimalLayout();
            var expectedHash = ComputeHash(before);
            Func<LayoutDocument, LayoutDocument>[] operations =
            {
                doc => LayoutOrganizationOperations.Pin(doc, new NodeId("guard")),
            };

            var first = LayoutPatchTransaction.Apply(before, expectedHash, operations);
            var second = LayoutPatchTransaction.Apply(before, expectedHash, operations);

            Assert.That(first.Accepted, Is.True);
            Assert.That(second.Accepted, Is.True);
            Assert.That(first.ResultHash, Is.EqualTo(second.ResultHash),
                "Calling the transaction twice (the only way to 'dry-run' -- there is no persistence step to skip) must be side-effect-free and repeatable.");
            Assert.That(before.Nodes[new NodeId("guard")].Pinned, Is.False, "The original input document must never be mutated by any call.");
        }

        private static LayoutDocument MinimalLayout()
        {
            var nodes = new Dictionary<NodeId, LayoutNodePlacement>
            {
                { new NodeId("root"), new LayoutNodePlacement(new LayoutPoint(0, 0)) },
                { new NodeId("guard"), new LayoutNodePlacement(new LayoutPoint(100, 0)) },
            };
            return new LayoutDocument(new TreeId("tree.p6-004.layout-test"), LayoutDirection.TopToBottom, nodes);
        }

        private static string ComputeHash(LayoutDocument document)
        {
            return StableHash.Sha256Hex(CanonicalLayoutJsonWriter.Write(document));
        }

        private static string DiagnosticsText(DiagnosticCollection diagnostics)
        {
            return diagnostics == null ? string.Empty : string.Join("; ", diagnostics.Select(d => d.Code.Value + ": " + d.Message));
        }
    }
}
