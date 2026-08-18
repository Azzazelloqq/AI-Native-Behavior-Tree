using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Serialization
{
    public sealed class CanonicalTreeJsonTests
    {
        [Test]
        public void ValidDocumentRoundTripsToStableCanonicalBytesAcrossCultures()
        {
            var document = CreateDocument();
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var first = CanonicalTreeJson.Serialize(document);
                Assert.That(first.Success, Is.True, Messages(first.Diagnostics));

                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                var read = CanonicalTreeJson.Parse(first.Utf8, "golden.aibt.json");
                var second = CanonicalTreeJson.Serialize(read.Document);

                Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
                Assert.That(second.Success, Is.True, Messages(second.Diagnostics));
                Assert.That(second.Utf8, Is.EqualTo(first.Utf8));
                Assert.That(second.SemanticHash, Is.EqualTo(first.SemanticHash));
                var text = Encoding.UTF8.GetString(first.Utf8);
                Assert.That(text, Does.EndWith("\n"));
                Assert.That(text, Does.Not.Contain("\r"));
                Assert.That(text, Does.Contain("\"speed\": 1.25"));
                Assert.That(text.IndexOf("\"alpha\"", StringComparison.Ordinal), Is.LessThan(text.IndexOf("\"世界\"", StringComparison.Ordinal)));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [TestCase("{\"format\":\"aibt.tree\",\"format\":\"aibt.tree\"}", "AIBT1003")]
        [TestCase("{\"format\":\"aibt.tree\",}", "AIBT1002")]
        [TestCase("{/*comment*/\"format\":\"aibt.tree\"}", "AIBT1002")]
        [TestCase("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"t\",\"name\":\"T\",\"root\":\"n\",\"nodes\":{\"n\":{\"type\":\"x\",\"typeVersion\":1,\"future\":true}}}", "AIBT1004")]
        [TestCase("{\"format\":\"aibt.tree\",\"formatVersion\":3,\"treeId\":\"t\",\"name\":\"T\",\"root\":\"n\",\"nodes\":{\"n\":{\"type\":\"x\",\"typeVersion\":1}}}", "AIBT1005")]
        [TestCase("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"t\",\"name\":\"\\uD800\",\"root\":\"n\",\"nodes\":{\"n\":{\"type\":\"x\",\"typeVersion\":1}}}", "AIBT1006")]
        public void StrictReaderRejectsInvalidInputsWithStableCodes(string json, string code)
        {
            var result = CanonicalTreeJson.Parse(json, "invalid.aibt.json");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Document, Is.Null);
            Assert.That(result.SourceText, Is.EqualTo(json));
            Assert.That(result.Diagnostics.Single().Code.Value, Is.EqualTo(code));
        }

        [Test]
        public void EscapedBackslashBeforeUnicodeTextIsNotTreatedAsAUnicodeEscape()
        {
            const string json = "{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"t\",\"name\":\"\\\\uD800\",\"root\":\"n\",\"nodes\":{\"n\":{\"type\":\"x\",\"typeVersion\":1}}}";

            var result = CanonicalTreeJson.Parse(json);

            Assert.That(result.Success, Is.True, Messages(result.Diagnostics));
            Assert.That(result.Document.Name, Is.EqualTo("\\uD800"));
        }

        [Test]
        public void Utf8ReaderRejectsBomAndInvalidByteSequencesWhilePreservingBytes()
        {
            var bom = new byte[] { 0xef, 0xbb, 0xbf, (byte)'{' , (byte)'}' };
            var invalid = new byte[] { 0xc3, 0x28 };

            var bomResult = CanonicalTreeJson.Parse(bom);
            var invalidResult = CanonicalTreeJson.Parse(invalid);

            Assert.That(bomResult.Diagnostics[0].Code, Is.EqualTo(TreeJsonDiagnosticCodes.InvalidUtf8));
            Assert.That(bomResult.SourceUtf8, Is.EqualTo(bom));
            Assert.That(invalidResult.Diagnostics[0].Code, Is.EqualTo(TreeJsonDiagnosticCodes.InvalidUtf8));
            Assert.That(invalidResult.SourceUtf8, Is.EqualTo(invalid));
        }

        [TestCase("valid-minimal.aibt.json", true)]
        [TestCase("invalid-unknown-field.aibt.json", false)]
        public void SerializationFixturesHaveStableAcceptanceClassification(string fileName, bool expectedSuccess)
        {
            var path = EditorTestPackagePaths.Resolve(
                "Tests", "Fixtures", "Trees", "Serialization", fileName);
            var result = CanonicalTreeJson.Parse(File.ReadAllBytes(path), fileName);

            Assert.That(result.Success, Is.EqualTo(expectedSuccess), Messages(result.Diagnostics));
        }

        [Test]
        public void CanonicalNumbersNormalizeNegativeZeroAndExponentSpelling()
        {
            var parameters = new SemanticObject(new[]
            {
                new SemanticProperty("negativeZero", SemanticValue.FromNumber(-0d)),
                new SemanticProperty("exponent", SemanticValue.FromNumber(1e20)),
            });
            var document = CreateDocument(parameters: parameters);

            var result = CanonicalTreeJson.Serialize(document);
            var json = Encoding.UTF8.GetString(result.Utf8);

            Assert.That(json, Does.Contain("\"negativeZero\": 0"));
            Assert.That(json, Does.Contain("\"exponent\": 1e20"));
            Assert.That(json, Does.Not.Contain("E"));
            Assert.That(json, Does.Not.Contain("e+"));
        }

        [Test]
        public void SemanticHashExcludesCosmeticFieldsButIncludesBehavioralParameters()
        {
            var baseline = CreateDocument(displayName: "First", description: "A");
            var cosmetic = CreateDocument(displayName: "Second", description: "B");
            var changed = CreateDocument(parameters: new SemanticObject(new[]
            {
                new SemanticProperty("speed", SemanticValue.FromNumber(2.5)),
            }));

            var baselineResult = CanonicalTreeJson.Serialize(baseline);
            var cosmeticResult = CanonicalTreeJson.Serialize(cosmetic);
            var changedResult = CanonicalTreeJson.Serialize(changed);

            Assert.That(cosmeticResult.Utf8, Is.Not.EqualTo(baselineResult.Utf8));
            Assert.That(cosmeticResult.SemanticHash, Is.EqualTo(baselineResult.SemanticHash));
            Assert.That(changedResult.SemanticHash, Is.Not.EqualTo(baselineResult.SemanticHash));
        }

        [Test]
        public void WriterReportsSchemaLimitationForRegisteredTypes()
        {
            const string typeId = "game.target";
            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(typeId), 1, 8, 8, StableHash.Fnv1A64(typeId + ".equals"), StableHash.Fnv1A64(typeId + ".schema"));
            var blackboard = new[]
            {
                new BlackboardKeyDefinition("target", "target", BlackboardTypeReference.Registered(typeId, descriptor))
            };
            var document = CreateDocument(blackboard: blackboard);

            var result = CanonicalTreeJson.Serialize(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Code == TreeJsonDiagnosticCodes.MissingRegisteredSchema), Is.True);
        }

        [Test]
        public void EnumContractRoundTripsInExactCanonicalPropertyOrder()
        {
            var node = new NodeDocument(
                new NodeId("root"),
                "aibt.action.success",
                1,
                parameters: SemanticObject.Empty,
                tags: TagSet.Empty);
            var key = new BlackboardKeyDefinition(
                "state",
                "state",
                BlackboardTypeReference.Enum32("game.state"),
                defaultValue: BlackboardDefaultValue.Enum32("game.state", 2));
            var document = new TreeDocument(
                TreeDocument.CurrentFormat,
                TreeDocument.CurrentFormatVersion,
                new TreeId("tree.enum"),
                "Enum",
                new NodeId("root"),
                new[] { node },
                new[] { key },
                tags: TagSet.Empty,
                metadata: SemanticObject.Empty);

            var written = CanonicalTreeJson.Serialize(document);
            var expected = "{\n"
                + "  \"format\": \"aibt.tree\",\n"
                + "  \"formatVersion\": 1,\n"
                + "  \"treeId\": \"tree.enum\",\n"
                + "  \"name\": \"Enum\",\n"
                + "  \"root\": \"root\",\n"
                + "  \"blackboard\": {\n"
                + "    \"state\": {\n"
                + "      \"type\": \"Enum32\",\n"
                + "      \"enumContract\": \"game.state\",\n"
                + "      \"default\": {\n"
                + "        \"contract\": \"game.state\",\n"
                + "        \"value\": 2\n"
                + "      }\n"
                + "    }\n"
                + "  },\n"
                + "  \"nodes\": {\n"
                + "    \"root\": {\n"
                + "      \"type\": \"aibt.action.success\",\n"
                + "      \"typeVersion\": 1\n"
                + "    }\n"
                + "  }\n"
                + "}\n";

            Assert.That(written.Success, Is.True, Messages(written.Diagnostics));
            Assert.That(Encoding.UTF8.GetString(written.Utf8), Is.EqualTo(expected));
            var read = CanonicalTreeJson.Parse(written.Utf8, "enum.aibt.json");
            Assert.That(read.Success, Is.True, Messages(read.Diagnostics));
            Assert.That(read.Document.Blackboard.Single().Type.EnumContract, Is.EqualTo("game.state"));
            Assert.That(CanonicalTreeJson.Serialize(read.Document).Utf8, Is.EqualTo(written.Utf8));
        }

        [TestCase("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"t\",\"name\":\"T\",\"root\":\"n\",\"blackboard\":{\"state\":{\"type\":\"Enum32\"}},\"nodes\":{\"n\":{\"type\":\"x\",\"typeVersion\":1}}}")]
        [TestCase("{\"format\":\"aibt.tree\",\"formatVersion\":1,\"treeId\":\"t\",\"name\":\"T\",\"root\":\"n\",\"blackboard\":{\"state\":{\"type\":\"Bool\",\"enumContract\":\"game.state\"}},\"nodes\":{\"n\":{\"type\":\"x\",\"typeVersion\":1}}}")]
        public void EnumContractPresenceMatchesTheDeclaredType(string json)
        {
            var result = CanonicalTreeJson.Parse(json, "invalid-enum.aibt.json");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(TreeJsonDiagnosticCodes.SchemaViolation));
        }

        [Test]
        public void WriterRejectsModelStateThatCannotBeRepresentedByV1Schema()
        {
            var blackboard = new[]
            {
                new BlackboardKeyDefinition("speed", "Display speed", BlackboardTypeReference.BuiltIn(BlackboardValueType.Float32))
            };

            var result = CanonicalTreeJson.Serialize(CreateDocument(blackboard: blackboard));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Location.JsonPointer == "/blackboard/speed"), Is.True);
        }

        [Test]
        public void TagsAreWrittenInOrdinalUtf8ByteOrder()
        {
            var document = CreateDocument();
            document.SetTags(new TagSet(new[] { "\ue000", "\U00010000" }));

            var result = CanonicalTreeJson.Serialize(document);
            var json = Encoding.UTF8.GetString(result.Utf8);

            Assert.That(result.Success, Is.True, Messages(result.Diagnostics));
            Assert.That(json.IndexOf("\ue000", StringComparison.Ordinal),
                Is.LessThan(json.IndexOf("\U00010000", StringComparison.Ordinal)));
        }

        [Test]
        public void NullSemanticStringReturnsDiagnosticInsteadOfThrowing()
        {
            var document = CreateDocument(parameters: new SemanticObject(new[]
            {
                new SemanticProperty("invalid", SemanticValue.FromString(null)),
            }));

            TreeJsonWriteResult result = null;
            Assert.DoesNotThrow(() => result = CanonicalTreeJson.Serialize(document));
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(item => item.Location.JsonPointer == "/nodes/root/parameters/invalid"), Is.True);
        }

        private static TreeDocument CreateDocument(
            SemanticObject parameters = null,
            string displayName = "Move",
            string description = "Description",
            IEnumerable<BlackboardKeyDefinition> blackboard = null)
        {
            parameters = parameters ?? new SemanticObject(new[]
            {
                new SemanticProperty("世界", SemanticValue.FromString("Привет 🌍")),
                new SemanticProperty("speed", SemanticValue.FromNumber(1.25)),
                new SemanticProperty("alpha", SemanticValue.FromBoolean(true)),
            });
            blackboard = blackboard ?? new[]
            {
                new BlackboardKeyDefinition(
                    "speed", "speed", BlackboardTypeReference.BuiltIn(BlackboardValueType.Float32),
                    BlackboardScope.Agent, BlackboardDefaultValue.Float32(1.25f), "Movement speed")
            };
            var node = new NodeDocument(
                new NodeId("root"), "aibt.action.move", 1,
                parameters: parameters,
                observer: new NodeObserver("self", new[] { "speed" }),
                displayName: displayName,
                description: description,
                tags: new TagSet(new[] { "movement", "action" }));
            return new TreeDocument(
                TreeDocument.CurrentFormat, TreeDocument.CurrentFormatVersion,
                new TreeId("tree.sample"), "Sample", new NodeId("root"), new[] { node }, blackboard,
                description, new TagSet(new[] { "sample", "ai" }),
                new SemanticObject(new[] { new SemanticProperty("owner", SemanticValue.FromString("AIBT")) }));
        }

        private static string Messages(DiagnosticCollection diagnostics)
            => string.Join(" | ", diagnostics.Select(item => item.Code + ": " + item.Message));
    }
}
