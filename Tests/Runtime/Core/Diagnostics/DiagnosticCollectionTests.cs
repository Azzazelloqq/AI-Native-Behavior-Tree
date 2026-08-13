using System.Collections.Generic;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.Core.Diagnostics
{
    public sealed class DiagnosticCollectionTests
    {
        [Test]
        public void Collection_SortsByStableContractRegardlessOfInsertionOrder()
        {
            var info = Create("AIBT0001", DiagnosticSeverity.Info, "Info");
            var warning = Create("AIBT0001", DiagnosticSeverity.Warning, "Warning");
            var laterCode = Create("AIBT0002", DiagnosticSeverity.Error, "Later code");
            var laterLocation = Create(
                "AIBT0001",
                DiagnosticSeverity.Error,
                "Later location",
                new DiagnosticLocation(documentId: "b"));
            var first = Create(
                "AIBT0001",
                DiagnosticSeverity.Error,
                "First",
                new DiagnosticLocation(documentId: "a"));

            var diagnostics = new DiagnosticCollection(new[]
            {
                info,
                laterLocation,
                laterCode,
                first,
                warning,
            });

            Assert.That(diagnostics, Is.EqualTo(new[] { first, laterLocation, laterCode, warning, info }));
        }

        [Test]
        public void Collection_RetainsOneExactDuplicate()
        {
            var related = new DiagnosticLocation(documentId: "related");
            var diagnostic = Create(
                "AIBT0001",
                DiagnosticSeverity.Error,
                "Message",
                new DiagnosticLocation(documentId: "tree"),
                new[] { related });
            var equivalent = Create(
                "AIBT0001",
                DiagnosticSeverity.Error,
                "Message",
                new DiagnosticLocation(documentId: "tree"),
                new[] { related });

            var diagnostics = new DiagnosticCollection(new[] { diagnostic, equivalent, diagnostic });

            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0], Is.EqualTo(diagnostic));
        }

        [Test]
        public void Collection_KeepsDiagnosticsWithSameCodeAndLocationButDifferentMessage()
        {
            var location = new DiagnosticLocation(documentId: "tree");
            var first = Create("AIBT0001", DiagnosticSeverity.Error, "A", location);
            var second = Create("AIBT0001", DiagnosticSeverity.Error, "B", location);

            var diagnostics = new DiagnosticCollection(new[] { second, first });

            Assert.That(diagnostics.Count, Is.EqualTo(2));
            Assert.That(diagnostics[0], Is.EqualTo(first));
            Assert.That(diagnostics[1], Is.EqualTo(second));
        }

        [Test]
        public void Diagnostic_DefensivelyCopiesAndSortsRelatedLocations()
        {
            var input = new List<DiagnosticLocation>
            {
                new DiagnosticLocation(documentId: "b"),
                new DiagnosticLocation(documentId: "a"),
            };
            var diagnostic = Create(
                "AIBT0001",
                DiagnosticSeverity.Error,
                "Message",
                relatedLocations: input);

            input.Clear();

            Assert.That(diagnostic.RelatedLocations.Count, Is.EqualTo(2));
            Assert.That(diagnostic.RelatedLocations[0].DocumentId, Is.EqualTo("a"));
            Assert.That(diagnostic.RelatedLocations[1].DocumentId, Is.EqualTo("b"));
        }

        [Test]
        public void Collection_RejectsNullEntries()
        {
            Assert.That(
                () => new DiagnosticCollection(new Diagnostic[] { Create("AIBT0001", DiagnosticSeverity.Error, "A"), null }),
                Throws.ArgumentException);
        }

        private static Diagnostic Create(
            string code,
            DiagnosticSeverity severity,
            string message,
            DiagnosticLocation location = default,
            IEnumerable<DiagnosticLocation> relatedLocations = null)
        {
            return new Diagnostic(DiagnosticCode.Parse(code), severity, message, location, relatedLocations);
        }
    }
}
