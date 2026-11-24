using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Runtime.Integration
{
    /// <summary>
    /// P11-016 item 2 (ADR-P7-010 amendment 2026-11-24): a host has a trace channel by default only in
    /// the Editor. The default is one pure function of "is this the Editor" and the requested capacity,
    /// so both branches are tested here; <see cref="NativeTraceChannelCapacityV1.None"/> means no channel
    /// everywhere, and a capacity the caller passes is used as it is.
    /// </summary>
    public sealed class NativeTraceDefaultTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void DestroyGameObject()
        {
            if (_gameObject == null) return;
            var host = _gameObject.GetComponent<ProductionTreeHost>();
            if (host != null)
                typeof(ProductionTreeHost).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void TheEditorDefaultIsTodaysCapacity()
        {
            var resolved = NativeTraceDefaultsV1.Resolve(null, isEditor: true);
            Assert.That(resolved.HasValue, Is.True);
            Assert.That(resolved.Value.RecordCapacity, Is.EqualTo(256u));
            Assert.That(resolved.Value.PayloadCapacity, Is.EqualTo(0u));
            Assert.That(resolved.Value.MaximumPayloadBytes, Is.EqualTo(0u));
            Assert.That(resolved.Value.EmissionCapacity, Is.EqualTo(512u));
        }

        [Test]
        public void APlayerHasNoChannelByDefault()
            => Assert.That(NativeTraceDefaultsV1.Resolve(null, isEditor: false).HasValue, Is.False);

        [Test]
        public void NoneMeansNoChannelInTheEditorAndInAPlayer()
        {
            Assert.That(NativeTraceDefaultsV1.Resolve(NativeTraceChannelCapacityV1.None, isEditor: true).HasValue, Is.False);
            Assert.That(NativeTraceDefaultsV1.Resolve(NativeTraceChannelCapacityV1.None, isEditor: false).HasValue, Is.False);
            Assert.That(NativeTraceChannelCapacityV1.None.IsNone, Is.True);
            Assert.That(default(NativeTraceChannelCapacityV1).IsNone, Is.True);
            Assert.That(new NativeTraceChannelCapacityV1(0, 0, 0, 1).IsNone, Is.False, "Only an all-zero capacity is None.");
        }

        [Test]
        public void ACapacityTheCallerPassesIsUsedAsItIsInBoth()
        {
            var requested = new NativeTraceChannelCapacityV1(8, 0, 0, 16);
            foreach (var isEditor in new[] { true, false })
            {
                var resolved = NativeTraceDefaultsV1.Resolve(requested, isEditor);
                Assert.That(resolved.HasValue, Is.True);
                Assert.That(resolved.Value.RecordCapacity, Is.EqualTo(8u));
                Assert.That(resolved.Value.EmissionCapacity, Is.EqualTo(16u));
            }
        }

        [Test]
        public void BootstrapWithNoneSucceedsWithoutAChannelAndRuns()
        {
            _gameObject = new GameObject("AIBT.Tests.P11016.TraceNone");
            var host = _gameObject.AddComponent<ProductionTreeHost>();
            var program = ProductionTreeHostTests.Fixture.CreateSingleLeafProgram();
            Assert.That(host.TryBootstrap(program, _ => NodeStatus.Success, NativeTraceChannelCapacityV1.None, out var failure),
                Is.True, failure.Code.ToString());
            Assert.That(host.TraceChannelOwner, Is.Null);
            typeof(ProductionTreeHost).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(host, null);
            Assert.That(host.LastRootResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void BootstrapWithACapacityOwnsAChannel()
        {
            _gameObject = new GameObject("AIBT.Tests.P11016.TraceOn");
            var host = _gameObject.AddComponent<ProductionTreeHost>();
            var program = ProductionTreeHostTests.Fixture.CreateSingleLeafProgram();
            Assert.That(host.TryBootstrap(program, _ => NodeStatus.Success, new NativeTraceChannelCapacityV1(8, 0, 0, 16), out var failure),
                Is.True, failure.Code.ToString());
            Assert.That(host.TraceChannelOwner, Is.Not.Null);
            Assert.That(host.TraceChannelOwner.Capacity.RecordCapacity, Is.EqualTo(8u));
        }
    }
}
