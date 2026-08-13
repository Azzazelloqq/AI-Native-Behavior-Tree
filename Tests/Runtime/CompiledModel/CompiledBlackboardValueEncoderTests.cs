using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class CompiledBlackboardValueEncoderTests
    {
        [Test]
        public void Encode_WritesLittleEndianIntegersAndCanonicalFloatingPoint()
        {
            Assert.That(
                CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromInt32(unchecked((int)0x89abcdef))),
                Is.EqualTo(new byte[] { 0xef, 0xcd, 0xab, 0x89 }));
            Assert.That(
                CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromFloat32(1f)),
                Is.EqualTo(new byte[] { 0x00, 0x00, 0x80, 0x3f }));
        }

        [Test]
        public void Encode_WritesVectorComponentsInSemanticOrder()
        {
            var bytes = CompiledBlackboardValueEncoder.Encode(
                BlackboardValue.FromFloat3(new Float3Value(1f, 2f, -0f)));

            Assert.That(bytes, Is.EqualTo(new byte[]
            {
                0x00, 0x00, 0x80, 0x3f,
                0x00, 0x00, 0x00, 0x40,
                0x00, 0x00, 0x00, 0x00,
            }));
        }

        [Test]
        public void Encode_FixedStringUsesUInt16LengthUtf8AndZeroFilledCapacity()
        {
            var bytes = CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromString32("Aé"));

            Assert.That(bytes.Length, Is.EqualTo(BuiltInBlackboardTypes.FixedString32.Size));
            Assert.That(bytes[0], Is.EqualTo(3));
            Assert.That(bytes[1], Is.Zero);
            Assert.That(bytes[2], Is.EqualTo(0x41));
            Assert.That(bytes[3], Is.EqualTo(0xc3));
            Assert.That(bytes[4], Is.EqualTo(0xa9));
            for (var index = 5; index < bytes.Length; index++)
            {
                Assert.That(bytes[index], Is.Zero);
            }
        }

        [Test]
        public void Encode_OperationIdUsesExplicitFieldOffsetsWithoutStructPadding()
        {
            var value = BlackboardValue.FromOperationId(
                new OperationId(new TreeInstanceId(0x0102030405060708), new RuntimeNodeIndex(0x11223344), 0xaabbccdd, 0x8899aabbccddeeff));
            var bytes = CompiledBlackboardValueEncoder.Encode(value);

            Assert.That(bytes.Length, Is.EqualTo(BuiltInBlackboardTypes.OperationId.Size));
            Assert.That(bytes[0], Is.EqualTo(0x08));
            Assert.That(bytes[7], Is.EqualTo(0x01));
            Assert.That(bytes[8], Is.EqualTo(0x44));
            Assert.That(bytes[12], Is.EqualTo(0xdd));
            Assert.That(bytes[16], Is.EqualTo(0xff));
            Assert.That(bytes[23], Is.EqualTo(0x88));
            AssertZeroFrom(bytes, 24);
        }

        [Test]
        public void Encode_AssetIdMakesOptionalLocalIdPresenceExplicitAndZeroesPadding()
        {
            var bytes = CompiledBlackboardValueEncoder.Encode(
                BlackboardValue.FromAssetId(new AssetId(1, 2, -2, true)));

            Assert.That(bytes.Length, Is.EqualTo(BuiltInBlackboardTypes.AssetId.Size));
            Assert.That(bytes[0], Is.EqualTo(1));
            Assert.That(bytes[8], Is.EqualTo(2));
            Assert.That(bytes[16], Is.EqualTo(0xfe));
            Assert.That(bytes[23], Is.EqualTo(0xff));
            Assert.That(bytes[24], Is.EqualTo(1));
            AssertZeroFrom(bytes, 25);
        }

        [Test]
        public void Encode_AllBuiltInValuesMatchTheirDeclaredSlotSize()
        {
            var values = new[]
            {
                BlackboardValue.FromBool(true),
                BlackboardValue.FromInt32(1),
                BlackboardValue.FromInt64(1),
                BlackboardValue.FromFloat32(1),
                BlackboardValue.FromFloat64(1),
                BlackboardValue.FromFloat2(new Float2Value(1, 2)),
                BlackboardValue.FromFloat3(new Float3Value(1, 2, 3)),
                BlackboardValue.FromQuaternion(new QuaternionValue(1, 2, 3, 4)),
                BlackboardValue.FromEnum32(new Enum32Value(9, -1)),
                BlackboardValue.FromString32("a"),
                BlackboardValue.FromString64("a"),
                BlackboardValue.FromString128("a"),
                BlackboardValue.FromString512("a"),
                BlackboardValue.FromAgentId(new AgentId(1)),
                BlackboardValue.FromEntityId(new EntityId(1)),
                BlackboardValue.FromOperationId(new OperationId(new TreeInstanceId(1), new RuntimeNodeIndex(0), 0, 0)),
                BlackboardValue.FromAssetId(new AssetId(1, 0)),
            };

            foreach (var value in values)
            {
                Assert.That(BuiltInBlackboardTypes.TryGet(value.Type, out var descriptor), Is.True);
                Assert.That(CompiledBlackboardValueEncoder.Encode(value).Length, Is.EqualTo(descriptor.Size), value.Type.ToString());
            }
        }

        [Test]
        public void Encode_RejectsInvalidAndRegisteredValues()
        {
            Assert.Throws<ArgumentException>(() => CompiledBlackboardValueEncoder.Encode(default));
        }

        private static void AssertZeroFrom(byte[] bytes, int start)
        {
            for (var index = start; index < bytes.Length; index++)
            {
                Assert.That(bytes[index], Is.Zero, "Unexpected data at byte " + index + ".");
            }
        }
    }
}
