using System.Collections.Generic;
using System.Reflection;
using AIBT.Burst;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Runtime.Scheduling.Production
{
    /// <summary>
    /// P7-033 steps 3-4: registration/ownership and deterministic due ordering/budget admission.
    /// Deterministic policy selection (Immediate/Budgeted/Jobs) and full explainability are later
    /// steps of the same card. Reuses
    /// <see cref="AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture"/> (made internal
    /// for this reuse) rather than re-deriving an already-proven minimal compiled-program shape.
    /// </summary>
    public sealed class ProductionTreeSchedulerTests
    {
        private GameObject _hostObject;
        private GameObject _hostObjectB;
        private GameObject _schedulerObject;
        private GameObject _schedulerObjectB;
        private readonly List<GameObject> _extraObjects = new List<GameObject>();

        [TearDown]
        public void Cleanup()
        {
            DestroyHost(ref _hostObject);
            DestroyHost(ref _hostObjectB);
            for (var index = 0; index < _extraObjects.Count; index++)
            {
                var obj = _extraObjects[index];
                if (obj == null) continue;
                var host = obj.GetComponent<ProductionTreeHost>();
                if (host != null) InvokePrivate(host, "OnDestroy");
                Object.DestroyImmediate(obj);
            }
            _extraObjects.Clear();
            if (_schedulerObject != null) { Object.DestroyImmediate(_schedulerObject); _schedulerObject = null; }
            if (_schedulerObjectB != null) { Object.DestroyImmediate(_schedulerObjectB); _schedulerObjectB = null; }
        }

        private void DestroyHost(ref GameObject hostObject)
        {
            if (hostObject == null) return;
            var host = hostObject.GetComponent<ProductionTreeHost>();
            if (host != null) InvokePrivate(host, "OnDestroy");
            Object.DestroyImmediate(hostObject);
            hostObject = null;
        }

        [Test]
        public void TryRegister_NoProfile_UsesNormalAndOwnsTheHost()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();

            Assert.That(scheduler.TryRegister(host, out var error), Is.True, error.ToString());
            Assert.That(scheduler.IsRegistered(host), Is.True);
            Assert.That(scheduler.RegisteredCount, Is.EqualTo(1));
            Assert.That(host.IsCoordinatorOwned, Is.True);
            Assert.That(scheduler.TryGetProfile(host, out var profile), Is.True);
            Assert.That(profile, Is.SameAs(SchedulingProfile.Normal));
        }

        [Test]
        public void TryRegister_ExplicitProfile_IsStoredExactly()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(SchedulingProfile.TryCreate("game.ai.boss", 5, 1u, null, null, false, null, out var custom, out _), Is.True);

            Assert.That(scheduler.TryRegister(host, custom, out var error), Is.True, error.ToString());
            Assert.That(scheduler.TryGetProfile(host, out var stored), Is.True);
            Assert.That(stored, Is.SameAs(custom));
        }

        [Test]
        public void TryRegister_Duplicate_OnSameScheduler_FailsWithoutReplacing()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            Assert.That(scheduler.TryRegister(host, out var error), Is.False);
            Assert.That(error, Is.EqualTo(SchedulerRegistrationError.AlreadyRegisteredWithThisScheduler));
            Assert.That(scheduler.RegisteredCount, Is.EqualTo(1));
        }

        [Test]
        public void TryRegister_OnASecondScheduler_FailsWithoutStealingOwnership()
        {
            var schedulerA = CreateScheduler();
            var schedulerB = CreateSecondScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(schedulerA.TryRegister(host, out _), Is.True);

            Assert.That(schedulerB.TryRegister(host, out var error), Is.False);
            Assert.That(error, Is.EqualTo(SchedulerRegistrationError.AlreadyRegisteredWithAnotherScheduler));
            Assert.That(schedulerB.IsRegistered(host), Is.False);
            Assert.That(schedulerA.IsRegistered(host), Is.True, "Ownership must not be stolen by the failed attempt.");
        }

        [Test]
        public void TryUnregister_ReturnsHostToStandaloneOperation()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            Assert.That(scheduler.TryUnregister(host), Is.True);
            Assert.That(scheduler.IsRegistered(host), Is.False);
            Assert.That(host.IsCoordinatorOwned, Is.False);

            InvokePrivate(host, "Update");
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL), "Standalone Update must resume driving after unregistration.");
        }

        [Test]
        public void RegisteredHost_IgnoresItsOwnUpdateMessage_OnlyTheSchedulerDrivesIt()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            InvokePrivate(host, "Update");
            Assert.That(host.TotalUpdates, Is.Zero, "A coordinator-owned host's own Update message must no-op.");

            InvokePrivate(scheduler, "Update");
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL), "The scheduler's own Update must drive the registered host.");
        }

        [Test]
        public void SchedulerDestruction_ReturnsEveryHostToStandaloneOperation()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            InvokePrivate(scheduler, "OnDestroy");

            Assert.That(host.IsCoordinatorOwned, Is.False);
            InvokePrivate(host, "Update");
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL), "A host must resume standalone driving once its coordinator is gone.");
        }

        [Test]
        public void HostDestruction_WhileRegistered_LeavesNoStaleEntryInTheScheduler()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            DestroyHost(ref _hostObject);

            Assert.That(scheduler.RegisteredCount, Is.Zero);
        }

        [Test]
        public void UnregisteringAnotherHost_FromWithinADispatchCallback_DoesNotCorruptTheDriveLoop()
        {
            var scheduler = CreateScheduler();
            // hostA is bootstrapped (and so assigned its stable InstanceId) first, which -- both
            // hosts sharing the same default Normal profile and becoming eligible on the same frame
            // -- makes hostA sort before hostB in due order. Its own Enter-phase callback
            // unregisters hostB mid-loop, before hostB's own already-queued Registration would have
            // been driven: a real re-entrant mutation of the scheduler's registry from inside the
            // very loop iterating a snapshot of it.
            _hostObject = new GameObject("AIBT.Tests.P7033.HostA");
            var hostA = _hostObject.AddComponent<ProductionTreeHost>();
            ProductionTreeHost hostBRef = null;
            BurstContextResult DispatchA(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                if (request.Phase == BurstCallbackPhase.Enter) scheduler.TryUnregister(hostBRef);
                status = NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(hostA.TryBootstrap(
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.CreateSingleLeafProgram(),
                DispatchA,
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.TraceCapacity,
                null,
                out var failureA), Is.True, failureA.Code.ToString());
            Assert.That(scheduler.TryRegister(hostA, out _), Is.True);

            _hostObjectB = new GameObject("AIBT.Tests.P7033.HostB");
            var hostB = _hostObjectB.AddComponent<ProductionTreeHost>();
            hostBRef = hostB;
            Assert.That(hostB.TryBootstrap(
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.CreateSingleLeafProgram(),
                _ => NodeStatus.Running,
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.TraceCapacity,
                out var failureB), Is.True, failureB.Code.ToString());
            Assert.That(scheduler.TryRegister(hostB, out _), Is.True);
            Assert.That(hostA.InstanceId, Is.LessThan(hostB.InstanceId), "Test setup precondition: hostA must be due-ordered before hostB.");

            Assert.That(() => InvokePrivate(scheduler, "Update"), Throws.Nothing);

            Assert.That(scheduler.RegisteredCount, Is.EqualTo(1));
            Assert.That(scheduler.IsRegistered(hostA), Is.True);
            Assert.That(scheduler.IsRegistered(hostB), Is.False);
            Assert.That(hostA.TotalUpdates, Is.EqualTo(1uL));
            Assert.That(hostB.TotalUpdates, Is.Zero,
                "hostB must not be driven this frame -- it was unregistered by hostA's own callback before its turn.");
        }

        [Test]
        public void DueOrdering_HigherPriorityDrivesFirst_WhenOtherwiseTied()
        {
            var scheduler = CreateScheduler();
            var log = new List<string>();
            var low = CreateHostWithRecordingDispatch(log, "low");
            var high = CreateHostWithRecordingDispatch(log, "high");
            Assert.That(SchedulingProfile.TryCreate("test.low", 0, 1u, null, null, false, null, out var lowProfile, out _), Is.True);
            Assert.That(SchedulingProfile.TryCreate("test.high", 10, 1u, null, null, false, null, out var highProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(low, lowProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(high, highProfile, out _), Is.True);

            InvokePrivate(scheduler, "Update");

            Assert.That(log, Is.EqualTo(new[] { "high", "low" }));
        }

        [Test]
        public void DueOrdering_UrgentOutranksHigherPriority()
        {
            var scheduler = CreateScheduler();
            var log = new List<string>();
            var lowUrgent = CreateHostWithRecordingDispatch(log, "lowUrgent");
            var high = CreateHostWithRecordingDispatch(log, "high");
            Assert.That(SchedulingProfile.TryCreate("test.lowUrgent", 0, 1u, null, null, false, null, out var lowProfile, out _), Is.True);
            Assert.That(SchedulingProfile.TryCreate("test.high2", 10, 1u, null, null, false, null, out var highProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(lowUrgent, lowProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(high, highProfile, out _), Is.True);
            Assert.That(scheduler.RequestUrgentUpdate(lowUrgent), Is.True);

            InvokePrivate(scheduler, "Update");

            Assert.That(log, Is.EqualTo(new[] { "lowUrgent", "high" }));
        }

        [Test]
        public void DueOrdering_DeadlineOutranksUrgencyAndPriority()
        {
            var scheduler = CreateScheduler();
            var log = new List<string>();
            var deadlineHost = CreateHostWithRecordingDispatch(log, "deadline");
            var urgentHost = CreateHostWithRecordingDispatch(log, "urgent");
            Assert.That(SchedulingProfile.TryCreate("test.deadline", 0, 1u, 1u, null, false, null, out var deadlineProfile, out _), Is.True);
            Assert.That(SchedulingProfile.TryCreate("test.urgent", 100, 1u, null, null, false, null, out var urgentProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(deadlineHost, deadlineProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(urgentHost, urgentProfile, out _), Is.True);
            Assert.That(scheduler.RequestUrgentUpdate(urgentHost), Is.True);

            InvokePrivate(scheduler, "Update");

            Assert.That(log, Is.EqualTo(new[] { "deadline", "urgent" }));
        }

        [Test]
        public void DueOrdering_OlderEligibleAge_OutranksFreshHigherPriority_PreventingStarvation()
        {
            var scheduler = CreateScheduler();
            scheduler.SetFixedBudget(1.0);
            var log = new List<string>();
            var costly = CreateHostWithRecordingDispatch(log, "costly", sleepMilliseconds: 2);
            Assert.That(SchedulingProfile.TryCreate("test.costlyLow", 0, 1u, null, null, false, null, out var costlyProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(costly, costlyProfile, out _), Is.True);

            InvokePrivate(scheduler, "Update"); // frame 1: unmeasured -> cold-start admits, records a real ~2ms cost
            Assert.That(log, Is.EqualTo(new[] { "costly" }));
            log.Clear();

            InvokePrivate(scheduler, "Update"); // frame 2: now measured, far exceeds the 1us budget -> deferred, stays eligible
            Assert.That(log, Is.Empty);

            var fresh = CreateHostWithRecordingDispatch(log, "fresh");
            Assert.That(SchedulingProfile.TryCreate("test.freshHigh", 100, 1u, null, null, false, null, out var freshProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(fresh, freshProfile, out _), Is.True);
            scheduler.SetUnboundedBudget(); // isolate ordering from admission cutoff for the observable frame

            InvokePrivate(scheduler, "Update"); // frame 3: costly has waited since frame 2; fresh just became eligible this frame

            Assert.That(log, Is.EqualTo(new[] { "costly", "fresh" }),
                "costly's older eligible-since frame must outrank fresh's higher priority -- starvation protection.");
        }

        [Test]
        public void Cadence_SkipsFramesBetweenDrives()
        {
            var scheduler = CreateScheduler();
            var host = CreateBootstrappedHost();
            Assert.That(SchedulingProfile.TryCreate("test.cadence", 0, 3u, null, null, false, null, out var profile, out _), Is.True);
            Assert.That(scheduler.TryRegister(host, profile, out _), Is.True);

            InvokePrivate(scheduler, "Update"); // frame 1: eligible since registration, drives
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL));
            InvokePrivate(scheduler, "Update"); // frame 2: next eligible frame is 1+3=4
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL));
            InvokePrivate(scheduler, "Update"); // frame 3
            Assert.That(host.TotalUpdates, Is.EqualTo(1uL));
            InvokePrivate(scheduler, "Update"); // frame 4: eligible again
            Assert.That(host.TotalUpdates, Is.EqualTo(2uL));
        }

        [Test]
        public void Budget_Fixed_StoppingOnACostlyHost_AlsoDefersACheaperLowerRankedHost()
        {
            var scheduler = CreateScheduler();
            var log = new List<string>();
            var costly = CreateHostWithRecordingDispatch(log, "costly", sleepMilliseconds: 2);
            var cheap = CreateHostWithRecordingDispatch(log, "cheap");
            Assert.That(scheduler.TryRegister(costly, out _), Is.True); // registered first -> lower InstanceId -> due-sorted first when otherwise tied
            Assert.That(scheduler.TryRegister(cheap, out _), Is.True);

            InvokePrivate(scheduler, "Update"); // default Unbounded -- warm up real per-host measurements
            Assert.That(log, Is.EqualTo(new[] { "costly", "cheap" }));
            log.Clear();

            scheduler.SetFixedBudget(500.0); // comfortably smaller than costly's real ~2ms measured cost
            InvokePrivate(scheduler, "Update");

            Assert.That(log, Is.Empty,
                "The highest-ranked due host not fitting the remaining allowance stops admission for the whole frame -- it does not skip ahead to a cheaper lower-ranked host.");
            Assert.That(scheduler.LastFrameOverran, Is.True);
        }

        [Test]
        public void Budget_Bounded_ColdStartAlwaysAdmitsAnUnmeasuredHost_AndReportsOverrun()
        {
            var scheduler = CreateScheduler();
            scheduler.SetFixedBudget(1.0); // far smaller than any real measured cost
            var log = new List<string>();
            var host = CreateHostWithRecordingDispatch(log, "host", sleepMilliseconds: 1);
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            InvokePrivate(scheduler, "Update");

            Assert.That(log, Is.EqualTo(new[] { "host" }), "An unmeasured cost is never guessed -- always admitted on its first-ever drive.");
            Assert.That(scheduler.LastFrameOverran, Is.True);
        }

        [Test]
        public void Budget_Provider_IsConsultedEachFrame()
        {
            var scheduler = CreateScheduler();
            var calls = 0;
            scheduler.SetBudgetProvider(() => { calls++; return 1_000_000.0; });
            var host = CreateBootstrappedHost();
            Assert.That(scheduler.TryRegister(host, out _), Is.True);

            InvokePrivate(scheduler, "Update");
            InvokePrivate(scheduler, "Update");

            Assert.That(scheduler.BudgetMode, Is.EqualTo(SchedulerBudgetMode.Provider));
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void Budget_ShareCap_LimitsOneProfileGroupWithoutBlockingAnother()
        {
            var scheduler = CreateScheduler();
            var log = new List<string>();
            Assert.That(SchedulingProfile.TryCreate("test.cappedGroup", 0, 1u, null, 0.1, false, null, out var cappedProfile, out _), Is.True);
            Assert.That(SchedulingProfile.TryCreate("test.uncappedGroup", 0, 1u, null, null, false, null, out var uncappedProfile, out _), Is.True);
            var cappedA = CreateHostWithRecordingDispatch(log, "cappedA", sleepMilliseconds: 2);
            var cappedB = CreateHostWithRecordingDispatch(log, "cappedB", sleepMilliseconds: 2);
            var uncapped = CreateHostWithRecordingDispatch(log, "uncapped");
            Assert.That(scheduler.TryRegister(cappedA, cappedProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(cappedB, cappedProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(uncapped, uncappedProfile, out _), Is.True);

            InvokePrivate(scheduler, "Update"); // default Unbounded -- warm up real measurements for all three
            Assert.That(log, Is.EqualTo(new[] { "cappedA", "cappedB", "uncapped" }));
            log.Clear();

            // Thread.Sleep(2) can genuinely take far longer than 2ms under this environment's
            // timer-resolution granularity (observed ~15-20ms in practice), so the budget/share
            // below are derived from cappedA's own real measured cost rather than an assumed
            // sleep duration. Total is 20x that cost (comfortably fits it and uncapped's own
            // near-zero cost globally); the group's own share is half of it, so cappedA's own
            // admission alone already exceeds its group's cap for cappedB's turn.
            var cappedACost = GetLastMeasuredMicroseconds(scheduler, cappedA);
            Assert.That(cappedACost, Is.GreaterThan(0.0));
            scheduler.SetFixedBudget(cappedACost * 20.0);
            var share = 0.5 / 20.0;
            Assert.That(SchedulingProfile.TryCreate("test.cappedGroup", 0, 1u, null, share, false, null, out cappedProfile, out _), Is.True);
            Assert.That(scheduler.TryUnregister(cappedA), Is.True);
            Assert.That(scheduler.TryUnregister(cappedB), Is.True);
            Assert.That(scheduler.TryRegister(cappedA, cappedProfile, out _), Is.True);
            Assert.That(scheduler.TryRegister(cappedB, cappedProfile, out _), Is.True);

            InvokePrivate(scheduler, "Update");

            Assert.That(log, Does.Contain("cappedA"), "The group's first host still gets a chance -- a cap blocks further admissions within the group, not the very first.");
            Assert.That(log, Does.Not.Contain("cappedB"), "A second host in the same capped group must not exceed the group's own share.");
            Assert.That(log, Does.Contain("uncapped"), "A capped group's own limit must not block a different, uncapped group.");
        }

        [Test]
        public void ForcedBatchedJobsSameFrame_WithNoGeneratedCatalog_FailsWithStructuredDiagnostic()
        {
            var scheduler = CreateScheduler();
            scheduler.SetJobsCapabilities(RequireJobsCapabilities());
            var host = CreateBootstrappedHost(); // legacy delegate bootstrap -- no generated catalog to batch.
            Assert.That(SchedulingProfile.TryCreate(
                    "test.forcedBatchedNoCatalog", 0, 1u, null, null, false,
                    SchedulingPolicy.BatchedJobsSameFrame, out var profile, out _), Is.True);
            Assert.That(scheduler.TryRegister(host, profile, out _), Is.True);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AIBT ProductionTreeHost:.*Forced scheduling policy"));
            InvokePrivate(scheduler, "Update");

            Assert.That(host.LastFailure.Code, Is.Not.EqualTo(NativeRuntimeDiagnosticCodeV1.None),
                "A forced Jobs policy with nothing real to batch must fail with a structured diagnostic, never silently run as Immediate.");
            Assert.That(host.TotalUpdates, Is.Zero, "The host's own machine must never have been driven once its forced policy was rejected.");
        }

        [Test]
        public void ForcedPipelinedJobs_IsNeverSupportedYet_FailsEvenWithJobsCapabilitiesConfigured()
        {
            var scheduler = CreateScheduler();
            scheduler.SetJobsCapabilities(RequireJobsCapabilities());
            var host = CreateBootstrappedHost();
            Assert.That(SchedulingProfile.TryCreate(
                    "test.forcedPipelined", 0, 1u, null, null, false,
                    SchedulingPolicy.PipelinedJobs, out var profile, out _), Is.True);
            Assert.That(scheduler.TryRegister(host, profile, out _), Is.True);

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AIBT ProductionTreeHost:.*Forced scheduling policy"));
            InvokePrivate(scheduler, "Update");

            Assert.That(host.LastFailure.Code, Is.Not.EqualTo(NativeRuntimeDiagnosticCodeV1.None),
                "PipelinedJobs' own cross-frame stage semantics are not yet integrated by any configuration -- forcing it must fail honestly, not silently downgrade to a different policy.");
        }

        private static SchedulerJobsCapabilities RequireJobsCapabilities()
        {
            Assert.That(SchedulerJobsCapabilities.TryCreate(
                    1.0, 1.0, 1u, 8u, 8u, out var capabilities, out var error),
                Is.True, error.ToString());
            return capabilities;
        }

        private ProductionTreeHost CreateHostWithRecordingDispatch(List<string> log, string label, int sleepMilliseconds = 0)
        {
            var obj = new GameObject("AIBT.Tests.P7033.Host." + label);
            _extraObjects.Add(obj);
            var host = obj.AddComponent<ProductionTreeHost>();
            BurstContextResult Dispatch(in ProductionTreeHost.DispatchRequest request, out NodeStatus status)
            {
                // Only the leaf (node 1) ever receives a dispatch callback for this fixture -- the
                // root composite (node 0) is driven internally by the native machine with no
                // managed callback, per ProductionTreeHostTests's own already-proven assertion. The
                // leaf is entered once and then only re-ticked on every later drive (it never
                // returns a terminal status here), so Tick -- not Enter -- is the phase that fires
                // on every single DriveOneUpdate call, including the very first.
                if (request.Phase == BurstCallbackPhase.Tick && request.NodeIndex == 1)
                {
                    if (sleepMilliseconds > 0) System.Threading.Thread.Sleep(sleepMilliseconds);
                    log.Add(label);
                }
                status = NodeStatus.Running;
                return BurstContextResult.Success;
            }
            Assert.That(host.TryBootstrap(
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.CreateSingleLeafProgram(),
                Dispatch,
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.TraceCapacity,
                null,
                out var failure), Is.True, failure.Code.ToString());
            return host;
        }

        private static double GetLastMeasuredMicroseconds(ProductionTreeScheduler scheduler, ProductionTreeHost host)
        {
            var byHostField = typeof(ProductionTreeScheduler).GetField("_byHost", BindingFlags.NonPublic | BindingFlags.Instance);
            var byHost = (System.Collections.IDictionary)byHostField.GetValue(scheduler);
            var registration = byHost[host];
            var field = registration.GetType().GetField("LastMeasuredMicroseconds", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return (double)field.GetValue(registration);
        }

        private ProductionTreeScheduler CreateScheduler()
        {
            _schedulerObject = new GameObject("AIBT.Tests.P7033.Scheduler");
            return _schedulerObject.AddComponent<ProductionTreeScheduler>();
        }

        private ProductionTreeScheduler CreateSecondScheduler()
        {
            _schedulerObjectB = new GameObject("AIBT.Tests.P7033.SchedulerB");
            return _schedulerObjectB.AddComponent<ProductionTreeScheduler>();
        }

        private ProductionTreeHost CreateBootstrappedHost()
        {
            _hostObject = new GameObject("AIBT.Tests.P7033.Host");
            var host = _hostObject.AddComponent<ProductionTreeHost>();
            Assert.That(host.TryBootstrap(
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.CreateSingleLeafProgram(),
                _ => NodeStatus.Running,
                AIBT.Tests.Runtime.Integration.ProductionTreeHostTests.Fixture.TraceCapacity,
                out var failure), Is.True, failure.Code.ToString());
            return host;
        }

        private static void InvokePrivate(object target, string methodName)
            => target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    }
}
