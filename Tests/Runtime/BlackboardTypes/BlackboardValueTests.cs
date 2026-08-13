using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace AIBT.Tests.Runtime.BlackboardTypes
{
    public sealed class BlackboardValueTests
    {
        [Test]
        public void RuntimeRepresentationsContainNoManagedReferences()
        {
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<BlackboardValue>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<BlackboardTypeDescriptor>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<RegisteredUnmanagedTypeDescriptor>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<AssetId>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<Float3Value>(), Is.False);
        }

        [Test]
        public void EveryBuiltInTypeHasAStableDescriptor()
        {
            for (var value = BlackboardValueType.Bool; value <= BlackboardValueType.AssetId; value++)
            {
                Assert.That(BuiltInBlackboardTypes.TryGet(value, out var descriptor), Is.True, value.ToString());
                Assert.That(descriptor.IsValid, Is.True, value.ToString());
                Assert.That(descriptor.ValueType, Is.EqualTo(value));
                Assert.That(descriptor.Version, Is.EqualTo(1));
                Assert.That(descriptor.TypeId, Is.Not.EqualTo(0));
            }

            Assert.That(BuiltInBlackboardTypes.TryGet(BlackboardValueType.Registered, out _), Is.False);
            Assert.That(BuiltInBlackboardTypes.Bool.TypeId, Is.EqualTo(StableHash.Fnv1A64("Bool")));
        }

        [Test]
        public void ScalarAndIdentifierValuesRoundTripByExactType()
        {
            var operation = new OperationId(new TreeInstanceId(7), new RuntimeNodeIndex(3), 2, 11);
            var asset = AssetId.Parse("0123456789abcdef0123456789abcdef", -12);

            Assert.That(BlackboardValue.FromBool(true).TryGetBool(out var boolValue), Is.True);
            Assert.That(boolValue, Is.True);
            Assert.That(BlackboardValue.FromInt32(-17).TryGetInt32(out var intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(-17));
            Assert.That(BlackboardValue.FromInt64(long.MinValue).TryGetInt64(out var longValue), Is.True);
            Assert.That(longValue, Is.EqualTo(long.MinValue));
            Assert.That(BlackboardValue.FromAgentId(new AgentId(9)).TryGetAgentId(out var agent), Is.True);
            Assert.That(agent, Is.EqualTo(new AgentId(9)));
            Assert.That(BlackboardValue.FromEntityId(new EntityId(10)).TryGetEntityId(out var entity), Is.True);
            Assert.That(entity, Is.EqualTo(new EntityId(10)));
            Assert.That(BlackboardValue.FromOperationId(operation).TryGetOperationId(out var operationValue), Is.True);
            Assert.That(operationValue, Is.EqualTo(operation));
            Assert.That(BlackboardValue.FromAssetId(asset).TryGetAssetId(out var assetValue), Is.True);
            Assert.That(assetValue, Is.EqualTo(asset));
        }

        [Test]
        public void TypedGetterDoesNotExposeOverlaidDataForWrongType()
        {
            var value = BlackboardValue.FromInt64(long.MaxValue);

            Assert.That(value.TryGetInt32(out var wrongType), Is.False);
            Assert.That(wrongType, Is.Zero);
        }

        [Test]
        public void NegativeZeroIsCanonicalAndAllNonFiniteValuesAreRejected()
        {
            var negativeZero = BlackboardValue.FromFloat32(-0f);
            var positiveZero = BlackboardValue.FromFloat32(0f);

            Assert.That(negativeZero, Is.EqualTo(positiveZero));
            Assert.That(negativeZero.GetHashCode(), Is.EqualTo(positiveZero.GetHashCode()));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlackboardValue.FromFloat32(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlackboardValue.FromFloat32(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlackboardValue.FromFloat64(double.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuaternionValue(0, 0, float.NaN, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Float2Value(float.PositiveInfinity, 0));
        }

        [Test]
        public void VectorQuaternionAndFixedStringRoundTripAcrossCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var float2 = new Float2Value(1.25f, -2.5f);
                var float3 = new Float3Value(1.25f, -2.5f, 3.75f);
                var quaternion = new QuaternionValue(0.1f, 0.2f, 0.3f, 0.4f);
                const string text = "Grüße 世界";

                Assert.That(BlackboardValue.FromFloat2(float2).TryGetFloat2(out var float2Value), Is.True);
                Assert.That(float2Value, Is.EqualTo(float2));
                Assert.That(BlackboardValue.FromFloat3(float3).TryGetFloat3(out var float3Value), Is.True);
                Assert.That(float3Value, Is.EqualTo(float3));
                Assert.That(BlackboardValue.FromQuaternion(quaternion).TryGetQuaternion(out var quaternionValue), Is.True);
                Assert.That(quaternionValue, Is.EqualTo(quaternion));
                Assert.That(BlackboardValue.FromString128(text).TryGetFixedString(out var textValue), Is.True);
                Assert.That(textValue, Is.EqualTo("Grüße 世界"));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void EnumEqualityIncludesItsRegisteredContract()
        {
            var firstContract = new Enum32Value(StableHash.Fnv1A64("game.state"), 3);
            var secondContract = new Enum32Value(StableHash.Fnv1A64("game.team"), 3);

            Assert.That(BlackboardValue.FromEnum32(firstContract), Is.Not.EqualTo(BlackboardValue.FromEnum32(secondContract)));
        }

        [Test]
        public void AssetIdRequiresCanonicalLowercaseGuidAndPreservesOptionalFileId()
        {
            const string guid = "0123456789abcdefabcdef0123456789";

            Assert.That(AssetId.TryParse(guid, 42, out var value), Is.True);
            Assert.That(value.ToGuidString(), Is.EqualTo(guid));
            Assert.That(value.HasLocalFileId, Is.True);
            Assert.That(value.LocalFileId, Is.EqualTo(42));
            Assert.That(AssetId.TryParse(guid.ToUpperInvariant(), null, out _), Is.False);
        }

        [Test]
        public void AssetIdNormalizesAbsentLocalFileIdForEqualityAndHashing()
        {
            var canonical = new AssetId(1, 2);
            var ignoredPayload = new AssetId(1, 2, 999, false);

            Assert.That(ignoredPayload.LocalFileId, Is.Zero);
            Assert.That(ignoredPayload, Is.EqualTo(canonical));
            Assert.That(ignoredPayload.GetHashCode(), Is.EqualTo(canonical.GetHashCode()));
        }

        [Test]
        public void OpaqueIdentifierFactoriesRejectInvalidValues()
        {
            Assert.Throws<ArgumentException>(() => BlackboardValue.FromAgentId(default));
            Assert.Throws<ArgumentException>(() => BlackboardValue.FromEntityId(default));
            Assert.Throws<ArgumentException>(() => BlackboardValue.FromOperationId(default));
            Assert.Throws<ArgumentException>(() => BlackboardValue.FromAssetId(default));
            Assert.That(default(BlackboardValue).IsValid, Is.False);
        }

        [Test]
        public void RegisteredDescriptorRequiresDeterministicEqualityAndCoherentMigrationMetadata()
        {
            var typeId = StableHash.Fnv1A64("game.damage");
            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                typeId,
                2,
                16,
                4,
                StableHash.Fnv1A64("game.damage.equals.v1"),
                StableHash.Fnv1A64("game.damage.schema.v2"),
                1,
                StableHash.Fnv1A64("game.damage.migrate.1-2"));

            Assert.That(descriptor.IsValid, Is.True);
            Assert.That(descriptor.HasCanonicalSchema, Is.True);
            Assert.That(descriptor.HasMigration, Is.True);
            Assert.That(BlackboardTypeDescriptor.FromRegistered(descriptor).ValueType, Is.EqualTo(BlackboardValueType.Registered));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RegisteredUnmanagedTypeDescriptor(typeId, 1, 8, 3, 1));
            Assert.Throws<ArgumentException>(() => new RegisteredUnmanagedTypeDescriptor(typeId, 1, 10, 4, 1));
            Assert.Throws<ArgumentException>(() => new RegisteredUnmanagedTypeDescriptor(typeId, 2, 8, 4, 1, 0, 1, 0));
        }

        [Test]
        public void RuntimeHasNoDotsEntitiesDependency()
        {
            var references = typeof(EntityId).Assembly.GetReferencedAssemblies();
            Assert.That(Array.Exists(references, reference => reference.Name == "Unity.Entities"), Is.False);
        }
    }
}
