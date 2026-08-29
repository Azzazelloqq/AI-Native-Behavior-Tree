using System.IO;
using AIBT.Authoring.BehaviorCases;
using AIBT.Authoring.Benchmarking;
using AIBT.Mcp;
using AIBT.Runtime.Scheduling;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Collections;

namespace AIBT.Tests.Editor.Mcp.Testing
{
    /// <summary>
    /// P6-008's real end-to-end proof at the same dispatcher entry point
    /// <see cref="AIBT.Mcp.McpBridgeListener"/> calls for every real request -- mirrors
    /// Tests/Editor/Mcp/Verification/McpVerificationToolDispatcherTests.cs's fixture shape.
    /// run-tests reuses the promoted <see cref="AuthoringBehaviorCaseExecutorFactory"/>'s own
    /// registries (<see cref="ReferencePreviewFixtureEnvironment"/>) -- the same Phase 1 fixture/
    /// built-in set aibt_simulate uses -- so the fixture tree/case here is the same shape as
    /// Tests/Editor/Preview/Fixtures/success-then-running.aibt.json (P3-009's own proven fixture).
    /// run-benchmark drives the promoted <see cref="SchedulingPolicyDriver"/> against a real
    /// P4-001-approved scenario from <see cref="SchedulingScenarios"/>.
    /// </summary>
    public sealed class McpTestingToolDispatcherTests
    {
        private const string SuccessThenRunningTree = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tree.mcp-testing.success-then-running"",
  ""name"": ""Success Then Running"", ""root"": ""root"",
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [ ""a"", ""b"" ] },
    ""a"": { ""type"": ""aibt.test.success"", ""typeVersion"": 1 },
    ""b"": { ""type"": ""aibt.test.running"", ""typeVersion"": 1 }
  }
}";

        private const string PassingCase = @"{
  ""format"": ""aibt.case"", ""formatVersion"": 1, ""name"": ""passing"",
  ""tree"": ""tree.aibt.json"", ""treeInstanceId"": 1, ""rootSeed"": 0,
  ""steps"": [ { ""operation"": ""update"", ""updateId"": 1, ""snapshotRevision"": 1, ""timeMicroseconds"": 0,
    ""expect"": { ""progress"": ""waiting"" } } ]
}";

        private const string KnownFailingCase = @"{
  ""format"": ""aibt.case"", ""formatVersion"": 1, ""name"": ""known failing"",
  ""tree"": ""tree.aibt.json"", ""treeInstanceId"": 1, ""rootSeed"": 0,
  ""steps"": [ { ""operation"": ""update"", ""updateId"": 1, ""snapshotRevision"": 1, ""timeMicroseconds"": 0,
    ""expect"": { ""progress"": ""completed"" } } ]
}";

        private string _projectRoot;
        private string _assetsDir;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-testing-" + System.Guid.NewGuid().ToString("N"));
            _assetsDir = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(_assetsDir);
            File.WriteAllText(Path.Combine(_assetsDir, "tree.aibt.json"), SuccessThenRunningTree);
        }

        [TearDown]
        public void RemoveTempProject()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, recursive: true);
            }
        }

        // ---- run-tests -------------------------------------------------------------------------

        [Test]
        public void RunTestsReturnsTheSameResultAsADirectBehaviorCaseRunnerCall()
        {
            File.WriteAllText(Path.Combine(_assetsDir, "passing.aibtcase.json"), PassingCase);
            var response = Dispatch("run_tests", new JObject { ["casePath"] = "passing.aibtcase.json" }, "TestExecution");

            Assert.That((bool)response["result"]["success"], Is.True, response.ToString());
            Assert.That(((JArray)response["result"]["failures"]).Count, Is.EqualTo(0));

            var directFactory = new AuthoringBehaviorCaseExecutorFactory(_assetsDir);
            var direct = BehaviorCaseRunner.Run(
                System.Text.Encoding.UTF8.GetBytes(PassingCase), "passing.aibtcase.json", directFactory, BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(direct.Success, Is.True);
            Assert.That((int)response["result"]["executedStepCount"], Is.EqualTo(direct.ExecutedStepCount));
        }

        [Test]
        public void RunTestsOnAKnownFailingCaseReturnsTheSameFailureTheDirectRunnerReports()
        {
            File.WriteAllText(Path.Combine(_assetsDir, "known-failing.aibtcase.json"), KnownFailingCase);
            var response = Dispatch("run_tests", new JObject { ["casePath"] = "known-failing.aibtcase.json" }, "TestExecution");

            Assert.That((bool)response["result"]["success"], Is.False, response.ToString());
            var failures = (JArray)response["result"]["failures"];
            Assert.That(failures.Count, Is.EqualTo(1));

            var directFactory = new AuthoringBehaviorCaseExecutorFactory(_assetsDir);
            var direct = BehaviorCaseRunner.Run(
                System.Text.Encoding.UTF8.GetBytes(KnownFailingCase), "known-failing.aibtcase.json", directFactory, BehaviorCaseRegisteredValueRegistry.Empty);

            Assert.That(direct.Success, Is.False);
            Assert.That((string)failures[0]["kind"], Is.EqualTo(direct.Failures[0].Kind.ToString()));
        }

        [Test]
        public void RunTestsOnAMissingCaseReportsCaseNotFound()
        {
            var response = Dispatch("run_tests", new JObject { ["casePath"] = "does-not-exist.aibtcase.json" }, "TestExecution");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9025"));
        }

        // ---- run-benchmark ----------------------------------------------------------------------

        [Test]
        public void RunBenchmarkReturnsTheSameStepCountAsADirectSchedulingPolicyDriverCall()
        {
            var response = Dispatch("run_benchmark", new JObject
            {
                ["scenario"] = "scheduling-baseline-empty-job",
                ["agentCount"] = 4,
                ["policy"] = "immediate",
            }, "BenchmarkExecution");

            Assert.That(response["error"], Is.Null, response.ToString());
            var toolSteps = (ulong)response["result"]["totalSteps"];
            Assert.That(toolSteps, Is.GreaterThan(0UL));
            Assert.That((string)response["result"]["policy"], Is.EqualTo("immediate"));
            Assert.That((int)response["result"]["agentCount"], Is.EqualTo(4));

            SchedulingScenarios.ScenarioDefinition? found = null;
            foreach (var definition in SchedulingScenarios.Catalog)
                if (definition.Name == "scheduling-baseline-empty-job") found = definition;
            var compiled = found.Value.Build();
            Assert.That(SchedulingPolicyDriver.TryCreateAgents(compiled.Program, compiled.NodeKinds, 4, Allocator.Persistent, out var agents, out var createFailure), Is.True, createFailure.Code.ToString());
            try
            {
                Assert.That(SchedulingPolicyDriver.TryRunImmediate(agents, 1, compiled.LeafStatusByRuntimeIndex, out var directSteps, out var runFailure), Is.True, runFailure.Code.ToString());
                Assert.That(toolSteps, Is.EqualTo(directSteps));
            }
            finally
            {
                foreach (var agent in agents) agent.Dispose();
            }
        }

        [Test]
        public void RunBenchmarkOnAPlaceholderScenarioIsRefusedNeverSubstituted()
        {
            var response = Dispatch("run_benchmark", new JObject
            {
                ["scenario"] = "event-driven-sleeping-wakeup",
                ["agentCount"] = 1,
                ["policy"] = "immediate",
            }, "BenchmarkExecution");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9028"));
        }

        [Test]
        public void RunBenchmarkOnAnUnknownScenarioIsRefused()
        {
            var response = Dispatch("run_benchmark", new JObject
            {
                ["scenario"] = "totally-made-up-scenario",
                ["agentCount"] = 1,
                ["policy"] = "immediate",
            }, "BenchmarkExecution");

            Assert.That(response["error"], Is.Not.Null);
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9027"));
        }

        // ---- permission negative matrix --------------------------------------------------------

        [TestCase("run_tests", "Read")]
        [TestCase("run_benchmark", "Read")]
        public void EachTestingToolIsRejectedWithoutItsDeclaredPermission(string tool, string wrongCategory)
        {
            var response = Dispatch(tool, new JObject
            {
                ["casePath"] = "passing.aibtcase.json",
                ["scenario"] = "scheduling-baseline-empty-job",
                ["agentCount"] = 1,
                ["policy"] = "immediate",
            }, wrongCategory);

            Assert.That(response["error"], Is.Not.Null, "Tool '" + tool + "' must reject a call granted only '" + wrongCategory + "'.");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9012"));
        }

        private JObject Dispatch(string tool, JObject args, string grantedCategory)
        {
            var request = new JObject
            {
                ["tool"] = tool,
                ["args"] = args,
                ["grantedCategories"] = grantedCategory == null ? new JArray() : new JArray(grantedCategory),
            };
            var responseLine = McpToolDispatcher.Dispatch(request.ToString(Newtonsoft.Json.Formatting.None), _assetsDir);
            return JObject.Parse(responseLine);
        }
    }
}
