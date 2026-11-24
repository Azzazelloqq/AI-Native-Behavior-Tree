using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-014 review follow-up (item 2): a <c>CapacityExceeded</c> refusal has several causes. The
    /// Console text names the node, names the limit only where the failing site knows it, and otherwise
    /// lists the possible limits without calling one of them the usual cause.
    /// </summary>
    public sealed class CapacityExceededMessageTests
    {
        private const string S = "aibt.native.";
        private NativeLibraryKit _kit;

        [SetUp]
        public void SetUp() => _kit = new NativeLibraryKit();

        [TearDown]
        public void TearDown() => _kit.Dispose();

        [Test]
        public void AFullEffectQueue_IsNamedAsTheCause_NotTheValueSessionLimit()
        {
            // Effect capacity 8 and nobody drains: each update's signal adds one effect until the reservation
            // before the next callback finds too few free entries, and the node is not called.
            var prepared = _kit.Prepare(Tree("tests.p11014.full-effect-queue", "loop", new string[0],
                CompositeWith("loop", "aibt.core.repeater", "\"count\": 4000000000, \"stopOnFailure\": false", "cycle"),
                Composite("cycle", "aibt.core.memory-sequence", "pace", "ping"),
                Leaf("pace", "aibt.stdlib.wait", "\"ticks\": 2"),
                Leaf("ping", S + "signal", "\"code\": 1")));
            var host = _kit.Start(prepared, effectCapacity: 8).Host;

            var updates = 0;
            var errors = CaptureErrors(() =>
            {
                while (updates < 64 && host.LastFailure.Code == NativeRuntimeDiagnosticCodeV1.None)
                {
                    host.DriveOneUpdate();
                    updates++;
                }
            });

            Assert.That(host.LastFailure.Code, Is.Not.EqualTo(NativeRuntimeDiagnosticCodeV1.None), "the host stops");
            var message = string.Join("\n", errors);
            Debug.Log("P11-014 item 2, full effect queue after " + updates + " updates: " + message);
            Assert.That(updates, Is.GreaterThan(1), "the queue had room for the first signals");
            Assert.That(message, Does.Contain("node 'ping'"), message);
            Assert.That(message, Does.Contain("effect queue"), message);
            Assert.That(message, Does.Not.Contain("value-session").And.Not.Contain("usual cause").And.Not.Contain("Read a value once"), message);
        }

        private static List<string> CaptureErrors(TestDelegate action)
        {
            var errors = new List<string>();
            void Collect(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception) errors.Add(condition);
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Collect;
            try { action(); }
            finally { Application.logMessageReceived -= Collect; }
            return errors;
        }
    }
}
