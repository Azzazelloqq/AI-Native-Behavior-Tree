using AIBT.Authoring;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Discovery
{
    public sealed class ProjectPolicySnapshotTests
    {
        private const string ValidPolicyJson = @"{
  ""format"": ""aibt.policy"",
  ""formatVersion"": 1,
  ""maxTreeDepth"": 64,
  ""maxNodesPerTree"": 4096,
  ""allowManagedNodes"": true,
  ""allowMainThreadNodes"": true,
  ""requireTreeDescription"": true,
  ""requireNodeDescriptions"": true,
  ""blackboardNaming"": ""snake_case"",
  ""requireDeterministicNodes"": true,
  ""allowSideEffects"": true,
  ""unreachableNodes"": ""error"",
  ""supportsAgentScope"": false,
  ""supportsSharedScope"": false,
  ""forbiddenNodeTypes"": [],
  ""warningsAsErrors"": [""unreachable_node"", ""unbounded_repeater""],
  ""performance"": {
    ""forbidUnboundedRepeaters"": true,
    ""requireEventDrivenServices"": false
  }
}";

        [Test]
        public void ValidDocumentParsesEveryFieldExactly()
        {
            var parsed = ProjectPolicySnapshot.TryParse(ValidPolicyJson, out var snapshot, out var error);

            Assert.That(parsed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(snapshot.MaxTreeDepth, Is.EqualTo(64));
            Assert.That(snapshot.MaxNodesPerTree, Is.EqualTo(4096));
            Assert.That(snapshot.AllowManagedNodes, Is.True);
            Assert.That(snapshot.AllowMainThreadNodes, Is.True);
            Assert.That(snapshot.RequireTreeDescription, Is.True);
            Assert.That(snapshot.RequireNodeDescriptions, Is.True);
            Assert.That(snapshot.BlackboardNaming, Is.EqualTo("snake_case"));
            Assert.That(snapshot.RequireDeterministicNodes, Is.True);
            Assert.That(snapshot.AllowSideEffects, Is.True);
            Assert.That(snapshot.UnreachableNodes, Is.EqualTo("error"));
            Assert.That(snapshot.SupportsAgentScope, Is.False);
            Assert.That(snapshot.SupportsSharedScope, Is.False);
            Assert.That(snapshot.ForbiddenNodeTypes, Is.Empty);
            Assert.That(snapshot.WarningsAsErrors, Is.EquivalentTo(new[] { "unreachable_node", "unbounded_repeater" }));
            Assert.That(snapshot.ForbidUnboundedRepeaters, Is.True);
            Assert.That(snapshot.RequireEventDrivenServices, Is.False);
            Assert.That(snapshot.MaxEstimatedCost, Is.Null);
        }

        [Test]
        public void NotJsonProducesAStructuredDiagnosticNotAThrownException()
        {
            bool parsed = false;
            Diagnostic error = null;
            Assert.DoesNotThrow(() => parsed = ProjectPolicySnapshot.TryParse("{ not valid json", out _, out error));

            Assert.That(parsed, Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(error.Code.Value, Is.EqualTo("AIBT9008"));
            Assert.That(error.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        }

        [Test]
        public void MissingRequiredFieldProducesAStructuredDiagnostic()
        {
            const string missingFormat = @"{ ""allowManagedNodes"": true }";

            var parsed = ProjectPolicySnapshot.TryParse(missingFormat, out var snapshot, out var error);

            Assert.That(parsed, Is.False);
            Assert.That(snapshot, Is.Null);
            Assert.That(error, Is.Not.Null);
            Assert.That(error.Code.Value, Is.EqualTo("AIBT9008"));
        }

        [Test]
        public void WrongFormatFieldProducesAStructuredDiagnostic()
        {
            const string wrongFormat = @"{
  ""format"": ""something-else"",
  ""allowManagedNodes"": true,
  ""allowMainThreadNodes"": true,
  ""requireTreeDescription"": true,
  ""requireNodeDescriptions"": true,
  ""blackboardNaming"": ""snake_case"",
  ""requireDeterministicNodes"": true,
  ""allowSideEffects"": true,
  ""unreachableNodes"": ""error"",
  ""supportsAgentScope"": false,
  ""supportsSharedScope"": false,
  ""performance"": { ""forbidUnboundedRepeaters"": true, ""requireEventDrivenServices"": false }
}";

            var parsed = ProjectPolicySnapshot.TryParse(wrongFormat, out var snapshot, out var error);

            Assert.That(parsed, Is.False);
            Assert.That(snapshot, Is.Null);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void MissingFileProducesAStructuredDiagnosticNotAThrownException()
        {
            bool parsed = false;
            Diagnostic error = null;
            Assert.DoesNotThrow(() => parsed = ProjectPolicySnapshot.TryReadFile(
                "Z:\\definitely\\not\\a\\real\\path\\policy.json", out _, out error));

            Assert.That(parsed, Is.False);
            Assert.That(error, Is.Not.Null);
            Assert.That(error.Code.Value, Is.EqualTo("AIBT9008"));
        }
    }
}
