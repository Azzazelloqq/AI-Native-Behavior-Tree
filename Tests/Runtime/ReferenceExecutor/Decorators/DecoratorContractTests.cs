using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class DecoratorContractTests
    {
        [Test]
        public void BuiltInRegistryUsesExactTypeAndVersion()
        {
            var registry = ReferenceDecoratorRegistry.CreatePhase1BuiltIns();

            Assert.That(registry.TryGet(StableHash.Fnv1A64("aibt.core.inverter"), 1, out var kind), Is.True);
            Assert.That(kind, Is.EqualTo(ReferenceDecoratorKind.Inverter));
            Assert.That(registry.TryGet(StableHash.Fnv1A64("aibt.core.inverter"), 2, out _), Is.False);
            Assert.That(registry.TryGet(StableHash.Fnv1A64("aibt.core.unknown"), 1, out _), Is.False);
        }

        [Test]
        public void RegistryRejectsDuplicateTypeAndVersion()
        {
            var binding = new ReferenceDecoratorBinding(17, 1, ReferenceDecoratorKind.Inverter);

            Assert.Throws<ArgumentException>(() => new ReferenceDecoratorRegistry(new[] { binding, binding }));
        }

        [TestCase(0u, false)]
        [TestCase(1u, true)]
        [TestCase(uint.MaxValue, false)]
        public void RepeaterDecoderAcceptsPositiveCountAndCanonicalBoolean(uint count, bool stopOnFailure)
        {
            var bytes = new byte[8];
            WriteUInt32(bytes, count);
            bytes[4] = stopOnFailure ? (byte)1 : (byte)0;

            if (count == 0)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceDecoratorConfigurationDecoder.DecodeRepeater(bytes));
                return;
            }

            var decoded = ReferenceDecoratorConfigurationDecoder.DecodeRepeater(bytes);
            Assert.That(decoded.Count, Is.EqualTo(count));
            Assert.That(decoded.StopOnFailure, Is.EqualTo(stopOnFailure));
        }

        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(long.MaxValue)]
        public void TimeoutDecoderEnforcesPositiveSignedInt64Duration(long duration)
        {
            var bytes = DurationConfiguration(unchecked((ulong)duration), 1, 0);

            if (duration == 0)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceDecoratorConfigurationDecoder.DecodeTimeout(bytes));
                return;
            }

            var decoded = ReferenceDecoratorConfigurationDecoder.DecodeTimeout(bytes);
            Assert.That(decoded.DurationMicroseconds, Is.EqualTo(duration));
            Assert.That(decoded.TerminalResult, Is.EqualTo(NodeStatus.Success));
        }

        [Test]
        public void DurationAboveSignedInt64RangeIsRejected()
        {
            var bytes = DurationConfiguration((ulong)long.MaxValue + 1, 0, 0);

            Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceDecoratorConfigurationDecoder.DecodeTimeout(bytes));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceDecoratorConfigurationDecoder.DecodeCooldown(bytes));
        }

        [Test]
        public void CooldownDecoderMapsEveryPersistedEnumValue()
        {
            var onEnter = ReferenceDecoratorConfigurationDecoder.DecodeCooldown(DurationConfiguration(25, 0, 0));
            var onSuccess = ReferenceDecoratorConfigurationDecoder.DecodeCooldown(DurationConfiguration(25, 1, 1));

            Assert.That(onEnter.BlockedResult, Is.EqualTo(NodeStatus.Failure));
            Assert.That(onEnter.StartPolicy, Is.EqualTo(ReferenceCooldownStartPolicy.OnEnter));
            Assert.That(onSuccess.BlockedResult, Is.EqualTo(NodeStatus.Success));
            Assert.That(onSuccess.StartPolicy, Is.EqualTo(ReferenceCooldownStartPolicy.OnSuccessfulExit));
        }

        [TestCase(7)]
        [TestCase(9)]
        [TestCase(15)]
        [TestCase(17)]
        public void DecoratorDecodersRejectWrongConfigurationSize(int length)
        {
            var bytes = new byte[length];

            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeRepeater(bytes));
            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeTimeout(bytes));
            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeCooldown(bytes));
        }

        [Test]
        public void DecoratorDecodersRejectUndefinedEnumsAndNonZeroPadding()
        {
            var repeater = new byte[8];
            WriteUInt32(repeater, 1);
            repeater[4] = 2;
            var timeout = DurationConfiguration(1, 2, 0);
            var cooldown = DurationConfiguration(1, 0, 2);
            var padded = DurationConfiguration(1, 0, 0);
            padded[15] = 1;

            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeRepeater(repeater));
            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeTimeout(timeout));
            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeCooldown(cooldown));
            Assert.Throws<ArgumentException>(() => ReferenceDecoratorConfigurationDecoder.DecodeCooldown(padded));
        }

        private static byte[] DurationConfiguration(ulong duration, byte result, byte policy)
        {
            var bytes = new byte[16];
            for (var index = 0; index < 8; index++) bytes[index] = (byte)(duration >> (index * 8));
            bytes[8] = result;
            bytes[9] = policy;
            return bytes;
        }

        private static void WriteUInt32(byte[] bytes, uint value)
        {
            for (var index = 0; index < 4; index++) bytes[index] = (byte)(value >> (index * 8));
        }
    }
}
