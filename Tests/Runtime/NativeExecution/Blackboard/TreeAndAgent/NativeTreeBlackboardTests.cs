using NUnit.Framework;
using Unity.Collections;
using AIBT.Burst;
using System.Runtime.InteropServices;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent
{
    public sealed class NativeTreeBlackboardTests
    {
        [Test]
        public void BuiltInMatrix_DefaultReadAndEqualWrite_PreserveCanonicalValuesWithoutVersionChange()
        {
            AssertBuiltIn(BuiltInBlackboardTypes.Bool, true);
            AssertBuiltIn(BuiltInBlackboardTypes.Int32, -17);
            AssertBuiltIn(BuiltInBlackboardTypes.Int64, long.MinValue + 1);
            AssertBuiltIn(BuiltInBlackboardTypes.Float32, 1.25f);
            AssertBuiltIn(BuiltInBlackboardTypes.Float64, -2.5d);
            AssertBuiltIn(BuiltInBlackboardTypes.Float2, new Float2Value(1f, 2f));
            AssertBuiltIn(BuiltInBlackboardTypes.Float3, new Float3Value(1f, 2f, 3f));
            AssertBuiltIn(BuiltInBlackboardTypes.Quaternion, new QuaternionValue(1f, 2f, 3f, 4f));
            var enumContract = StableHash.Fnv1A64("aibt.test.enum");
            AssertBuiltIn(BuiltInBlackboardTypes.Enum32, new Enum32Value(enumContract, -3), enumContract);
            AssertBuiltIn(BuiltInBlackboardTypes.FixedString32, new FixedString32Bytes("thirty-two"));
            AssertBuiltIn(BuiltInBlackboardTypes.FixedString64, new FixedString64Bytes("sixty-four"));
            AssertBuiltIn(BuiltInBlackboardTypes.FixedString128, new FixedString128Bytes("one-two-eight"));
            AssertBuiltIn(BuiltInBlackboardTypes.FixedString512, new FixedString512Bytes("five-one-two"));
            AssertBuiltIn(BuiltInBlackboardTypes.AgentId, new AgentId(9));
            AssertBuiltIn(BuiltInBlackboardTypes.EntityId, new EntityId(10));
            AssertBuiltIn(BuiltInBlackboardTypes.OperationId,
                new OperationId(new TreeInstanceId(11), new RuntimeNodeIndex(3), 4, 5));
            AssertBuiltIn(BuiltInBlackboardTypes.AssetId, new AssetId(12, 13, 14, true));
        }

        [Test]
        public void RegisteredMatrix_UsesExactSchemaEqualityAndFieldwiseCanonicalValidation()
        {
            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.registered"), 1, 8, 4,
                NativeBlackboardProgramBindingTests.Fixture.CanonicalEquality,
                StableHash.Fnv1A64("aibt.test.registered-schema"));
            var fields = new[]
            {
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1,
                    0, 4, 4, NativeBlackboardFieldEncodingV2.Int32LE, 0, default, 0),
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("weight"), BuiltInBlackboardTypes.Float32.TypeId, 1,
                    4, 4, 4, NativeBlackboardFieldEncodingV2.Float32BitsLE, 0, default, 0),
            };
            var value = new RegisteredFixture { Value = 7, Weight = 1.5f };
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateRegisteredBinding(
                "aibt.test.registered", "aibt.test.registered-schema", descriptor, value, fields);
            using (var scenario = Scenario.Create(binding))
            using (var equal = One(value))
            using (var nonCanonical = One(new RegisteredFixture { Value = 8, Weight = float.NaN }))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.Registered(0, execution.Program.RegisteredTypes[0]);
                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, type, out RegisteredFixture actual, out var version),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(actual.Value, Is.EqualTo(value.Value));
                Assert.That(actual.Weight, Is.EqualTo(value.Weight));
                Assert.That(version, Is.Zero);
                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, equal, out var changed),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(changed, Is.False);
                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, nonCanonical, out changed),
                    Is.EqualTo(BurstContextResult.InvalidEncoding));
                Assert.That(execution.View.TreeSlotVersions[0], Is.Zero);
                Assert.That(execution.View.TreeRevision[0], Is.Zero);
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void NestedRegisteredFixtureValidatesNestedSchemaAndZeroPadding()
        {
            var innerHash = new CompiledHash(new string('7', 64));
            var outerHash = new CompiledHash(new string('8', 64));
            var inner = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.inner"), 1, 4, 4,
                NativeBlackboardProgramBindingTests.Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.inner"));
            var outer = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.outer"), 1, 8, 4,
                NativeBlackboardProgramBindingTests.Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.outer"));
            var fields = new[]
            {
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("a_inner"), inner.TypeId, inner.Version, 0, 4, 4,
                    NativeBlackboardFieldEncodingV2.Registered,
                    inner.CanonicalSchemaId, innerHash, inner.EqualityContractId),
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("z_flag"), BuiltInBlackboardTypes.Bool.TypeId, 1, 4, 1, 1,
                    NativeBlackboardFieldEncodingV2.Bool8, 0, default, 0),
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1, 0, 4, 4,
                    NativeBlackboardFieldEncodingV2.Int32LE, 0, default, 0),
            };
            var innerType = NativeBlackboardProgramBindingTests.Fixture.RegisteredType(
                "aibt.test.inner", "aibt.schema.inner", inner, 0, fields[2]);
            fields[0] = new NativeRegisteredBlackboardFieldBindingV2(
                StableHash.Fnv1A64("a_inner"), inner.TypeId, inner.Version, 0, 4, 4,
                NativeBlackboardFieldEncodingV2.Registered,
                inner.CanonicalSchemaId, innerType.SchemaHash, inner.EqualityContractId);
            var outerType = NativeBlackboardProgramBindingTests.Fixture.RegisteredType(
                "aibt.test.outer", "aibt.schema.outer", outer, 1, fields[0], fields[1]);
            var types = new[] { innerType, outerType };
            fields = new[] { fields[2], fields[0], fields[1] };
            var bytes = new byte[] { 7, 0, 0, 0, 1, 0, 0, 0 };
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(outer), bytes, BlackboardScope.Tree, 0, types, fields);
            using (var scenario = Scenario.Create(binding))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.Registered(1, execution.Program.RegisteredTypes[1]);
                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, type, out NestedRegistered value, out _),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(value.Value, Is.EqualTo(7));
                Assert.That(value.Flag, Is.EqualTo(1));
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }

            bytes[5] = 1;
            var badBinding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(outer), bytes, BlackboardScope.Tree, 0, types, fields);
            AssertProgramDefaultRejects(badBinding);
        }

        [Test]
        public void InvalidEnumAssetAndFixedStringDefaultsRejectBeforeInstancePublication()
        {
            AssertProgramDefaultRejects(
                NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                    BuiltInBlackboardTypes.Enum32, new Enum32Value(77, 1), BlackboardScope.Tree,
                    StableHash.Fnv1A64("aibt.test.enum")));
            AssertProgramDefaultRejects(
                NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                    BuiltInBlackboardTypes.AssetId, default(AssetId)));
            var absentLocalWithPayload = new byte[BuiltInBlackboardTypes.AssetId.Size];
            absentLocalWithPayload[0] = 12;
            absentLocalWithPayload[8] = 13;
            absentLocalWithPayload[16] = 14;
            absentLocalWithPayload[24] = 0;
            AssertProgramDefaultRejects(
                NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                    BuiltInBlackboardTypes.AssetId, absentLocalWithPayload));
            var fixedBytes = new byte[BuiltInBlackboardTypes.FixedString32.Size];
            fixedBytes[fixedBytes.Length - 1] = 1;
            AssertProgramDefaultRejects(
                NativeBlackboardProgramBindingTests.Fixture.CreateBinding(BuiltInBlackboardTypes.FixedString32, fixedBytes));
        }

        [Test]
        public void NestedRegisteredFloatNegativeZero_IsCanonicalizedRecursivelyForEquality()
        {
            var innerHash = new CompiledHash(new string('5', 64));
            var outerHash = new CompiledHash(new string('6', 64));
            var inner = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.float-inner"), 1, 16, 8,
                NativeBlackboardProgramBindingTests.Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.float-inner"));
            var outer = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.float-outer"), 1, 16, 8,
                NativeBlackboardProgramBindingTests.Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.float-outer"));
            var fields = new[]
            {
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("inner"), inner.TypeId, inner.Version, 0, 16, 8,
                    NativeBlackboardFieldEncodingV2.Registered,
                    inner.CanonicalSchemaId, innerHash, inner.EqualityContractId),
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("single"), BuiltInBlackboardTypes.Float32.TypeId, 1, 0, 4, 4,
                    NativeBlackboardFieldEncodingV2.Float32BitsLE, 0, default, 0),
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("wide"), BuiltInBlackboardTypes.Float64.TypeId, 1, 8, 8, 8,
                    NativeBlackboardFieldEncodingV2.Float64BitsLE, 0, default, 0),
            };
            var innerType = NativeBlackboardProgramBindingTests.Fixture.RegisteredType(
                "aibt.test.float-inner", "aibt.schema.float-inner", inner, 0, fields[1], fields[2]);
            fields[0] = new NativeRegisteredBlackboardFieldBindingV2(
                StableHash.Fnv1A64("inner"), inner.TypeId, inner.Version, 0, 16, 8,
                NativeBlackboardFieldEncodingV2.Registered,
                inner.CanonicalSchemaId, innerType.SchemaHash, inner.EqualityContractId);
            var outerType = NativeBlackboardProgramBindingTests.Fixture.RegisteredType(
                "aibt.test.float-outer", "aibt.schema.float-outer", outer, 2, fields[0]);
            var types = new[] { innerType, outerType };
            fields = new[] { fields[1], fields[2], fields[0] };
            var negativeFloat32 = new byte[16]; negativeFloat32[3] = 0x80;
            AssertProgramDefaultRejects(NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(outer), negativeFloat32, BlackboardScope.Tree, 0, types, fields));
            var negativeFloat64 = new byte[16]; negativeFloat64[15] = 0x80;
            AssertProgramDefaultRejects(NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(outer), negativeFloat64, BlackboardScope.Tree, 0, types, fields));
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(outer), new byte[16], BlackboardScope.Tree, 0, types, fields);
            using (var scenario = Scenario.Create(binding))
            using (var candidate = One(new NestedFloatFixture { Single = -0.0f, Wide = -0.0d }))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.Registered(1, execution.Program.RegisteredTypes[1]);
                var result = NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, candidate, out var changed);
                var version = execution.View.TreeSlotVersions[0];
                var revision = execution.View.TreeRevision[0];
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
                Assert.That(result, Is.EqualTo(AIBT.Burst.BurstContextResult.Success));
                Assert.That(changed, Is.False);
                Assert.That(version, Is.Zero);
                Assert.That(revision, Is.Zero);
            }
        }

        [Test]
        public void Create_ReadEqualWriteChangedWriteAndReset_UseExactVersionsAndSameTreeBytes()
        {
            using (var scenario = Scenario.Create())
            using (var equal = One(5))
            using (var changed = One(9))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);

                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, type, out int value, out var version),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(value, Is.EqualTo(5));
                Assert.That(version, Is.Zero);
                Assert.That(execution.View.Semantic.TreeBlackboard[0], Is.EqualTo(5), "V2 must reuse the P2-006 Tree byte arena.");

                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, equal, out var changedValue),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(changedValue, Is.False);
                Assert.That(execution.View.TreeSlotVersions[0], Is.Zero);
                Assert.That(execution.View.TreeRevision[0], Is.Zero);

                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, changed, out changedValue),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(changedValue, Is.True);
                Assert.That(execution.View.TreeSlotVersions[0], Is.EqualTo(1));
                Assert.That(execution.View.TreeRevision[0], Is.EqualTo(1));
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());

                Assert.That(scenario.Instance.TryResetTreeBlackboard(scenario.ProgramLease, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out execution, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, type, out value, out version), Is.EqualTo(BurstContextResult.Success));
                Assert.That(value, Is.EqualTo(5));
                Assert.That(version, Is.EqualTo(2));
                Assert.That(execution.View.TreeRevision[0], Is.EqualTo(2));
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void AccessValidation_RejectsUndeclaredWrongTypeWrongScopeAndSharedBeforeMutation()
        {
            using (var scenario = Scenario.Create())
            using (var candidate = One(7))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var intType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                var floatType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);

                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 1, intType, out int _, out _), Is.EqualTo(BurstContextResult.InvalidHandle));
                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, floatType, out float _, out _), Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, floatType, candidate, out _), Is.EqualTo(BurstContextResult.TypeMismatch));
                Assert.That(execution.View.TreeSlotVersions[0], Is.Zero);
                Assert.That(execution.View.TreeRevision[0], Is.Zero);
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }

            var sharedBinding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Shared);
            using (var scenario = Scenario.Create(sharedBinding))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, type, out int _, out _),
                    Is.EqualTo(BurstContextResult.PhaseViolation));
                Assert.That(execution.View.TreeRevision[0], Is.Zero);
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void NonCanonicalFloatAndVersionOverflowRejectAtomically()
        {
            var floatBinding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Float32, 1f);
            using (var scenario = Scenario.Create(floatBinding))
            using (var nan = One(float.NaN))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, "FLOAT ACQUIRE: " + failure.Code);
                var floatType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                var result = NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, floatType, nan, out _);
                var floatVersion = execution.View.TreeSlotVersions[0];
                var floatRevision = execution.View.TreeRevision[0];
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, "FLOAT RELEASE: " + failure.Code);
                Assert.That(result, Is.EqualTo(BurstContextResult.InvalidEncoding), "NaN must reject before mutation.");
                Assert.That(floatVersion, Is.Zero);
                Assert.That(floatRevision, Is.Zero);
            }

            using (var scenario = Scenario.Create())
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, "OVERFLOW ACQUIRE: " + failure.Code);
                var versions = execution.View.TreeSlotVersions;
                versions[0] = ulong.MaxValue;
                BurstContextResult overflowResult;
                using (var changed = One(8))
                {
                    var intType = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32);
                    overflowResult = NativeTreeBlackboardV1.TryWrite(
                        execution.Program, execution.View, 0, 0, intType, changed, out _);
                }
                var currentByte = execution.View.Semantic.TreeBlackboard[0];
                var revision = execution.View.TreeRevision[0];
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, "OVERFLOW RELEASE: " + failure.Code);
                Assert.That(overflowResult, Is.EqualTo(BurstContextResult.Overflow));
                Assert.That(currentByte, Is.EqualTo(5));
                Assert.That(revision, Is.Zero);
            }
        }

        [Test]
        public void PositiveZeroCompiledDefaultAndEqualWriteRemainCanonicalNoOp()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Float32, 0.0f);
            using (var scenario = Scenario.Create(binding))
            using (var zero = One(0.0f))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Float32);
                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, zero, out var changed), Is.EqualTo(BurstContextResult.Success));
                Assert.That(changed, Is.False);
                Assert.That(execution.View.TreeSlotVersions[0], Is.Zero);
                Assert.That(execution.View.TreeRevision[0], Is.Zero);
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        private static NativeArray<T> One<T>(T value) where T : struct
        {
            var result = new NativeArray<T>(1, Allocator.Temp);
            result[0] = value;
            return result;
        }

        private static NativeArray<byte> OneBytes(byte[] value)
        {
            var result = new NativeArray<byte>(value.Length, Allocator.Temp);
            for (var index = 0; index < value.Length; index++) result[index] = value[index];
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RegisteredFixture
        {
            internal int Value;
            internal float Weight;
        }

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        private struct NestedRegistered
        {
            [FieldOffset(0)] internal int Value;
            [FieldOffset(4)] internal byte Flag;
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct NestedFloatFixture
        {
            [FieldOffset(0)] public float Single;
            [FieldOffset(8)] public double Wide;
        }

        private static void AssertInstanceCreationRejects(
            NativeProgramBlackboardBindingV2 binding,
            NativeRuntimeDiagnosticCodeV1 expected)
        {
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, failure.Code.ToString());
            Assert.That(NativeInstanceArenaCapacityV2.TryDerive(lease.View, out var capacity, out failure),
                Is.True, failure.Code.ToString());
            Assert.That(NativeInstanceArenaOwnerV1.TryCreateV2(
                lease, capacity, Allocator.Persistent, out var instance, out failure), Is.False);
            Assert.That(instance, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(expected));
            Assert.That(program.TryReleaseReadLease(lease, out failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
        }

        private static void AssertProgramDefaultRejects(NativeProgramBlackboardBindingV2 binding)
        {
            NativeProgramImageOwnerV1 owner = null;
            NativeRuntimeFailureV1 failure = default;
            var created = false;
            Assert.DoesNotThrow(() => created = NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out owner, out failure));
            Assert.That(created, Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeCapacityPlanInvalid));
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.ProgramBlackboardSlots));
        }

        private static void AssertBuiltIn<T>(BlackboardTypeDescriptor descriptor, T value, ulong enumContractId = 0)
            where T : unmanaged
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                descriptor, value, BlackboardScope.Tree, enumContractId);
            using (var scenario = Scenario.Create(binding))
            using (var candidate = One(value))
            {
                Assert.That(scenario.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                var type = NativeBlackboardTypeIdV2.BuiltIn(descriptor, enumContractId);
                Assert.That(NativeTreeBlackboardV1.TryRead(
                    execution.Program, execution.View, 0, 0, type, out T actual, out var version),
                    Is.EqualTo(BurstContextResult.Success), descriptor.ValueType.ToString());
                Assert.That(actual, Is.EqualTo(value), descriptor.ValueType.ToString());
                Assert.That(version, Is.Zero);
                Assert.That(NativeTreeBlackboardV1.TryWrite(
                    execution.Program, execution.View, 0, 0, type, candidate, out var changed),
                    Is.EqualTo(BurstContextResult.Success), descriptor.ValueType.ToString());
                Assert.That(changed, Is.False);
                Assert.That(execution.View.TreeSlotVersions[0], Is.Zero);
                Assert.That(execution.View.TreeRevision[0], Is.Zero);
                Assert.That(scenario.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        private sealed class Scenario : System.IDisposable
        {
            internal NativeProgramImageOwnerV1 ProgramOwner;
            internal NativeProgramReadLeaseV2 ProgramLease;
            internal NativeInstanceArenaOwnerV1 Instance;

            internal static Scenario Create()
                => Create(NativeBlackboardProgramBindingTests.Fixture.CreateBinding());

            internal static Scenario Create(NativeProgramBlackboardBindingV2 binding)
            {
                Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                    binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                    out var program, out var failure), Is.True, "PROGRAM CREATE: " + failure.Code);
                Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, "PROGRAM ACQUIRE: " + failure.Code);
                Assert.That(NativeInstanceArenaCapacityV2.TryDerive(lease.View, out var capacity, out failure), Is.True, "CAPACITY: " + failure.Code);
                Assert.That(NativeInstanceArenaOwnerV1.TryCreateV2(
                    lease, capacity, Allocator.Persistent, out var instance, out failure), Is.True, "INSTANCE CREATE: " + failure.Code);
                return new Scenario { ProgramOwner = program, ProgramLease = lease, Instance = instance };
            }

            public void Dispose()
            {
                NativeRuntimeFailureV1 failure;
                if (Instance != null) Assert.That(Instance.TryDispose(out failure), Is.True, failure.Code.ToString());
                if (ProgramOwner != null)
                {
                    Assert.That(ProgramOwner.TryReleaseReadLease(ProgramLease, out failure), Is.True, failure.Code.ToString());
                    Assert.That(ProgramOwner.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }
    }
}
