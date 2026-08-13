using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.Core.Diagnostics
{
    public sealed class DiagnosticCodeAndCatalogTests
    {
        [TestCase("AIBT0001")]
        [TestCase("AIBT0999")]
        [TestCase("AIBT1000")]
        [TestCase("AIBT9999")]
        public void DiagnosticCode_RoundTripsValidCode(string value)
        {
            Assert.That(DiagnosticCode.TryParse(value, out var code), Is.True);
            Assert.That(code.IsValid, Is.True);
            Assert.That(code.ToString(), Is.EqualTo(value));
            Assert.That(DiagnosticCode.Parse(value), Is.EqualTo(code));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("aibt0001")]
        [TestCase("AIBT001")]
        [TestCase("AIBT00001")]
        [TestCase("AIBT00A1")]
        public void DiagnosticCode_RejectsNonCanonicalCode(string value)
        {
            Assert.That(DiagnosticCode.TryParse(value, out var code), Is.False);
            Assert.That(code, Is.EqualTo(default(DiagnosticCode)));
            Assert.That(() => DiagnosticCode.Parse(value), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void CodeRanges_ContainOnlyReservedSubsystemCodes()
        {
            Assert.That(
                DiagnosticCodeRanges.For(DiagnosticSubsystem.CoreRuntime).Contains(DiagnosticCode.Parse("AIBT0001")),
                Is.True);
            Assert.That(
                DiagnosticCodeRanges.For(DiagnosticSubsystem.CoreRuntime).Contains(DiagnosticCode.Parse("AIBT1000")),
                Is.False);
            Assert.That(
                DiagnosticCodeRanges.For(DiagnosticSubsystem.ToolingAndTestInput).Contains(DiagnosticCode.Parse("AIBT9000")),
                Is.True);
        }

        [Test]
        public void Catalog_ExposesDefaultSeverityAndFieldContractByCode()
        {
            var descriptor = new DiagnosticDescriptor(
                DiagnosticCode.Parse("AIBT2001"),
                DiagnosticSubsystem.SemanticValidation,
                DiagnosticSeverity.Error,
                DiagnosticField.NodeId,
                DiagnosticField.JsonPointer | DiagnosticField.RelatedLocations);
            var catalog = new DiagnosticCatalog(new[] { descriptor });

            Assert.That(catalog.TryGet(descriptor.Code, out var found), Is.True);
            Assert.That(found, Is.SameAs(descriptor));
            Assert.That(found.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(found.RequiredFields, Is.EqualTo(DiagnosticField.NodeId));
            Assert.That(found.OptionalFields, Is.EqualTo(DiagnosticField.JsonPointer | DiagnosticField.RelatedLocations));
        }

        [Test]
        public void Descriptor_RejectsCodeOutsideDeclaredSubsystem()
        {
            Assert.That(
                () => new DiagnosticDescriptor(
                    DiagnosticCode.Parse("AIBT4001"),
                    DiagnosticSubsystem.SemanticValidation,
                    DiagnosticSeverity.Error),
                Throws.ArgumentException);
        }

        [Test]
        public void Catalog_RejectsDuplicateCodeEntries()
        {
            var first = new DiagnosticDescriptor(
                DiagnosticCode.Parse("AIBT3001"),
                DiagnosticSubsystem.RegistryAndCompiler,
                DiagnosticSeverity.Error);
            var duplicate = new DiagnosticDescriptor(
                DiagnosticCode.Parse("AIBT3001"),
                DiagnosticSubsystem.RegistryAndCompiler,
                DiagnosticSeverity.Warning);

            Assert.That(() => new DiagnosticCatalog(new[] { first, duplicate }), Throws.ArgumentException);
        }

        [Test]
        public void Descriptor_RejectsUndefinedFieldMaskBits()
        {
            Assert.That(
                () => new DiagnosticDescriptor(
                    DiagnosticCode.Parse("AIBT2001"),
                    DiagnosticSubsystem.SemanticValidation,
                    DiagnosticSeverity.Error,
                    (DiagnosticField)(1 << 20)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void CatalogFactory_UsesDefaultSeverityAndValidatesFieldContract()
        {
            var descriptor = new DiagnosticDescriptor(
                DiagnosticCode.Parse("AIBT2001"),
                DiagnosticSubsystem.SemanticValidation,
                DiagnosticSeverity.Warning,
                DiagnosticField.NodeId,
                DiagnosticField.DocumentId | DiagnosticField.LineAndColumn);
            var catalog = new DiagnosticCatalog(new[] { descriptor });

            var diagnostic = catalog.Create(
                descriptor.Code,
                "Warning.",
                new DiagnosticLocation(
                    documentId: "tree.aibt.json",
                    line: 2,
                    column: 3,
                    nodeId: new NodeId("n1")));

            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostic.Location.NodeId, Is.EqualTo(new NodeId("n1")));
        }

        [Test]
        public void CatalogValidation_RejectsMissingUndeclaredAndPartialLineLocationFields()
        {
            var descriptor = new DiagnosticDescriptor(
                DiagnosticCode.Parse("AIBT2001"),
                DiagnosticSubsystem.SemanticValidation,
                DiagnosticSeverity.Error,
                DiagnosticField.NodeId,
                DiagnosticField.LineAndColumn | DiagnosticField.SuggestedOperation);
            var catalog = new DiagnosticCatalog(new[] { descriptor });

            Assert.That(
                () => catalog.Create(descriptor.Code, "Missing node."),
                Throws.ArgumentException);
            Assert.That(
                () => catalog.Create(
                    descriptor.Code,
                    "Undeclared document.",
                    new DiagnosticLocation(documentId: "tree", nodeId: new NodeId("n1"))),
                Throws.ArgumentException);
            Assert.That(
                () => catalog.Create(
                    descriptor.Code,
                    "Partial source position.",
                    new DiagnosticLocation(line: 2, nodeId: new NodeId("n1"))),
                Throws.ArgumentException);
        }
    }
}
