using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Burst;
using AIBT.Tests.CodeGen.GenerationP11009;
using AIBT.Tests.CodeGen.GenerationP11014;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBT.Tests.Integration.NativeRuntime
{
    // P11-014 item 7: one session per callback (both fixture nodes have one Read binding).
    [AibtCatalogSet("aibt.tests.p11014.session-limit-set", 1u, typeof(SessionLimitFixtureShard))]
    public static partial class SessionLimitCatalog { }

    // The same nodes next to MixedScopeFixtureShard, whose mixed-report node has four bindings: four sessions per callback.
    [AibtCatalogSet("aibt.tests.p11014.session-limit-roomy-set", 1u, typeof(SessionLimitFixtureShard), typeof(MixedScopeFixtureShard))]
    public static partial class SessionLimitRoomyCatalog { }

    /// <summary>
    /// P11-014 item 7 (the value-session limit) and confirmation U6. A generated catalog sizes one
    /// callback's value sessions for its largest node; a node that reads one Read binding twice in one
    /// callback is refused with CapacityExceeded and the host stops with an unchanged failure code. The
    /// host's message now names the node and the rule. A catalog with a larger node has room for the
    /// second read and hides the fault.
    /// </summary>
    public sealed partial class MixedScopeNativeTreeTests
    {
        private static string SessionTree(string nodeType, string treeId)
            => @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": """ + treeId + @""", ""name"": ""P11-014 session limit"",
  ""root"": ""root"",
  ""blackboard"": { ""value"": { ""type"": ""Int32"", ""typeVersion"": 1, ""scope"": ""tree"", ""default"": 5 } },
  ""nodes"": {
    ""root"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""reader""] },
    ""reader"": { ""type"": """ + nodeType + @""", ""typeVersion"": 1, ""bindings"": { ""value"": ""value"" } }
  }
}";

        [Test]
        public void ValueSessionLimit_ANodeThatReadsOneBindingTwice_StopsTheHost_AndTheMessageNamesTheNodeAndTheRule()
        {
            var compiled = CompileSession(SessionTree("aibt.tests.read-twice", "tests.p11014.read-twice"), roomy: false);
            var host = StartSessionHost(compiled, roomy: false);
            var errors = CaptureErrors(() => host.DriveOneUpdate());

            var code = host.LastFailure.Code;
            Debug.Log("P11-014 item 7: read-twice under a one-session catalog stops the host with " + code
                + "; message: " + string.Join(" | ", errors));
            Assert.That(code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid), "the failure code is unchanged");
            var message = errors.SingleOrDefault(text => text.Contains("AIBT ProductionTreeHost: "));
            Assert.That(message, Is.Not.Null, string.Join("\n", errors));
            Assert.That(message, Does.Contain("Callback rejected: CapacityExceeded by node 'reader'"), message);
            Assert.That(message, Does.Contain("value-session limit").And.Contain("Read a value once into a local"), message);
        }

        [Test]
        public void ValueSessionLimit_ACatalogWithALargerNode_HidesTheFault()
        {
            var compiled = CompileSession(SessionTree("aibt.tests.read-twice", "tests.p11014.read-twice-roomy"), roomy: true);
            var host = StartSessionHost(compiled, roomy: true);

            host.DriveOneUpdate();

            Assert.That(host.LastFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success), "both reads succeed and agree");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// U6: two agents on one catalog under forced BatchedJobsSameFrame, one with the over-reading node.
        /// Records whether the fault stops only that agent or both.
        /// </summary>
        [Test]
        public void U6_TwoAgentsOnOneCatalog_UnderBatchedJobs_OnlyTheOverReadingAgentStops()
        {
            var profile = ProfileFor("forced-batched-jobs");
            var twice = Spawn(PrepareSessionAsset(SessionTree("aibt.tests.read-twice", "tests.p11014.u6-twice")), profile);
            var once = Spawn(PrepareSessionAsset(SessionTree("aibt.tests.read-once", "tests.p11014.u6-once")), profile);
            var errors = CaptureErrors(() => { for (var frame = 0; frame < 12; frame++) Frame(); });

            Debug.Log("P11-014 U6: read-twice agent LastError=" + (twice.LastError ?? "null") + ", host " + (twice.Host == null ? "null" : "kept")
                + ", runs " + twice.CompletedRuns + "; read-once agent LastError=" + (once.LastError ?? "null") + ", host "
                + (once.Host == null ? "null" : "kept") + ", runs " + once.CompletedRuns + "; world failure " + _world.LastWorldFailure.Code
                + "; errors: " + string.Join(" | ", errors));
            Assert.That(twice.LastError, Is.Not.Null.And.Contain("NativeLifetimeStateInvalid"), "the over-reading agent stops");
            Assert.That(once.LastError, Is.Null, "the neighbour on the same catalog keeps running");
            Assert.That(once.CompletedRuns, Is.GreaterThanOrEqualTo(10));
            Assert.That(_world.LastWorldFailure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.None));
        }

        // ------------------------------------------------------------------ helpers (P11-014 item 7)

        private static GeneratedShardMetadataArtifact SessionArtifact() => NativeLibraryKit.Materialize(typeof(SessionLimitFixtureShard));

        private static GeneratedCompiledProgramV2 CompileSession(string json, bool roomy)
        {
            var parsed = CanonicalTreeJson.Parse(json, "Tests/P11014/session.aibt.json");
            Assert.That(parsed.Success, Is.True, Describe(parsed.Diagnostics));
            var nodes = roomy ? SessionArtifact().Nodes.Concat(Artifact().Nodes).ToArray() : SessionArtifact().Nodes.ToArray();
            var result = GeneratedCompiledProgramV2Compiler.Compile(parsed.Document, nodes, SessionArtifact().RegisteredTypes,
                new ReferenceCompilerOptions("Tests/P11014/session.aibt.json", ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 6)));
            Assert.That(result.Success, Is.True, Describe(result.Diagnostics));
            return result.Program;
        }

        private ProductionTreeHost StartSessionHost(GeneratedCompiledProgramV2 compiled, bool roomy)
        {
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(SessionArtifact().RegisteredTypes),
                roomy ? SessionLimitRoomyCatalog.Fingerprint : SessionLimitCatalog.Fingerprint));
            ProductionTreeProgram program;
            BurstContextResult failure;
            var created = roomy
                ? asset.TryCreateProgram(SessionLimitRoomyCatalog.TryCreateRuntimeCatalog, out program, out failure)
                : asset.TryCreateProgram(SessionLimitCatalog.TryCreateRuntimeCatalog, out program, out failure);
            Assert.That(created, Is.True, failure.ToString());
            _programs.Add(program);
            var gameObject = Track(new GameObject("AIBT.Tests.P11014.Host"));
            var host = gameObject.AddComponent<ProductionTreeHost>();
            Assert.That(program.TryBootstrap(host, ProductionTreeHost.DefaultRandomSeed, out var bootstrapFailure, 0, () => 0L),
                Is.True, bootstrapFailure.Code.ToString());
            return host;
        }

        private ProductionTreeAsset PrepareSessionAsset(string json)
        {
            var compiled = CompileSession(json, roomy: false);
            var asset = Track(ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(SessionArtifact().RegisteredTypes),
                SessionLimitCatalog.Fingerprint));
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(SessionLimitCatalog).FullName + ", " + typeof(SessionLimitCatalog).Assembly.GetName().Name);
            return asset;
        }

        /// <summary>Runs <paramref name="action"/> and returns the errors it logged (they do not fail the test by themselves).</summary>
        private static List<string> CaptureErrors(TestDelegate action)
        {
            var errors = new List<string>();
            Application.LogCallback handler = (text, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception) errors.Add(text);
            };
            LogAssert.ignoreFailingMessages = true; // per test: the log scope resets it for the next test
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return errors;
        }
    }
}
