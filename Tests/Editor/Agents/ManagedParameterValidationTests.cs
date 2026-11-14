using System.Linq;
using AIBT.Agents;
using AIBT.Authoring;
using NUnit.Framework;
using static AIBT.Tests.Editor.Agents.ManagedTestTrees;

namespace AIBT.Tests.Editor.Agents
{
    /// <summary>
    /// P9-001: <see cref="TreeValidator"/> rules for the managed-only parameter types
    /// <c>int32</c>, <c>string</c> and <c>blackboard-key</c>.
    /// </summary>
    public sealed class ManagedParameterValidationTests
    {
        private const string Score = "\"score\": { \"type\": \"Int32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 0 }";
        private const string ValidKey = "\"key\": \"score\"";

        private ManagedNodeSet _nodes;

        [SetUp]
        public void SetUp()
        {
            _nodes = ManagedNodeSet.Create(new[] { typeof(ProbeNode) });
            Assert.That(_nodes.Problems, Is.Empty, string.Join("\n", _nodes.Problems));
        }

        [TestCase("0")]
        [TestCase("-5")]
        [TestCase("2147483647")]
        [TestCase("-2147483648")]
        public void AnInt32ParameterAcceptsEveryIntegerInRange(string value)
        {
            Assert.That(Errors(ValidKey + ", \"count\": " + value), Is.Empty);
        }

        [TestCase("2147483648")]
        [TestCase("-2147483649")]
        [TestCase("1.5")]
        [TestCase("\"3\"")]
        [TestCase("true")]
        public void AnInt32ParameterRejectsValuesOutOfRangeOrOfAnotherType(string value)
        {
            AssertSingleError(ValidKey + ", \"count\": " + value, "count", TreeValidationDiagnosticCodes.ParameterType);
        }

        [TestCase("\"\"")]
        [TestCase("\"any text\"")]
        public void AStringParameterAcceptsAnyText(string value)
        {
            Assert.That(Errors(ValidKey + ", \"label\": " + value), Is.Empty);
        }

        [TestCase("3")]
        [TestCase("false")]
        public void AStringParameterRejectsNonText(string value)
        {
            AssertSingleError(ValidKey + ", \"label\": " + value, "label", TreeValidationDiagnosticCodes.ParameterType);
        }

        [Test]
        public void ABlackboardKeyParameterMustBeANonEmptyString()
        {
            AssertSingleError("\"key\": 3", "key", TreeValidationDiagnosticCodes.ParameterType);
            var empty = AssertSingleError("\"key\": \"\"", "key", TreeValidationDiagnosticCodes.ParameterConstraint);
            Assert.That(empty.Message, Does.Contain("must name a blackboard key"));
        }

        [Test]
        public void ABlackboardKeyParameterMustNameADeclaredKeyOfTheExpectedType()
        {
            var undeclared = AssertSingleError("\"key\": \"health\"", "key", TreeValidationDiagnosticCodes.ParameterConstraint);
            Assert.That(undeclared.Message, Does.Contain("'health'").And.Contain("does not declare"));

            var wrongType = AssertSingleError(
                Validate(Tree("probe", "\"score\": { \"type\": \"Float32\", \"typeVersion\": 1, \"scope\": \"tree\", \"default\": 0.0 }",
                    Node("probe", "test.validation-probe", ValidKey))),
                "key", TreeValidationDiagnosticCodes.ParameterConstraint);
            Assert.That(wrongType.Message, Does.Contain("Float32").And.Contain("requires Int32"));
        }

        [Test]
        public void ABlackboardKeyParameterMustNameATreeScopeKey()
        {
            var json = TreeWithContracts("probe",
                "\"agent\": { \"contractId\": \"contract.agent\", \"contractVersion\": 1 }",
                "\"score\": { \"type\": \"Int32\", \"typeVersion\": 1, \"scope\": \"agent\", \"default\": 0 }",
                Node("probe", "test.validation-probe", ValidKey));
            var diagnostics = Validate(json);
            var keyErrors = OnParameter(diagnostics, "key");
            Assert.That(keyErrors.Select(d => d.Message), Has.Some.Contains("must name a Tree-scope key"),
                Describe(diagnostics));
        }

        [Test]
        public void AMissingRequiredBlackboardKeyIsReportedAndOptionalScalarsMayBeOmitted()
        {
            AssertSingleError(string.Empty, "key", TreeValidationDiagnosticCodes.MissingParameter);
            Assert.That(Errors(ValidKey), Is.Empty, "int32 and string parameters with field initializers are optional.");
        }

        private Diagnostic AssertSingleError(string parameters, string parameter, DiagnosticCode code)
            => AssertSingleError(Validate(Tree("probe", Score, Node("probe", "test.validation-probe", parameters))), parameter, code);

        private static Diagnostic AssertSingleError(DiagnosticCollection diagnostics, string parameter, DiagnosticCode code)
        {
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.That(errors.Length, Is.EqualTo(1), Describe(diagnostics));
            Assert.That(errors[0].Code.Value, Is.EqualTo(code.Value), Describe(diagnostics));
            Assert.That(errors[0].Location.JsonPointer, Is.EqualTo("/nodes/probe/parameters/" + parameter));
            return errors[0];
        }

        private Diagnostic[] Errors(string parameters)
            => Validate(Tree("probe", Score, Node("probe", "test.validation-probe", parameters)))
                .Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        private static Diagnostic[] OnParameter(DiagnosticCollection diagnostics, string parameter)
            => diagnostics.Where(d => d.Location.JsonPointer == "/nodes/probe/parameters/" + parameter).ToArray();

        private DiagnosticCollection Validate(string json)
        {
            var read = CanonicalTreeJson.Parse(json, documentId: "trees/probe.aibt");
            Assert.That(read.Document, Is.Not.Null, Describe(read.Diagnostics));
            var options = ManagedTreeProgram.CreateOptions("trees/probe.aibt");
            return TreeValidator.Validate(read.Document, _nodes.Registry, options.Policy.CreateValidationOptions(options.SourceId));
        }

        private static string Describe(DiagnosticCollection diagnostics)
            => string.Join("\n", diagnostics.Select(d => d.Code.Value + " " + d.Location.JsonPointer + ": " + d.Message));

        [BtNode("test.validation-probe")]
        private sealed class ProbeNode : BtAction
        {
            public BbKey<int> key;
            public int count = 1;
            public string label = "none";

            protected override NodeStatus OnTick() => NodeStatus.Success;
        }
    }
}
