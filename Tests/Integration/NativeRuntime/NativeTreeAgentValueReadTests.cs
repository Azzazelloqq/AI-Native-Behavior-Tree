using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using AIBT.Tests.CodeGen.GenerationP7039;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using GcAllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-004 (ADR-P11-004 D2/D4): <see cref="NativeTreeAgent.TryGetValue{T}"/> reads the current
    /// run's tree across runs, refusals and policies. Frames are driven through the real scheduler,
    /// as in <see cref="NativeTreeAgentTests"/>. The tree is <see cref="ExternalTreeBlackboardReadTests.TreeJson"/>:
    /// the reader succeeds once the "live-target" fact is above zero, then the writer sets
    /// "owned-target" (default -3) to 7 and the root succeeds.
    /// </summary>
    public sealed class NativeTreeAgentValueReadTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private readonly List<NativeTreeAgent> _agents = new List<NativeTreeAgent>();
        private ProductionTreeScheduler _scheduler;
        private ProductionTreeWorld _world;
        private int _baselinePrograms;

        [SetUp]
        public void SetUp()
        {
            _baselinePrograms = NativeTreeProgramCache.LiveProgramCount;
            var worldObject = Track(new GameObject("AIBT.Tests.P11004.World"));
            _scheduler = worldObject.AddComponent<ProductionTreeScheduler>();
            _world = worldObject.AddComponent<ProductionTreeWorld>();
            InvokePrivate(_world, "Awake");
            InvokePrivate(_world, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var agent in _agents) if (agent != null) agent.Deactivate();
            _agents.Clear();
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null) UnityEngine.Object.DestroyImmediate(_created[index]);
            _created.Clear();
            Assert.That(NativeTreeProgramCache.LiveProgramCount, Is.EqualTo(_baselinePrograms), "A test leaked a shared program.");
        }

        [Test]
        public void FactAndNodeWrittenKeys_ReadBack_AndAFinishedRunKeepsItsValuesWithRestartNever()
        {
            var agent = Spawn(NativeTreeRestartMode.Never);
            Assert.That(agent.TryGetValue("owned-target", out int owned) && owned == -3, Is.True, "Defaults before the first update.");
            agent.SetFact("live-target", 4f);
            Assert.That(agent.TryGetValue("live-target", out float pending) && pending == 0f, Is.True,
                "A fact is written at the next scheduler boundary; until then the tree still holds the default.");

            Frames(1);

            Assert.That(agent.LastResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(agent.IsRunning, Is.False);
            Assert.That(agent.TryGetValue("live-target", out float fact) && fact == 4f, Is.True);
            Assert.That(agent.TryGetValue("owned-target", out owned) && owned == 7, Is.True, "The finished run's final value.");
            Assert.That(agent.TryGetValue("flag", out bool flag) && flag, Is.True, "A key nobody writes.");
            Assert.That(agent.TryGetVector3("spot", out var spot), Is.True);
            Assert.That(spot, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void WithRestartAlways_FinalValuesAreReadInCompleted_AndEachNewRunStartsFromDefaults()
        {
            var agent = Spawn(NativeTreeRestartMode.Always);
            agent.SetFact("live-target", 1f);
            var inCompleted = new List<int>();
            agent.Completed += _ =>
            {
                Assert.That(agent.TryGetValue("owned-target", out int value), Is.True);
                inCompleted.Add(value);
            };

            for (var run = 1; run <= 3; run++)
            {
                Frames(1);
                Assert.That(agent.CompletedRuns, Is.EqualTo(run));
                Assert.That(agent.TryGetValue("owned-target", out int fresh) && fresh == -3, Is.True,
                    "The next run already started (a new host) and reads the default.");
                Assert.That(agent.TryGetValue("live-target", out float fact) && fact == 0f, Is.True,
                    "A fact key reads its default until the next scheduler frame flushes it (ADR-P11-004 Q1).");
            }
            Assert.That(inCompleted, Is.EqualTo(new[] { 7, 7, 7 }));
        }

        [Test]
        public void NoRun_ReturnsFalseWithoutLogging()
        {
            var agent = Spawn(NativeTreeRestartMode.Never);
            agent.Stop();
            Assert.That(agent.TryGetValue("owned-target", out int value), Is.False);
            Assert.That(value, Is.EqualTo(0));
            agent.Restart();
            Assert.That(agent.TryGetValue("owned-target", out value) && value == -3, Is.True);

            agent.Deactivate();
            Assert.That(agent.TryGetValue<int>("owned-target", out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AFaultedAgent_ReturnsFalse()
        {
            var agent = Spawn(NativeTreeRestartMode.Always, Forced(SchedulingPolicy.BatchedJobsSameFrame, false));
            LogAssert.Expect(LogType.Error, new Regex("Forced scheduling policy is not supported"));
            LogAssert.Expect(LogType.Error, new Regex("SetJobsCapabilities"));
            Frames(1);
            Assert.That(agent.LastError, Is.Not.Null);
            Assert.That(agent.TryGetValue<int>("owned-target", out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WrongKeyOrType_IsLoggedOncePerKeyAndType_InAnyCallOrder()
        {
            var agent = Spawn(NativeTreeRestartMode.Never);
            LogAssert.Expect(LogType.Error, new Regex("value 'owned-target' \\(Single\\) cannot be read: BlackboardTypeMismatch"));
            LogAssert.Expect(LogType.Error, new Regex("value 'missing' \\(Int32\\) cannot be read: BlackboardUndeclaredAccess"));
            for (var call = 0; call < 3; call++)
            {
                Assert.That(agent.TryGetValue("owned-target", out float _), Is.False, "The wrong type first...");
                Assert.That(agent.TryGetValue("owned-target", out int owned) && owned == -3, Is.True, "...does not block the right one.");
                Assert.That(agent.TryGetValue("missing", out int _), Is.False);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ArgumentAndUnsupportedType_Throw()
        {
            var agent = Spawn(NativeTreeRestartMode.Never);
            Assert.Throws<ArgumentException>(() => agent.TryGetValue(string.Empty, out int _));
            Assert.Throws<NotSupportedException>(() => agent.TryGetValue("owned-target", out uint _));
        }

        [Test]
        public void PipelinedHostBetweenFrames_IsRefused_WithOneWarningPerAgent()
        {
            _scheduler.SetJobsCapabilities(JobsCapabilities(pipelined: true));
            var agent = Spawn(NativeTreeRestartMode.Always, Forced(SchedulingPolicy.PipelinedJobs, true));
            agent.SetFact("live-target", 1f);
            var executingFrames = 0;
            LogAssert.Expect(LogType.Warning, new Regex("refused because the host is executing.*PipelinedJobs"));
            for (var frame = 0; frame < 6; frame++)
            {
                Frames(1);
                var host = agent.Host;
                Assert.That(host, Is.Not.Null);
                if (!host.IsExecuting) continue;
                executingFrames++;
                Assert.That(host.CanReadTreeValues, Is.False);
                Assert.That(host.TryResolveExternalTreeRead<int>("owned-target",
                    NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32), out var handle, out var failure), Is.True);
                Assert.That(host.TryReadExternalTreeValue(handle, out _, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLifetimeStateInvalid));
                Assert.That(agent.TryGetValue("owned-target", out int _), Is.False);
                Assert.That(agent.TryGetValue("live-target", out float _), Is.False);
            }
            Assert.That(executingFrames, Is.GreaterThan(0), "A PipelinedJobs host holds its drive lock between frames.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TryGetValue_RepeatedAfterWarmup_AllocatesNoManagedMemory()
        {
            var agent = Spawn(NativeTreeRestartMode.Never);
            agent.SetFact("live-target", 1f);
            Frames(1);
            var sum = 0;
            TestDelegate reads = () =>
            {
                for (var index = 0; index < 1000; index++)
                {
                    agent.TryGetValue("owned-target", out int owned);
                    agent.TryGetVector3("spot", out var spot);
                    sum += owned + (int)spot.z;
                }
            };
            // Warm the measured delegate itself: its first call pays one-time runtime costs
            // (the first-ever reads of each key also create the agent's read slots).
            reads();
            Assert.That(sum, Is.EqualTo(10000));
            var before = GC.GetAllocatedBytesForCurrentThread();
            reads();
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero, "2000 warmed agent reads, bytes allocated.");
            Assert.That(reads, GcAllocIs.Not.AllocatingGCMemory(), "A warmed agent read must not allocate managed memory.");
            Assert.That(sum, Is.EqualTo(30000));
        }

        // ------------------------------------------------------------------ helpers

        private NativeTreeAgent Spawn(NativeTreeRestartMode restart, SchedulingProfile profile = null)
        {
            var gameObject = Track(new GameObject("AIBT.Tests.P11004.Agent" + _agents.Count));
            var agent = gameObject.AddComponent<NativeTreeAgent>();
            agent.Tree = Prepare();
            agent.World = _world;
            agent.RestartMode = restart;
            if (profile != null) agent.Profile = profile;
            _agents.Add(agent);
            agent.Activate();
            return agent;
        }

        private ProductionTreeAsset Prepare()
        {
            var artifact = ExternalTreeBlackboardReadTests.MaterializeShard();
            var compiled = ExternalTreeBlackboardReadTests.CompileFixture(artifact);
            var asset = ProductionTreeAsset.Create(compiled.CreateNativeBlackboardBindingV2(artifact.RegisteredTypes),
                ExternalTreeWriteFixtureCatalog.Fingerprint);
            asset.RecordPreparation(string.Empty, string.Empty,
                typeof(ExternalTreeWriteFixtureCatalog).FullName + ", " + typeof(ExternalTreeWriteFixtureCatalog).Assembly.GetName().Name);
            return Track(asset);
        }

        private static SchedulingProfile Forced(SchedulingPolicy policy, bool pipelining)
        {
            Assert.That(SchedulingProfile.TryCreate("tests.p11004." + policy, 0, 1u, null, null, pipelining, policy,
                out var profile, out var error), Is.True, error.ToString());
            return profile;
        }

        private static SchedulerJobsCapabilities JobsCapabilities(bool pipelined)
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(1.0, 1.0, 1u, 64u, 64u, pipelined, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private void Frames(int count)
        {
            for (var frame = 0; frame < count; frame++)
            {
                InvokePrivate(_scheduler, "Update");
                foreach (var agent in _agents) if (agent != null) agent.ProcessRootResult();
            }
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static void InvokePrivate(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
