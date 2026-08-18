using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using AIBT.Burst;
using AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Is = NUnit.Framework.Is;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.Shared
{
    public sealed class NativeSharedContextTests
    {
        [Test]
        public void ExecutionViewExposesOnlyReadOnlyArraysAndBurstReadStillWorks()
        {
            var contextType = typeof(NativeSharedContextViewV1);
            Assert.That(contextType.GetProperty(nameof(NativeSharedContextViewV1.Values)).PropertyType,
                Is.EqualTo(typeof(NativeArray<byte>.ReadOnly)));
            Assert.That(contextType.GetProperty(nameof(NativeSharedContextViewV1.SlotVersions)).PropertyType,
                Is.EqualTo(typeof(NativeArray<ulong>.ReadOnly)));
            Assert.That(contextType.GetProperty(nameof(NativeSharedContextViewV1.Revision)).PropertyType,
                Is.EqualTo(typeof(NativeArray<ulong>.ReadOnly)));
            Assert.That(typeof(NativeArray<byte>.ReadOnly).GetProperty("Item").CanWrite, Is.False);

            using (var scenario = SharedScenario.Create())
            using (var result = new NativeArray<BurstContextResult>(1, Allocator.TempJob))
            using (var value = new NativeArray<int>(1, Allocator.TempJob))
            {
                var tree = scenario.Bind(1);
                Assert.That(tree.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAcquireExecutionView(
                    tree.Binding, execution, out var view, out failure), Is.True, failure.Code.ToString());
                var handle = new SharedReadJob
                {
                    View = view,
                    Type = NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                    Result = result,
                    Value = value,
                }.Schedule();
                handle.Complete();
                Assert.That(result[0], Is.EqualTo(BurstContextResult.Success));
                Assert.That(value[0], Is.EqualTo(5));
                Assert.That(tree.Instance.TryReleaseExecutionLease(execution, out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void ContributionUpdatePreflightsBoundSelectionBeforePublication()
        {
            using (var scenario = SharedScenario.Create())
            using (var tree = scenario.Bind(2))
            {
                Assert.That(NativeExecuteSelectionWindowOwnerV1.TryCreate(
                    new NativeExecuteSelectionCapacityV1(2, 2), Allocator.Persistent,
                    out var selection, out var failure), Is.True, failure.Code.ToString());
                var entries = new NativeArray<NativeExecuteSelectionEntryV1>(2, Allocator.Temp);
                try
                {
                    entries[0] = new NativeExecuteSelectionEntryV1(new TreeInstanceId(1), 0, 0);
                    entries[1] = new NativeExecuteSelectionEntryV1(new TreeInstanceId(2), 2, 8);
                    Assert.That(selection.TryBegin(entries, out var selected, out failure), Is.True, failure.Code.ToString());
                    Assert.That(scenario.Context.TryBeginUpdate(
                        selection, selected, out NativeSharedUpdateWindowV1 update, out failure),
                        Is.True, failure.Code.ToString());
                    Assert.That(update.Count, Is.EqualTo(1));
                    Assert.That(scenario.Context.TryAbortUpdate(update, out failure), Is.True, failure.Code.ToString());
                    Assert.That(selection.TryAbort(selected, out failure), Is.True, failure.Code.ToString());
                }
                finally
                {
                    entries.Dispose();
                    Assert.That(selection.TryDispose(out failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void CanonicalBuiltInAndRegisteredDefaultsAreReadableWithoutMutation()
        {
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Bool, true);
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Int32, -17);
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Int64, long.MinValue + 1);
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Float32, 1.25f);
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Float64, -2.5d);
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Float2, new Float2Value(1f, 2f));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Float3, new Float3Value(1f, 2f, 3f));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Quaternion, new QuaternionValue(1f, 2f, 3f, 4f));
            var enumContract = StableHash.Fnv1A64("aibt.test.enum");
            AssertSharedBuiltIn(BuiltInBlackboardTypes.Enum32, new Enum32Value(enumContract, -3), enumContract);
            AssertSharedBuiltIn(BuiltInBlackboardTypes.FixedString32, new FixedString32Bytes("thirty-two"));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.FixedString64, new FixedString64Bytes("sixty-four"));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.FixedString128, new FixedString128Bytes("one-two-eight"));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.FixedString512, new FixedString512Bytes("five-one-two"));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.AgentId, new AgentId(9));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.EntityId, new EntityId(10));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.OperationId,
                new OperationId(new TreeInstanceId(11), new RuntimeNodeIndex(3), 4, 5));
            AssertSharedBuiltIn(BuiltInBlackboardTypes.AssetId, new AssetId(12, 13, 14, true));

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
            var registered = new SharedRegisteredFixture { Value = 7, Weight = 1.5f };
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateRegisteredBinding(
                "aibt.test.registered", "aibt.test.registered-schema",
                descriptor, registered, BlackboardScope.Shared, fields);
            AssertSharedRegistered(binding, registered);
        }

        [Test]
        public void OneSharedContextIsVisibleToMultipleTreesAndDifferentContextsAreIsolated()
        {
            using (var first = SharedScenario.Create())
            using (var second = SharedScenario.Create())
            using (var firstTree = first.Bind(2))
            using (var secondTree = first.Bind(8))
            using (var isolatedTree = second.Bind(4))
            {
                AssertRead(first, firstTree, 5, 0, 0);
                AssertRead(first, secondTree, 5, 0, 0);
                AssertRead(second, isolatedTree, 5, 0, 0);

                SetValue(first.Context, 9);
                AssertRead(first, firstTree, 9, 0, 0);
                AssertRead(second, isolatedTree, 5, 0, 0);
                Assert.That(first.Context.TryReset(out var failure), Is.True, failure.Code.ToString());
                AssertRead(first, firstTree, 5, 1, 1);
                AssertRead(first, secondTree, 5, 1, 1);
                AssertRead(second, isolatedTree, 5, 0, 0);

                Assert.That(first.Context.TryReset(out failure), Is.True, failure.Code.ToString());
                AssertRead(first, firstTree, 5, 1, 1);
                Assert.That(typeof(NativeSharedContextOwnerV1).GetProperty("AgentId"), Is.Null);
            }
        }

        [Test]
        public void BindingRequiresExactSharedAuthorityAndAllMutationsRequireQuiescence()
        {
            using (var scenario = SharedScenario.Create())
            using (var tree = scenario.Bind(2))
            {
                Assert.That(tree.Instance.TryAcquireExecutionLeaseV2(
                    scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryAcquireExecutionView(
                    tree.Binding, execution, out var view, out failure), Is.True, failure.Code.ToString());
                Assert.That(scenario.Context.TryReset(out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));
                Assert.That(scenario.Context.TryUnbind(tree.Binding, out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeLiveJobOwnershipViolation));

                var candidate = new NativeArray<int>(1, Allocator.TempJob);
                try
                {
                    candidate[0] = 9;
                    Assert.That(NativeSharedBlackboardV1.TryWrite(
                        view, 0, 0, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                        candidate, out var changed), Is.EqualTo(BurstContextResult.PhaseViolation));
                    Assert.That(changed, Is.False);
                }
                finally { candidate.Dispose(); }
                Assert.That(tree.Instance.TryReleaseExecutionLease(execution, out failure),
                    Is.True, failure.Code.ToString());
            }

            using (var expected = SharedScenario.Create())
            using (var other = SharedScenario.Create(BuiltInBlackboardTypes.Float32, 5f))
            {
                var wrongTree = other.CreateUnboundTree();
                try
                {
                    Assert.That(expected.Context.TryBind(
                        new TreeInstanceId(9), other.ProgramLease, wrongTree,
                        out _, out var failure), Is.False);
                    Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardRegistryMismatch));
                }
                finally
                {
                    Assert.That(wrongTree.TryDispose(out var failure), Is.True, failure.Code.ToString());
                }
            }
        }

        [Test]
        public void ResetNoOpAndVersionOrRevisionOverflowAreAtomic()
        {
            using (var scenario = SharedScenario.Create())
            {
                Assert.That(scenario.Context.TryReset(out var failure), Is.True, failure.Code.ToString());
                SetValue(scenario.Context, 9);
                SetVersion(scenario.Context, ulong.MaxValue);
                Assert.That(scenario.Context.TryReset(out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardVersionOverflow));
                AssertRawState(scenario.Context, 9, ulong.MaxValue, 0);

                SetVersion(scenario.Context, 0);
                SetRevision(scenario.Context, ulong.MaxValue);
                Assert.That(scenario.Context.TryReset(out failure), Is.False);
                Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.BlackboardVersionOverflow));
                AssertRawState(scenario.Context, 9, 0, ulong.MaxValue);

                SetValue(scenario.Context, 5);
                Assert.That(scenario.Context.TryReset(out failure), Is.True,
                    "an equal reset must not consume an already-max revision");
                AssertRawState(scenario.Context, 5, 0, ulong.MaxValue);
            }
        }

        [Test]
        public void CapacityDerivationAndEveryNativeAllocationRollbackBeforePublication()
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Shared);
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, failure.Code.ToString());
            try
            {
                Assert.That(NativeSharedContextCapacityV1.TryDerive(
                    lease.View, 2, 2, 8, 64, out var capacity, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(capacity.ValueBytes, Is.EqualTo(4));
                Assert.That(capacity.SlotVersions, Is.EqualTo((uint)lease.View.Slots.Length));

                var method = FindInjectedCreate();
                Assert.That(method, Is.Not.Null);
                for (var ordinal = 0; ordinal < 9; ordinal++)
                {
                    var arguments = new object[]
                    { lease, capacity, Allocator.Persistent, ordinal, null, default(NativeRuntimeFailureV1) };
                    Assert.That((bool)method.Invoke(null, arguments), Is.False, "allocation " + ordinal);
                    Assert.That(arguments[4], Is.Null);
                    Assert.That(((NativeRuntimeFailureV1)arguments[5]).Code,
                        Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeInstanceCapacityExceeded));
                }
            }
            finally
            {
                Assert.That(program.TryReleaseReadLease(lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        private static MethodInfo FindInjectedCreate()
        {
            foreach (var candidate in typeof(NativeSharedContextOwnerV1)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            {
                var parameters = candidate.GetParameters();
                if (candidate.Name == "TryCreate" && parameters.Length == 6
                    && parameters[3].ParameterType == typeof(int)) return candidate;
            }
            return null;
        }

        private static void AssertSharedBuiltIn<T>(
            BlackboardTypeDescriptor descriptor,
            T expected,
            ulong enumContract = 0)
            where T : unmanaged
        {
            var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                descriptor, expected, BlackboardScope.Shared, enumContract);
            AssertSharedDefault(
                binding,
                NativeBlackboardTypeIdV2.BuiltIn(descriptor, enumContract),
                expected);
        }

        private static void AssertSharedRegistered(
            NativeProgramBlackboardBindingV2 binding,
            SharedRegisteredFixture expected)
        {
            AssertSharedDefault(binding, default(NativeBlackboardTypeIdV2), expected, true);
        }

        private static void AssertSharedDefault<T>(
            NativeProgramBlackboardBindingV2 binding,
            NativeBlackboardTypeIdV2 expectedType,
            T expected,
            bool registered = false)
            where T : unmanaged
        {
            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var program, out var failure), Is.True, failure.Code.ToString());
            Assert.That(program.TryAcquireReadLeaseV2(out var programLease, out failure),
                Is.True, failure.Code.ToString());
            NativeSharedContextOwnerV1 context = null;
            NativeInstanceArenaOwnerV1 instance = null;
            NativeSharedBindingV1 sharedBinding = default;
            try
            {
                Assert.That(NativeSharedContextCapacityV1.TryDerive(
                    programLease.View, 1, 1, 1, (uint)Marshal.SizeOf<T>(),
                    out var sharedCapacity, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeSharedContextOwnerV1.TryCreate(
                    programLease, sharedCapacity, Allocator.Persistent, out context, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(NativeInstanceArenaCapacityV2.TryDerive(
                    programLease.View, out var instanceCapacity, out failure), Is.True, failure.Code.ToString());
                Assert.That(NativeInstanceArenaOwnerV1.TryCreateV2(
                    programLease, instanceCapacity, Allocator.Persistent, out instance, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(context.TryBind(
                    new TreeInstanceId(1), programLease, instance, out sharedBinding, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(instance.TryAcquireExecutionLeaseV2(
                    programLease, out var execution, out failure), Is.True, failure.Code.ToString());
                Assert.That(context.TryAcquireExecutionView(
                    sharedBinding, execution, out var view, out failure), Is.True, failure.Code.ToString());
                if (registered)
                    expectedType = NativeBlackboardTypeIdV2.Registered(0, execution.Program.RegisteredTypes[0]);
                Assert.That(NativeSharedBlackboardV1.TryRead(
                    view, 0, 0, expectedType, out T actual, out var version),
                    Is.EqualTo(BurstContextResult.Success));
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(version, Is.Zero);
                Assert.That(view.Context.Revision[0], Is.Zero);
                Assert.That(instance.TryReleaseExecutionLease(execution, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(context.TryUnbind(sharedBinding, out failure), Is.True, failure.Code.ToString());
                sharedBinding = default;
            }
            finally
            {
                if (context != null && sharedBinding.IsValid) context.TryUnbind(sharedBinding, out _);
                if (instance != null) Assert.That(instance.TryDispose(out failure), Is.True, failure.Code.ToString());
                if (context != null) Assert.That(context.TryDispose(out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryReleaseReadLease(programLease, out failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        private static void AssertRead(
            SharedScenario scenario,
            BoundTree tree,
            int expectedValue,
            ulong expectedVersion,
            ulong expectedRevision)
        {
            Assert.That(tree.Instance.TryAcquireExecutionLeaseV2(
                scenario.ProgramLease, out var execution, out var failure), Is.True, failure.Code.ToString());
            Assert.That(scenario.Context.TryAcquireExecutionView(
                tree.Binding, execution, out var view, out failure), Is.True, failure.Code.ToString());
            Assert.That(NativeSharedBlackboardV1.TryRead(
                view, 0, 0, NativeBlackboardTypeIdV2.BuiltIn(BuiltInBlackboardTypes.Int32),
                out int value, out var version), Is.EqualTo(BurstContextResult.Success));
            Assert.That(value, Is.EqualTo(expectedValue));
            Assert.That(version, Is.EqualTo(expectedVersion));
            Assert.That(view.Context.Revision[0], Is.EqualTo(expectedRevision));
            Assert.That(tree.Instance.TryReleaseExecutionLease(execution, out failure),
                Is.True, failure.Code.ToString());
        }

        private static void SetValue(NativeSharedContextOwnerV1 context, int value)
        {
            var bytes = GetArray<byte>(context, "_values");
            bytes[0] = (byte)value; bytes[1] = 0; bytes[2] = 0; bytes[3] = 0;
        }

        private static void SetVersion(NativeSharedContextOwnerV1 context, ulong value)
        {
            var values = GetArray<ulong>(context, "_versions");
            values[0] = value;
        }

        private static void SetRevision(NativeSharedContextOwnerV1 context, ulong value)
        {
            var values = GetArray<ulong>(context, "_revision");
            values[0] = value;
        }

        private static void AssertRawState(
            NativeSharedContextOwnerV1 context,
            int value,
            ulong version,
            ulong revision)
        {
            Assert.That(GetArray<byte>(context, "_values")[0], Is.EqualTo((byte)value));
            Assert.That(GetArray<ulong>(context, "_versions")[0], Is.EqualTo(version));
            Assert.That(GetArray<ulong>(context, "_revision")[0], Is.EqualTo(revision));
        }

        private static NativeArray<T> GetArray<T>(NativeSharedContextOwnerV1 context, string name)
            where T : struct
        {
            var field = typeof(NativeSharedContextOwnerV1).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeArray<T>)field.GetValue(context);
        }

        internal sealed class SharedScenario : IDisposable
        {
            private readonly List<BoundTree> _trees = new List<BoundTree>();

            private SharedScenario() { }

            internal NativeProgramImageOwnerV1 Program;
            internal NativeProgramReadLeaseV2 ProgramLease;
            internal NativeSharedContextOwnerV1 Context;

            internal static SharedScenario Create()
                => Create(BuiltInBlackboardTypes.Int32, 5);

            internal static SharedScenario Create<T>(BlackboardTypeDescriptor descriptor, T defaultValue)
                where T : unmanaged
            {
                var binding = NativeBlackboardProgramBindingTests.Fixture.CreateBinding(
                    descriptor, defaultValue, BlackboardScope.Shared);
                return Create(binding);
            }

            internal static SharedScenario Create(
                NativeProgramBlackboardBindingV2 binding,
                uint maximumBindings = 4,
                uint maximumSelected = 4,
                uint contributionRecords = 16,
                uint contributionPayloadBytes = 128)
            {
                Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                    binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                    out var program, out var failure), Is.True, failure.Code.ToString());
                Assert.That(program.TryAcquireReadLeaseV2(out var lease, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(NativeSharedContextCapacityV1.TryDerive(
                    lease.View, maximumBindings, maximumSelected,
                    contributionRecords, contributionPayloadBytes, out var capacity, out failure),
                    Is.True, failure.Code.ToString());
                Assert.That(NativeSharedContextOwnerV1.TryCreate(
                    lease, capacity, Allocator.Persistent, out var context, out failure),
                    Is.True, failure.Code.ToString());
                return new SharedScenario { Program = program, ProgramLease = lease, Context = context };
            }

            internal BoundTree Bind(ulong id)
            {
                var instance = CreateUnboundTree();
                Assert.That(Context.TryBind(
                    new TreeInstanceId(id), ProgramLease, instance, out var binding, out var failure),
                    Is.True, failure.Code.ToString());
                var tree = new BoundTree(Context, instance, binding);
                _trees.Add(tree);
                return tree;
            }

            internal NativeInstanceArenaOwnerV1 CreateUnboundTree()
            {
                Assert.That(NativeInstanceArenaCapacityV2.TryDerive(
                    ProgramLease.View, out var capacity, out var failure), Is.True, failure.Code.ToString());
                Assert.That(NativeInstanceArenaOwnerV1.TryCreateV2(
                    ProgramLease, capacity, Allocator.Persistent, out var instance, out failure),
                    Is.True, failure.Code.ToString());
                return instance;
            }

            public void Dispose()
            {
                for (var index = _trees.Count - 1; index >= 0; index--)
                {
                    _trees[index].Dispose();
                    _trees.RemoveAt(index);
                }
                Assert.That(Context.TryDispose(out var failure), Is.True, failure.Code.ToString());
                Assert.That(Program.TryReleaseReadLease(ProgramLease, out failure), Is.True, failure.Code.ToString());
                Assert.That(Program.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
        private struct SharedReadJob : IJob
        {
            internal NativeSharedExecutionViewV1 View;
            internal NativeBlackboardTypeIdV2 Type;
            internal NativeArray<BurstContextResult> Result;
            internal NativeArray<int> Value;
            public void Execute()
            {
                Result[0] = NativeSharedBlackboardV1.TryRead<int>(
                    View, 0, 0, Type,
                    out var value, out _);
                Value[0] = value;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SharedRegisteredFixture
        {
            internal int Value;
            internal float Weight;
        }

        internal sealed class BoundTree : IDisposable
        {
            private NativeSharedContextOwnerV1 _context;
            internal BoundTree(
                NativeSharedContextOwnerV1 context,
                NativeInstanceArenaOwnerV1 instance,
                NativeSharedBindingV1 binding)
            { _context = context; Instance = instance; Binding = binding; }

            internal NativeInstanceArenaOwnerV1 Instance { get; }
            internal NativeSharedBindingV1 Binding { get; }

            public void Dispose()
            {
                if (_context == null) return;
                Assert.That(_context.TryUnbind(Binding, out var failure), Is.True, failure.Code.ToString());
                Assert.That(Instance.TryDispose(out failure), Is.True, failure.Code.ToString());
                _context = null;
            }
        }
    }
}
