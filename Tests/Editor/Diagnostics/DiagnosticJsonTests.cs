using System;
using System.Collections.Generic;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Diagnostics
{
    public sealed class DiagnosticJsonTests
    {
        private static readonly DiagnosticPayloadContract RemoveNodeContract = DiagnosticPayloadContract.Map(
            new DiagnosticPayloadPropertyContract(
                "nodeId",
                DiagnosticPayloadContract.Scalar(DiagnosticPayloadKind.String)));

        [Test]
        public void Serialize_EmitsRequiredFieldsAndOmitsUnknownLocation()
        {
            var diagnostic = new AuthoringDiagnostic(
                new Diagnostic(
                    DiagnosticCode.Parse("AIBT1001"),
                    DiagnosticSeverity.Error,
                    "Invalid document."));

            var json = DiagnosticJson.Serialize(diagnostic);

            Assert.That(json, Is.EqualTo(
                "{\n" +
                "  \"code\": \"AIBT1001\",\n" +
                "  \"severity\": \"error\",\n" +
                "  \"message\": \"Invalid document.\"\n" +
                "}\n"));
            Assert.That(DiagnosticJson.SerializeUtf8(diagnostic), Is.EqualTo(Encoding.UTF8.GetBytes(json)));
        }

        [Test]
        public void Serialize_UsesExplicitCanonicalDiagnosticPropertyOrder()
        {
            Assert.That(DiagnosticJsonContract.DiagnosticProperties, Is.EqualTo(new[]
            {
                "code", "severity", "message", "treeId", "nodeId", "treeInstanceId", "documentId",
                "jsonPointer", "line", "column", "relatedLocations", "suggestedOperation",
            }));

            var operation = CreateRegistry().Create(
                "tree.removeNode",
                "aibt.tree.remove-node.v1",
                NodePayload("n1"));
            var diagnostic = new AuthoringDiagnostic(
                new Diagnostic(
                    DiagnosticCode.Parse("AIBT2001"),
                    DiagnosticSeverity.Warning,
                    "Node warning.",
                    new DiagnosticLocation(
                        documentId: "tree.aibt.json",
                        jsonPointer: "/nodes/n1",
                        line: 4,
                        column: 7,
                        treeId: new TreeId("tree"),
                        nodeId: new NodeId("n1"),
                        treeInstanceId: new TreeInstanceId(9)),
                    new[] { new DiagnosticLocation(documentId: "related") }),
                operation);

            Assert.That(DiagnosticJson.Serialize(diagnostic), Is.EqualTo(
                "{\n" +
                "  \"code\": \"AIBT2001\",\n" +
                "  \"severity\": \"warning\",\n" +
                "  \"message\": \"Node warning.\",\n" +
                "  \"treeId\": \"tree\",\n" +
                "  \"nodeId\": \"n1\",\n" +
                "  \"treeInstanceId\": \"9\",\n" +
                "  \"documentId\": \"tree.aibt.json\",\n" +
                "  \"jsonPointer\": \"/nodes/n1\",\n" +
                "  \"line\": 4,\n" +
                "  \"column\": 7,\n" +
                "  \"relatedLocations\": [\n" +
                "    {\n" +
                "      \"documentId\": \"related\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"suggestedOperation\": {\n" +
                "    \"operationId\": \"tree.removeNode\",\n" +
                "    \"payloadType\": \"aibt.tree.remove-node.v1\",\n" +
                "    \"payload\": {\n" +
                "      \"nodeId\": \"n1\"\n" +
                "    }\n" +
                "  }\n" +
                "}\n"));
        }

        [Test]
        public void Payload_MapPermutationsHaveIdenticalCanonicalRepresentationEqualityAndHash()
        {
            var first = DiagnosticOperationPayload.Map(
                new DiagnosticPayloadMember("z", DiagnosticOperationPayload.From(1)),
                new DiagnosticPayloadMember("a", DiagnosticOperationPayload.From("value")));
            var second = DiagnosticOperationPayload.Map(
                new DiagnosticPayloadMember("a", DiagnosticOperationPayload.From("value")),
                new DiagnosticPayloadMember("z", DiagnosticOperationPayload.From(1L)));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.CompareTo(second), Is.Zero);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void Payload_MapKeysUseOrdinalUtf8ByteOrdering()
        {
            var contract = DiagnosticPayloadContract.Map(
                new DiagnosticPayloadPropertyContract("\U00010000", DiagnosticPayloadContract.Scalar(DiagnosticPayloadKind.Int32)),
                new DiagnosticPayloadPropertyContract("\uE000", DiagnosticPayloadContract.Scalar(DiagnosticPayloadKind.Int32)));
            var registry = new DiagnosticOperationRegistry(new[]
            {
                new DiagnosticOperationDescriptor("tree.unicodeOrder", "aibt.tree.unicode-order.v1", contract),
            });
            var operation = registry.Create(
                "tree.unicodeOrder",
                "aibt.tree.unicode-order.v1",
                DiagnosticOperationPayload.Map(
                    new DiagnosticPayloadMember("\U00010000", DiagnosticOperationPayload.From(2)),
                    new DiagnosticPayloadMember("\uE000", DiagnosticOperationPayload.From(1))));

            var json = DiagnosticJson.Serialize(new AuthoringDiagnostic(
                new Diagnostic(DiagnosticCode.Parse("AIBT2001"), DiagnosticSeverity.Warning, "Unicode."),
                operation));

            Assert.That(
                json.IndexOf("\"\uE000\"", StringComparison.Ordinal),
                Is.LessThan(json.IndexOf("\"\U00010000\"", StringComparison.Ordinal)));
        }

        [Test]
        public void Payload_UsesCanonicalNumbersAndEscapesWithoutUnicodeNormalization()
        {
            var contract = DiagnosticPayloadContract.ArrayOf(DiagnosticPayloadContract.Scalar(DiagnosticPayloadKind.Float64));
            var registry = new DiagnosticOperationRegistry(new[]
            {
                new DiagnosticOperationDescriptor("tree.setNumbers", "aibt.tree.numbers.v1", contract),
            });
            var operation = registry.Create(
                "tree.setNumbers",
                "aibt.tree.numbers.v1",
                DiagnosticOperationPayload.Array(
                    DiagnosticOperationPayload.From(-0d),
                    DiagnosticOperationPayload.From(1.25d),
                    DiagnosticOperationPayload.From(1e-7d),
                    DiagnosticOperationPayload.From(1e20d)));

            var json = DiagnosticJson.Serialize(new AuthoringDiagnostic(
                new Diagnostic(DiagnosticCode.Parse("AIBT2001"), DiagnosticSeverity.Info, "line\n\u00e9"),
                operation));

            Assert.That(json, Does.Contain("\"message\": \"line\\n\u00e9\""));
            Assert.That(json, Does.Contain("      0,\n      1.25,\n      1e-7,\n      1e20"));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Payload_RejectsNonFiniteFloat32(float value)
        {
            Assert.That(() => DiagnosticOperationPayload.From(value), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Payload_RejectsNonFiniteFloat64(double value)
        {
            Assert.That(() => DiagnosticOperationPayload.From(value), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Payload_DoesNotApplyUnicodeNormalization()
        {
            var composed = DiagnosticOperationPayload.From("\u00e9");
            var decomposed = DiagnosticOperationPayload.From("e\u0301");

            Assert.That(composed, Is.Not.EqualTo(decomposed));
            Assert.That(composed.CompareTo(decomposed), Is.Not.Zero);
        }

        [Test]
        public void Payload_RejectsInvalidUnicodeDuplicateKeysAndNullReferences()
        {
            Assert.That(() => DiagnosticOperationPayload.From("\uD800"), Throws.ArgumentException);
            Assert.That(
                () => DiagnosticOperationPayload.Map(
                    new DiagnosticPayloadMember("x", DiagnosticOperationPayload.Null),
                    new DiagnosticPayloadMember("x", DiagnosticOperationPayload.Null)),
                Throws.ArgumentException);
            Assert.That(
                () => DiagnosticOperationPayload.Array(new DiagnosticOperationPayload[] { null }),
                Throws.ArgumentException);
        }

        [Test]
        public void Serialize_RejectsInvalidUnicodeInRuntimeMessage()
        {
            var diagnostic = new AuthoringDiagnostic(
                new Diagnostic(DiagnosticCode.Parse("AIBT1001"), DiagnosticSeverity.Error, "\uD800"));

            Assert.That(() => DiagnosticJson.Serialize(diagnostic), Throws.ArgumentException);
        }

        [Test]
        public void Registry_RejectsUnknownOrMismatchedContractsAndInvalidPayloadShape()
        {
            var registry = CreateRegistry();

            Assert.That(
                () => registry.Create("tree.unknown", "aibt.tree.remove-node.v1", NodePayload("n1")),
                Throws.ArgumentException);
            Assert.That(
                () => registry.Create("tree.removeNode", "aibt.tree.other.v1", NodePayload("n1")),
                Throws.ArgumentException);
            Assert.That(
                () => registry.Create(
                    "tree.removeNode",
                    "aibt.tree.remove-node.v1",
                    DiagnosticOperationPayload.Map(new DiagnosticPayloadMember("other", DiagnosticOperationPayload.From("n1")))),
                Throws.ArgumentException);
            Assert.That(
                () => new DiagnosticOperationDescriptor(
                    "not stable",
                    "aibt.tree.remove-node.v1",
                    RemoveNodeContract),
                Throws.ArgumentException);
        }

        [Test]
        public void SuggestedOperation_ComparisonEqualityAndHashUseSameCanonicalBytes()
        {
            var registry = CreateRegistry();
            var first = registry.Create("tree.removeNode", "aibt.tree.remove-node.v1", NodePayload("n1"));
            var equivalent = registry.Create("tree.removeNode", "aibt.tree.remove-node.v1", NodePayload("n1"));
            var later = registry.Create("tree.removeNode", "aibt.tree.remove-node.v1", NodePayload("n2"));

            Assert.That(first, Is.EqualTo(equivalent));
            Assert.That(first.CompareTo(equivalent), Is.Zero);
            Assert.That(first.GetHashCode(), Is.EqualTo(equivalent.GetHashCode()));
            Assert.That(first.CompareTo(later), Is.LessThan(0));
        }

        [Test]
        public void AuthoringFactory_ValidatesSuggestedOperationAgainstDiagnosticCatalog()
        {
            var code = DiagnosticCode.Parse("AIBT2001");
            var catalog = new DiagnosticCatalog(new[]
            {
                new DiagnosticDescriptor(
                    code,
                    DiagnosticSubsystem.SemanticValidation,
                    DiagnosticSeverity.Warning,
                    DiagnosticField.SuggestedOperation),
            });
            var runtimeDiagnostic = new Diagnostic(code, DiagnosticSeverity.Warning, "Safe fix.");
            var operation = CreateRegistry().Create(
                "tree.removeNode",
                "aibt.tree.remove-node.v1",
                NodePayload("n1"));

            Assert.That(
                () => AuthoringDiagnostic.CreateValidated(catalog, runtimeDiagnostic),
                Throws.ArgumentException);
            Assert.That(
                AuthoringDiagnostic.CreateValidated(catalog, runtimeDiagnostic, operation).SuggestedOperation,
                Is.SameAs(operation));
        }

        private static DiagnosticOperationRegistry CreateRegistry()
        {
            return new DiagnosticOperationRegistry(new[]
            {
                new DiagnosticOperationDescriptor("tree.removeNode", "aibt.tree.remove-node.v1", RemoveNodeContract),
            });
        }

        private static DiagnosticOperationPayload NodePayload(string nodeId)
        {
            return DiagnosticOperationPayload.Map(
                new DiagnosticPayloadMember("nodeId", DiagnosticOperationPayload.From(nodeId)));
        }
    }
}
