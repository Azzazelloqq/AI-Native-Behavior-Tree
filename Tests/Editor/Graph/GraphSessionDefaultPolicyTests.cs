using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Graph
{
    /// <summary>
    /// P11-015 review follow-up (ADR-P9-001, second amendment): the Graph window's edit session checks a tree
    /// with the default its tree list uses. A native tree with an Agent-scope key was "Valid" in the list while
    /// the session (<see cref="BehaviorTreeGraphWindow.CompilerOptions"/>, then <c>Phase1</c>) showed AIBT2030.
    /// </summary>
    public sealed class GraphSessionDefaultPolicyTests
    {
        private const string NativeAgentScopeTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tree.graph-agent"", ""name"": ""Graph Agent"", ""root"": ""root"",
  ""blackboardContracts"": { ""agent"": { ""contractId"": ""contract.agent"", ""contractVersion"": 1 } },
  ""blackboard"": { ""danger"": { ""type"": ""Bool"", ""typeVersion"": 1, ""scope"": ""agent"", ""default"": false } },
  ""nodes"": { ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1 } }
}";

        private string _path;

        [SetUp]
        public void SetUp() => _path = Path.Combine(Path.GetTempPath(), "aibt-graph-default-" + System.Guid.NewGuid().ToString("N") + ".aibt.json");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Test]
        public void ANativeAgentScopeTree_TheListCallsValid_HasNoAIBT2030InTheEditSession()
        {
            File.WriteAllText(_path, NativeAgentScopeTree);
            var catalog = ProjectNodeCatalogCache.Current;
            var listed = TreeCatalogEntry.Read(_path, catalog.Registry);
            Assert.That(listed.Status, Is.EqualTo("Valid"), listed.Details);

            var document = CanonicalTreeJson.Parse(NativeAgentScopeTree, "trees/editor.aibt.json").Document;
            var session = catalog.ValidateAuthoring(document, BehaviorTreeGraphWindow.CompilerOptions());

            Assert.That(session.Select(d => d.Code.Value), Does.Not.Contain("AIBT2030"),
                string.Join("; ", session.Select(d => d.Code.Value + " " + d.Message)));
            Assert.That(session.Any(d => d.Severity == DiagnosticSeverity.Error), Is.False,
                string.Join("; ", session.Select(d => d.Code.Value + " " + d.Message)));
        }
    }
}
