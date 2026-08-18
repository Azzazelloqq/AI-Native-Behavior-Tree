using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AIBT.Burst;
using NUnit.Framework;

namespace AIBT.BurstAbi.Tests
{
    public sealed class AbiSurfaceTests
    {
        [Test]
        public void HandlesAndResultEnum_MatchAbiV1Exactly()
        {
            var handles = new[]
            {
                typeof(BlackboardReadHandle<int>), typeof(BlackboardWriteHandle<int>), typeof(BlackboardReadWriteHandle<int>),
                typeof(SnapshotReadHandle<int>), typeof(CommandHandle<int>), typeof(AsyncOperationHandle<int, uint>),
                typeof(CompletionHandle<int>)
            };
            foreach (var handle in handles)
            {
                var layout = handle.StructLayoutAttribute;
                Assert.That(layout.Value, Is.EqualTo(LayoutKind.Sequential), handle.FullName);
                Assert.That(layout.Pack, Is.EqualTo(4), handle.FullName);
                Assert.That(layout.Size, Is.EqualTo(8), handle.FullName);
                var fields = handle.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(fields.Length, Is.EqualTo(2), handle.FullName);
                Assert.That(fields.All(field => field.FieldType == typeof(uint)), Is.True, handle.FullName);
                Assert.That(fields.Select(field => field.Name), Is.EquivalentTo(new[] { "_ordinal", "_accessToken" }), handle.FullName);
            }

            Assert.That(Enum.GetNames(typeof(BurstContextResult)), Is.EqualTo(new[]
            {
                "Success", "InvalidHandle", "TypeMismatch", "PhaseViolation", "CapacityExceeded", "StaleCompletion",
                "Overflow", "InvalidEncoding", "IncompleteValue", "AlreadyCommitted", "InvalidStatus"
            }));
            Assert.That(Enum.GetValues(typeof(BurstContextResult)).Cast<BurstContextResult>().Select(value => (byte)value),
                Is.EqualTo(Enumerable.Range(0, 11).Select(value => (byte)value)));
        }
    }
}
