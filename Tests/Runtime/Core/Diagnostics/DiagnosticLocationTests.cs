using NUnit.Framework;

namespace AIBT.Tests.Runtime.Core.Diagnostics
{
    public sealed class DiagnosticLocationTests
    {
        [Test]
        public void DefaultLocation_RepresentsUnknownFieldsWithoutInventingValues()
        {
            var location = default(DiagnosticLocation);

            Assert.That(location.IsKnown, Is.False);
            Assert.That(location.HasDocumentId, Is.False);
            Assert.That(location.HasJsonPointer, Is.False);
            Assert.That(location.Line, Is.Null);
            Assert.That(location.Column, Is.Null);
            Assert.That(location.TreeId.IsValid, Is.False);
            Assert.That(location.NodeId.IsValid, Is.False);
            Assert.That(location.TreeInstanceId.IsValid, Is.False);
        }

        [Test]
        public void EmptyJsonPointer_IsKnownRootAndDistinctFromUnknown()
        {
            var root = new DiagnosticLocation(jsonPointer: string.Empty);

            Assert.That(root.IsKnown, Is.True);
            Assert.That(root.HasJsonPointer, Is.True);
            Assert.That(root.JsonPointer, Is.Empty);
            Assert.That(root, Is.Not.EqualTo(default(DiagnosticLocation)));
        }

        [TestCase("nodes/0")]
        [TestCase("/nodes/~")]
        [TestCase("/nodes/~2")]
        public void Location_RejectsInvalidRfc6901JsonPointer(string pointer)
        {
            Assert.That(() => new DiagnosticLocation(jsonPointer: pointer), Throws.ArgumentException);
        }

        [TestCase("/nodes/0")]
        [TestCase("/a~1b")]
        [TestCase("/m~0n")]
        public void Location_AcceptsValidRfc6901JsonPointer(string pointer)
        {
            Assert.That(new DiagnosticLocation(jsonPointer: pointer).JsonPointer, Is.EqualTo(pointer));
        }

        [Test]
        public void Location_RejectsEmptyDocumentIdentity()
        {
            Assert.That(() => new DiagnosticLocation(documentId: string.Empty), Throws.ArgumentException);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Location_RejectsNonOneBasedLine(int line)
        {
            Assert.That(() => new DiagnosticLocation(line: line), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Location_RejectsNonOneBasedColumn(int column)
        {
            Assert.That(() => new DiagnosticLocation(column: column), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void Location_UsesOrdinalDeterministicOrderingWithMissingValuesFirst()
        {
            var unknown = default(DiagnosticLocation);
            var lowerDocument = new DiagnosticLocation(documentId: "A");
            var upperDocument = new DiagnosticLocation(documentId: "a");

            Assert.That(unknown.CompareTo(lowerDocument), Is.LessThan(0));
            Assert.That(lowerDocument.CompareTo(upperDocument), Is.LessThan(0));
        }
    }
}
