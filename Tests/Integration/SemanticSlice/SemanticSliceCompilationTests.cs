using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Tests.BehaviorCases;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Integration.SemanticSlice
{
    public sealed class SemanticSliceCompilationTests
    {
        private static readonly CompiledCompilerVersion CompilerVersion
            = new CompiledCompilerVersion(1, 0, 0, 1);

        [TestCase("patrol-react.aibt.json")]
        [TestCase("async-action.aibt.json")]
        [TestCase("enum-snapshot.aibt.json")]
        [TestCase("parallel-decorator.aibt.json")]
        public void CanonicalGoldenTree_ValidatesAndCompilesDeterministically(string fileName)
        {
            var path = GoldenTreePath(fileName);
            var source = File.ReadAllBytes(path);
            var registry = SemanticSliceNodeContracts.CreateAuthoringRegistry();

            var read = CanonicalTreeJson.Parse(source, fileName);
            var write = read.Success ? CanonicalTreeJson.Serialize(read.Document) : null;
            var validation = read.Success
                ? TreeValidator.Validate(read.Document, registry)
                : read.Diagnostics;
            var first = read.Success
                ? ReferenceCompiler.Compile(read.Document, registry, Options(fileName))
                : null;
            var second = read.Success
                ? ReferenceCompiler.Compile(read.Document, registry, Options(fileName))
                : null;

            Assert.That(read.Success, Is.True, Diagnostics(read.Diagnostics));
            Assert.That(write.Success, Is.True, Diagnostics(write.Diagnostics));
            Assert.That(write.Utf8, Is.EqualTo(source));
            Assert.That(validation, Is.Empty, Diagnostics(validation));
            Assert.That(first.Success, Is.True, Diagnostics(first.Diagnostics));
            Assert.That(second.Success, Is.True, Diagnostics(second.Diagnostics));
            Assert.That(second.Program.Header.CanonicalSemanticHash,
                Is.EqualTo(first.Program.Header.CanonicalSemanticHash));
            Assert.That(second.Program.Header.CompiledContentHash,
                Is.EqualTo(first.Program.Header.CompiledContentHash));
            CollectionAssert.AreEqual(first.Program.ConfigBlob, second.Program.ConfigBlob);
            CollectionAssert.AreEqual(first.Program.DefaultValueBlob, second.Program.DefaultValueBlob);
        }

        [Test]
        public void UnknownNodeFixture_FailsSemanticValidationAtStableLocation()
        {
            const string fileName = "invalid-unknown-node.aibt.json";
            var read = CanonicalTreeJson.Parse(File.ReadAllBytes(GoldenTreePath(fileName)), fileName);
            var diagnostics = TreeValidator.Validate(
                read.Document,
                SemanticSliceNodeContracts.CreateAuthoringRegistry());

            Assert.That(read.Success, Is.True, Diagnostics(read.Diagnostics));
            Assert.That(diagnostics.Any(item => item.Code == TreeValidationDiagnosticCodes.UnknownNodeType
                && item.Location.JsonPointer == "/nodes/root/type"), Is.True, Diagnostics(diagnostics));
        }

        [TestCase("patrol-react.aibtcase.json")]
        [TestCase("parallel-decorator.aibtcase.json")]
        [TestCase("async-completion.aibtcase.json")]
        [TestCase("async-budgeted-abort.aibtcase.json")]
        [TestCase("initial-blackboard.aibtcase.json")]
        public void GoldenBehaviorCase_RoundTripsExactCanonicalBytes(string fileName)
        {
            var source = File.ReadAllBytes(GoldenCasePath(fileName));
            var read = BehaviorCaseJson.Parse(source, fileName);
            var write = read.Success ? BehaviorCaseJson.Serialize(read.Document) : null;

            Assert.That(read.Success, Is.True, Diagnostics(read.Diagnostics));
            Assert.That(write.Success, Is.True, Diagnostics(write.Diagnostics));
            Assert.That(write.CopyUtf8(), Is.EqualTo(source));
        }

        private static ReferenceCompilerOptions Options(string fileName)
            => new ReferenceCompilerOptions(
                "golden/" + fileName,
                ReferenceCompilationPolicy.Phase1,
                CompilerVersion);

        private static string GoldenTreePath(string fileName)
            => SemanticSliceTestPackagePaths.Resolve("Tests", "Fixtures", "Golden", "Trees", fileName);

        private static string GoldenCasePath(string fileName)
            => SemanticSliceTestPackagePaths.Resolve("Tests", "Fixtures", "Golden", "Cases", fileName);

        private static string Diagnostics(DiagnosticCollection diagnostics)
            => string.Join(" | ", diagnostics.Select(item => item.Code + " "
                + item.Location.JsonPointer + ": " + item.Message));
    }
}
