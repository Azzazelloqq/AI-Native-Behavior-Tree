using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class ReferenceBlackboardStorageTests
    {
        [Test]
        public void AllBuiltInDefaults_RoundTripThroughCanonicalStorage()
        {
            var values = new[]
            {
                BlackboardValue.FromBool(true),
                BlackboardValue.FromInt32(unchecked((int)0x89abcdef)),
                BlackboardValue.FromInt64(unchecked((long)0x89abcdef01234567)),
                BlackboardValue.FromFloat32(1.25f),
                BlackboardValue.FromFloat64(-2.5),
                BlackboardValue.FromFloat2(new Float2Value(1, -2)),
                BlackboardValue.FromFloat3(new Float3Value(1, -2, 3)),
                BlackboardValue.FromQuaternion(new QuaternionValue(1, -2, 3, -4)),
                BlackboardValue.FromEnum32(new Enum32Value(StableHash.Fnv1A64("game.state"), -7)),
                BlackboardValue.FromString32("Aé"),
                BlackboardValue.FromString64("Aé"),
                BlackboardValue.FromString128("Aé"),
                BlackboardValue.FromString512("Aé"),
                BlackboardValue.FromAgentId(new AgentId(11)),
                BlackboardValue.FromEntityId(new EntityId(12)),
                BlackboardValue.FromOperationId(new OperationId(new TreeInstanceId(13), new RuntimeNodeIndex(2), 3, 4)),
                BlackboardValue.FromAssetId(new AssetId(14, 15, -16, true)),
            };

            foreach (var expected in values)
            {
                var fixture = Fixture.BuiltIn(expected, read: true, write: true);
                Assert.That(fixture.Create(out var storage, out var diagnostic), Is.True, expected.Type.ToString());
                var read = storage.TryRead(new RuntimeNodeIndex(0), 0, out var actual);

                Assert.That(diagnostic, Is.Null, expected.Type.ToString());
                Assert.That(read.Success, Is.True, expected.Type.ToString());
                Assert.That(actual, Is.EqualTo(expected), expected.Type.ToString());
            }
        }

        [Test]
        public void Creation_CopiesDefaultAndStartsVersionsAtZero()
        {
            var fixture = Fixture.BuiltIn(BlackboardValue.FromInt32(12), read: true, write: true);
            Assert.That(fixture.Create(out var storage, out var diagnostic), Is.True);

            var read = storage.TryRead(new RuntimeNodeIndex(0), 0, out var value);
            value.TryGetInt32(out var number);

            Assert.That(diagnostic, Is.Null);
            Assert.That(read.Success, Is.True);
            Assert.That(number, Is.EqualTo(12));
            Assert.That(storage.GetSlotVersion(0), Is.Zero);
            Assert.That(storage.Revision, Is.Zero);
        }

        [Test]
        public void EqualWrite_DoesNotAdvanceVersions_ChangedWriteIsImmediatelyVisible()
        {
            var fixture = Fixture.BuiltIn(BlackboardValue.FromInt32(12), read: true, write: true);
            fixture.Create(out var storage, out _);

            var equal = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromInt32(12));
            var changed = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromInt32(99));
            var read = storage.TryRead(new RuntimeNodeIndex(0), 0, out var value);
            value.TryGetInt32(out var number);

            Assert.That(equal.Success, Is.True);
            Assert.That(equal.Changed, Is.False);
            Assert.That(changed.Changed, Is.True);
            Assert.That(changed.SlotIndex, Is.Zero);
            Assert.That(changed.StableKeyId, Is.EqualTo(1));
            Assert.That(changed.OldVersion, Is.Zero);
            Assert.That(changed.NewVersion, Is.EqualTo(1));
            Assert.That(read.Success, Is.True);
            Assert.That(number, Is.EqualTo(99));
            Assert.That(storage.GetSlotVersion(0), Is.EqualTo(1));
            Assert.That(storage.Revision, Is.EqualTo(1));
        }

        [Test]
        public void FloatNegativeZero_IsCanonicalAndEqualToPositiveZero()
        {
            var fixture = Fixture.BuiltIn(BlackboardValue.FromFloat32(-0f), read: true, write: true);
            fixture.Create(out var storage, out _);

            var result = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromFloat32(+0f));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Changed, Is.False);
            Assert.That(storage.GetSlotVersion(0), Is.Zero);
        }

        [Test]
        public void Enum32_RequiresExactCompiledContractForDefaultsAndWrites()
        {
            var contract = StableHash.Fnv1A64("game.state");
            var otherContract = StableHash.Fnv1A64("game.other-state");
            var value = BlackboardValue.FromEnum32(new Enum32Value(contract, 1));
            var invalidDefault = Fixture.BuiltIn(value, true, true, enumContractId: otherContract);

            Assert.That(invalidDefault.Create(out _, out var defaultDiagnostic), Is.False);
            AssertError(defaultDiagnostic, BlackboardStorageDiagnosticCodes.InvalidValue);

            var valid = Fixture.BuiltIn(value, true, true);
            valid.Create(out var storage, out _);
            var write = storage.TryWrite(
                new RuntimeNodeIndex(0),
                0,
                BlackboardValue.FromEnum32(new Enum32Value(otherContract, 2)));
            storage.TryRead(new RuntimeNodeIndex(0), 0, out var after);

            Assert.That(write.Success, Is.False);
            AssertError(write.Diagnostic, BlackboardStorageDiagnosticCodes.TypeMismatch);
            Assert.That(after, Is.EqualTo(value));
            Assert.That(storage.GetSlotVersion(0), Is.Zero);
            Assert.That(storage.Revision, Is.Zero);
        }

        [Test]
        public void UndeclaredAndTypeMismatchedAccess_ReturnStructuredDiagnostics()
        {
            var noAccess = Fixture.BuiltIn(BlackboardValue.FromInt32(1), read: false, write: false);
            noAccess.Create(out var storage, out _);

            var read = storage.TryRead(new RuntimeNodeIndex(0), 0, out _);
            var write = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromFloat32(1));

            Assert.That(read.Diagnostic.Code, Is.EqualTo(BlackboardStorageDiagnosticCodes.UndeclaredAccess));
            Assert.That(write.Diagnostic.Code, Is.EqualTo(BlackboardStorageDiagnosticCodes.UndeclaredAccess));

            var writable = Fixture.BuiltIn(BlackboardValue.FromInt32(1), read: true, write: true);
            writable.Create(out storage, out _);
            var wrongType = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromFloat32(1));
            Assert.That(wrongType.Diagnostic.Code, Is.EqualTo(BlackboardStorageDiagnosticCodes.TypeMismatch));

            var invalidNode = storage.TryRead(new RuntimeNodeIndex(1), 0, out _);
            var undeclaredOrdinal = storage.TryRead(new RuntimeNodeIndex(0), 1, out _);
            AssertError(invalidNode.Diagnostic, BlackboardStorageDiagnosticCodes.InvalidSlot);
            AssertError(undeclaredOrdinal.Diagnostic, BlackboardStorageDiagnosticCodes.UndeclaredAccess);
        }

        [Test]
        public void DeclaredOrdinals_MapThroughEachNodesCompiledAccessTables()
        {
            var fixture = Fixture.TwoNodeOrdinalMapping();
            fixture.Create(out var storage, out _);

            storage.TryRead(new RuntimeNodeIndex(0), 0, out var nodeZeroValue);
            storage.TryRead(new RuntimeNodeIndex(1), 0, out var nodeOneValue);
            var write = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromInt32(20));
            storage.TryRead(new RuntimeNodeIndex(1), 0, out var unchangedNodeOneValue);

            Assert.That(nodeZeroValue, Is.EqualTo(BlackboardValue.FromInt32(2)));
            Assert.That(nodeOneValue, Is.EqualTo(BlackboardValue.FromInt32(1)));
            Assert.That(write.SlotIndex, Is.EqualTo(1));
            Assert.That(write.StableKeyId, Is.EqualTo(5));
            Assert.That(unchangedNodeOneValue, Is.EqualTo(BlackboardValue.FromInt32(1)));
        }

        [Test]
        public void Creation_RejectsInvalidBuiltInBytesAndUnsupportedScope()
        {
            var invalidBool = Fixture.BuiltInBytes(BuiltInBlackboardTypes.Bool, new byte[] { 2 });
            Assert.That(invalidBool.Create(out _, out var valueDiagnostic), Is.False);
            AssertError(valueDiagnostic, BlackboardStorageDiagnosticCodes.InvalidValue);

            var agent = Fixture.BuiltIn(
                BlackboardValue.FromInt32(1), true, true, scope: BlackboardScope.Agent);
            Assert.That(agent.Create(out _, out var scopeDiagnostic), Is.False);
            AssertError(scopeDiagnostic, BlackboardStorageDiagnosticCodes.UnsupportedScope);
        }

        [Test]
        public void Creation_RejectsNonCanonicalFloatVectorAndAssetBytes()
        {
            var negativeZero = new byte[] { 0, 0, 0, 0x80 };
            var floatFixture = Fixture.BuiltInBytes(BuiltInBlackboardTypes.Float32, negativeZero);
            Assert.That(floatFixture.Create(out _, out var floatDiagnostic), Is.False);
            AssertError(floatDiagnostic, BlackboardStorageDiagnosticCodes.InvalidValue);

            var vectorBytes = CompiledBlackboardValueEncoder.Encode(
                BlackboardValue.FromFloat2(new Float2Value(1, 0)));
            vectorBytes[7] = 0x80;
            var vectorFixture = Fixture.BuiltInBytes(BuiltInBlackboardTypes.Float2, vectorBytes);
            Assert.That(vectorFixture.Create(out _, out var vectorDiagnostic), Is.False);
            AssertError(vectorDiagnostic, BlackboardStorageDiagnosticCodes.InvalidValue);

            var assetBytes = CompiledBlackboardValueEncoder.Encode(
                BlackboardValue.FromAssetId(new AssetId(1, 2)));
            assetBytes[16] = 7;
            var assetFixture = Fixture.BuiltInBytes(BuiltInBlackboardTypes.AssetId, assetBytes);
            Assert.That(assetFixture.Create(out _, out var assetDiagnostic), Is.False);
            AssertError(assetDiagnostic, BlackboardStorageDiagnosticCodes.InvalidValue);
        }

        [Test]
        public void ArenaSize_IncludesDeterministicSlotOffsetGaps()
        {
            var fixture = Fixture.BuiltInAtOffset(BlackboardValue.FromInt32(7), 8);
            fixture.Create(out var storage, out _);

            var read = storage.TryRead(new RuntimeNodeIndex(0), 0, out var value);

            Assert.That(storage.ArenaSize, Is.EqualTo(12));
            Assert.That(read.Success, Is.True);
            Assert.That(value, Is.EqualTo(BlackboardValue.FromInt32(7)));
        }

        [Test]
        public void Reset_RestoresDefaultsInSlotOrderAndAdvancesChangedSlotAndTreeVersions()
        {
            var fixture = Fixture.TwoInt32Slots(1, 2);
            fixture.Create(out var storage, out _);
            storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromInt32(10));
            storage.TryWrite(new RuntimeNodeIndex(0), 1, BlackboardValue.FromInt32(20));

            var reset = storage.Reset();

            Assert.That(reset.Success, Is.True);
            Assert.That(reset.Changed, Is.True);
            Assert.That(reset.Changes.Count, Is.EqualTo(2));
            Assert.That(reset.Changes[0].SlotIndex, Is.Zero);
            Assert.That(reset.Changes[0].StableKeyId, Is.EqualTo(1));
            Assert.That(reset.Changes[0].OldVersion, Is.EqualTo(1));
            Assert.That(reset.Changes[0].NewVersion, Is.EqualTo(2));
            Assert.That(reset.Changes[1].SlotIndex, Is.EqualTo(1));
            Assert.That(reset.Changes[1].StableKeyId, Is.EqualTo(5));
            Assert.That(storage.GetSlotVersion(0), Is.EqualTo(2));
            Assert.That(storage.GetSlotVersion(1), Is.EqualTo(2));
            Assert.That(storage.Revision, Is.EqualTo(3));
            storage.TryRead(new RuntimeNodeIndex(0), 0, out var first);
            storage.TryRead(new RuntimeNodeIndex(0), 1, out var second);
            first.TryGetInt32(out var firstValue);
            second.TryGetInt32(out var secondValue);
            Assert.That(firstValue, Is.EqualTo(1));
            Assert.That(secondValue, Is.EqualTo(2));
        }

        [Test]
        public void ResetWithoutChanges_IsNoOp()
        {
            var fixture = Fixture.BuiltIn(BlackboardValue.FromInt32(1), read: true, write: true);
            fixture.Create(out var storage, out _);

            var result = storage.Reset();

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Changes, Is.Empty);
            Assert.That(storage.Revision, Is.Zero);
            Assert.That(storage.GetSlotVersion(0), Is.Zero);
        }

        [Test]
        public void VersionOverflow_RejectsChangedWriteAtomically()
        {
            var fixture = Fixture.BuiltIn(BlackboardValue.FromInt32(1), true, true);
            fixture.Create(out var storage, out _);
            typeof(ReferenceBlackboardStorage)
                .GetField("<Revision>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(storage, ulong.MaxValue);

            var write = storage.TryWrite(new RuntimeNodeIndex(0), 0, BlackboardValue.FromInt32(2));
            storage.TryRead(new RuntimeNodeIndex(0), 0, out var after);

            Assert.That(write.Success, Is.False);
            AssertError(write.Diagnostic, BlackboardStorageDiagnosticCodes.VersionOverflow);
            Assert.That(after, Is.EqualTo(BlackboardValue.FromInt32(1)));
            Assert.That(storage.GetSlotVersion(0), Is.Zero);
        }

        [Test]
        public void RegisteredBinding_UsesDeclaredEqualityAndDefensiveCopies()
        {
            var descriptor = new RegisteredUnmanagedTypeDescriptor(900, 2, 4, 4, 901);
            var registry = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(descriptor, (left, right) => left[0] == right[0]),
            });
            var fixture = Fixture.Registered(descriptor, new byte[] { 1, 2, 3, 4 }, registry);
            fixture.Create(out var storage, out _);
            var equalByContract = new byte[] { 1, 9, 9, 9 };

            var unchanged = storage.TryWriteRegistered(new RuntimeNodeIndex(0), 0, 900, 2, equalByContract);
            equalByContract[0] = 7;
            var changed = storage.TryWriteRegistered(new RuntimeNodeIndex(0), 0, 900, 2, new byte[] { 2, 4, 5, 6 });
            var read = storage.TryReadRegistered(new RuntimeNodeIndex(0), 0, 900, 2, out var bytes);
            bytes[0] = 99;
            storage.TryReadRegistered(new RuntimeNodeIndex(0), 0, 900, 2, out var reread);

            Assert.That(unchanged.Changed, Is.False);
            Assert.That(changed.Changed, Is.True);
            Assert.That(read.Success, Is.True);
            CollectionAssert.AreEqual(new byte[] { 2, 4, 5, 6 }, reread);
        }

        [Test]
        public void InitialValues_AreAppliedAtomicallyWithZeroVersions_AndSnapshotUsesSlotOrder()
        {
            var fixture = Fixture.TwoInt32Slots(1, 2);
            var initial = new[]
            {
                ReferenceBlackboardInitialValue.BuiltIn(5, BlackboardValue.FromInt32(20)),
                ReferenceBlackboardInitialValue.BuiltIn(1, BlackboardValue.FromInt32(10)),
            };

            Assert.That(fixture.Create(out var storage, out _, initialValues: initial), Is.True);

            var snapshot = storage.CaptureSnapshot();
            Assert.That(snapshot.Revision, Is.Zero);
            Assert.That(snapshot.Entries.Select(item => item.SlotIndex), Is.EqualTo(new uint[] { 0, 1 }));
            Assert.That(snapshot.Entries.Select(item => item.StableKeyId), Is.EqualTo(new ulong[] { 1, 5 }));
            Assert.That(snapshot.Entries.All(item => item.Version == 0), Is.True);
            Assert.That(snapshot.Entries[0].BuiltInValue.TryGetInt32(out var first), Is.True);
            Assert.That(snapshot.Entries[1].BuiltInValue.TryGetInt32(out var second), Is.True);
            Assert.That(first, Is.EqualTo(10));
            Assert.That(second, Is.EqualTo(20));

            var reset = storage.Reset();
            var afterReset = storage.CaptureSnapshot();
            Assert.That(reset.Changes.Select(item => item.SlotIndex), Is.EqualTo(new uint[] { 0, 1 }));
            Assert.That(afterReset.Revision, Is.EqualTo(1));
            Assert.That(afterReset.Entries.All(item => item.Version == 1), Is.True);
            Assert.That(afterReset.Entries[0].BuiltInValue.TryGetInt32(out first), Is.True);
            Assert.That(afterReset.Entries[1].BuiltInValue.TryGetInt32(out second), Is.True);
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
        }

        [Test]
        public void InvalidOrDuplicateInitialValues_RejectCreationWithoutPublishingStorage()
        {
            var fixture = Fixture.TwoInt32Slots(1, 2);
            var missing = new[]
            {
                ReferenceBlackboardInitialValue.BuiltIn(1, BlackboardValue.FromInt32(10)),
                ReferenceBlackboardInitialValue.BuiltIn(99, BlackboardValue.FromInt32(20)),
            };
            var duplicate = new[]
            {
                ReferenceBlackboardInitialValue.BuiltIn(1, BlackboardValue.FromInt32(10)),
                ReferenceBlackboardInitialValue.BuiltIn(1, BlackboardValue.FromInt32(20)),
            };
            var mismatched = new[]
            {
                ReferenceBlackboardInitialValue.BuiltIn(1, BlackboardValue.FromBool(true)),
            };

            Assert.That(fixture.Create(out var missingStorage, out var missingDiagnostic, initialValues: missing), Is.False);
            Assert.That(missingStorage, Is.Null);
            AssertError(missingDiagnostic, BlackboardStorageDiagnosticCodes.InvalidSlot);
            Assert.That(fixture.Create(out var duplicateStorage, out var duplicateDiagnostic, initialValues: duplicate), Is.False);
            Assert.That(duplicateStorage, Is.Null);
            AssertError(duplicateDiagnostic, BlackboardStorageDiagnosticCodes.InvalidSlot);
            Assert.That(fixture.Create(out var mismatchedStorage, out var mismatchDiagnostic, initialValues: mismatched), Is.False);
            Assert.That(mismatchedStorage, Is.Null);
            AssertError(mismatchDiagnostic, BlackboardStorageDiagnosticCodes.TypeMismatch);
        }

        [Test]
        public void RegisteredInitialValueAndSnapshot_DefensivelyCopyBytes()
        {
            var descriptor = new RegisteredUnmanagedTypeDescriptor(900, 2, 4, 4, 99);
            var registry = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(descriptor, (left, right) => left.SequenceEqual(right)),
            });
            var fixture = Fixture.Registered(descriptor, new byte[] { 1, 2, 3, 4 }, registry);
            var source = new byte[] { 5, 6, 7, 8 };
            var initial = ReferenceBlackboardInitialValue.Registered(1, 900, 2, source);
            source[0] = 99;

            Assert.That(fixture.Create(out var storage, out _, initialValues: new[] { initial }), Is.True);
            var snapshot = storage.CaptureSnapshot();
            var observed = snapshot.Entries[0].CopyRegisteredBytes();
            observed[1] = 99;
            var repeated = storage.CaptureSnapshot();

            Assert.That(snapshot.Entries[0].IsRegistered, Is.True);
            Assert.That(snapshot.Entries[0].Type, Is.EqualTo(BlackboardTypeDescriptor.FromRegistered(descriptor)));
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7, 8 }, repeated.Entries[0].CopyRegisteredBytes());
            Assert.That(repeated.Entries[0].Version, Is.Zero);
        }

        [Test]
        public void InitialEnumValue_RequiresExactCompiledContract()
        {
            var expectedContract = StableHash.Fnv1A64("game.state");
            var fixture = Fixture.BuiltIn(
                BlackboardValue.FromEnum32(new Enum32Value(expectedContract, 0)),
                read: true,
                write: true,
                enumContractId: expectedContract);
            var initial = new[]
            {
                ReferenceBlackboardInitialValue.BuiltIn(
                    1,
                    BlackboardValue.FromEnum32(new Enum32Value(StableHash.Fnv1A64("game.other"), 1))),
            };

            Assert.That(fixture.Create(out var storage, out var diagnostic, initialValues: initial), Is.False);
            Assert.That(storage, Is.Null);
            AssertError(diagnostic, BlackboardStorageDiagnosticCodes.TypeMismatch);
        }

        [Test]
        public void RegisteredEqualityFault_WriteAndResetReturnDiagnosticsWithoutMutation()
        {
            var descriptor = new RegisteredUnmanagedTypeDescriptor(900, 2, 4, 4, 901);
            var throwEquality = true;
            var registry = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(descriptor, (left, right) =>
                {
                    if (throwEquality) throw new InvalidOperationException("test equality fault");
                    return left.SequenceEqual(right);
                }),
            });
            var fixture = Fixture.Registered(descriptor, new byte[] { 1, 2, 3, 4 }, registry);
            fixture.Create(out var storage, out _);

            var writeFault = storage.TryWriteRegistered(
                new RuntimeNodeIndex(0), 0, 900, 2, new byte[] { 2, 3, 4, 5 });
            AssertError(writeFault.Diagnostic, BlackboardStorageDiagnosticCodes.EqualityFault);
            storage.TryReadRegistered(new RuntimeNodeIndex(0), 0, 900, 2, out var afterWriteFault);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, afterWriteFault);
            Assert.That(storage.GetSlotVersion(0), Is.Zero);
            Assert.That(storage.Revision, Is.Zero);

            throwEquality = false;
            Assert.That(storage.TryWriteRegistered(
                new RuntimeNodeIndex(0), 0, 900, 2, new byte[] { 2, 3, 4, 5 }).Changed, Is.True);
            throwEquality = true;

            var resetFault = storage.Reset();
            Assert.That(resetFault.Success, Is.False);
            AssertError(resetFault.Diagnostic, BlackboardStorageDiagnosticCodes.EqualityFault);
            storage.TryReadRegistered(new RuntimeNodeIndex(0), 0, 900, 2, out var afterResetFault);
            CollectionAssert.AreEqual(new byte[] { 2, 3, 4, 5 }, afterResetFault);
            Assert.That(storage.GetSlotVersion(0), Is.EqualTo(1));
            Assert.That(storage.Revision, Is.EqualTo(1));
        }

        [Test]
        public void MissingRegisteredBindingAndRegistryHashMismatch_AreDiagnostics()
        {
            var descriptor = new RegisteredUnmanagedTypeDescriptor(900, 2, 4, 4, 901);
            var goodRegistry = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(descriptor, (left, right) => left.SequenceEqual(right)),
            });
            var missing = Fixture.Registered(descriptor, new byte[4], RegisteredBlackboardRegistry.Empty);

            Assert.That(missing.Create(out _, out var missingDiagnostic), Is.False);
            Assert.That(missingDiagnostic.Code, Is.EqualTo(BlackboardStorageDiagnosticCodes.MissingTypeBinding));

            var fixture = Fixture.Registered(descriptor, new byte[4], goodRegistry);
            Assert.That(fixture.Create(out _, out var hashDiagnostic, new CompiledHash(new string('f', 64))), Is.False);
            Assert.That(hashDiagnostic.Code, Is.EqualTo(BlackboardStorageDiagnosticCodes.RegistryMismatch));
        }

        [Test]
        public void RegisteredRegistryHash_IsStableAcrossInputOrderAndIncludesEqualityContract()
        {
            var a = new RegisteredUnmanagedTypeDescriptor(10, 1, 4, 4, 100);
            var b = new RegisteredUnmanagedTypeDescriptor(20, 1, 8, 8, 200);
            RegisteredBlackboardEquality equality = (left, right) => left.SequenceEqual(right);

            var first = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(a, equality),
                new RegisteredBlackboardBinding(b, equality),
            });
            var reversed = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(b, equality),
                new RegisteredBlackboardBinding(a, equality),
            });
            var changedContract = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(new RegisteredUnmanagedTypeDescriptor(10, 1, 4, 4, 101), equality),
                new RegisteredBlackboardBinding(b, equality),
            });

            Assert.That(first.Hash, Is.EqualTo(reversed.Hash));
            Assert.That(changedContract.Hash, Is.Not.EqualTo(first.Hash));

            var changedSchema = new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(new RegisteredUnmanagedTypeDescriptor(10, 1, 4, 4, 100, 77), equality),
                new RegisteredBlackboardBinding(b, equality),
            });
            Assert.That(changedSchema.Hash, Is.Not.EqualTo(first.Hash));
        }

        [Test]
        public void RegisteredRegistry_RejectsBuiltInTypeIdentityCollision()
        {
            var builtIn = BuiltInBlackboardTypes.Int32;
            var collision = new RegisteredUnmanagedTypeDescriptor(
                builtIn.TypeId,
                builtIn.Version,
                builtIn.Size,
                builtIn.Alignment,
                999);

            Assert.Throws<ArgumentException>(() => new RegisteredBlackboardRegistry(new[]
            {
                new RegisteredBlackboardBinding(collision, (left, right) => left.SequenceEqual(right)),
            }));
        }

        private static void AssertError(Diagnostic diagnostic, DiagnosticCode code)
        {
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Code, Is.EqualTo(code));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.Location.TreeInstanceId, Is.EqualTo(new TreeInstanceId(55)));
        }

        private sealed class Fixture
        {
            private static readonly CompiledHash Hash = new CompiledHash(new string('c', 64));
            private readonly CompiledProgram _program;
            private readonly RegisteredBlackboardRegistry _registry;

            private Fixture(CompiledProgram program, RegisteredBlackboardRegistry registry)
            {
                _program = program;
                _registry = registry;
            }

            internal bool Create(
                out ReferenceBlackboardStorage storage,
                out Diagnostic diagnostic,
                CompiledHash expected = default,
                IReadOnlyList<ReferenceBlackboardInitialValue> initialValues = null)
                => ReferenceBlackboardStorage.TryCreate(
                    _program,
                    new TreeInstanceId(55),
                    _registry,
                    out storage,
                    out diagnostic,
                    expected,
                    initialValues);

            internal static Fixture BuiltIn(
                BlackboardValue defaultValue,
                bool read,
                bool write,
                ulong enumContractId = 0,
                BlackboardScope scope = BlackboardScope.Tree)
            {
                BuiltInBlackboardTypes.TryGet(defaultValue.Type, out var descriptor);
                if (enumContractId == 0 && defaultValue.TryGetEnum32(out var enumValue))
                {
                    enumContractId = enumValue.ContractTypeId;
                }

                return Create(
                    new[] { Slot(descriptor, 0, 0, read, write, enumContractId, scope) },
                    CompiledBlackboardValueEncoder.Encode(defaultValue),
                    read ? new uint[] { 0 } : Array.Empty<uint>(),
                    write ? new uint[] { 0 } : Array.Empty<uint>(),
                    RegisteredBlackboardRegistry.Empty);
            }

            internal static Fixture BuiltInBytes(BlackboardTypeDescriptor descriptor, byte[] defaults)
            {
                return Create(
                    new[] { Slot(descriptor, 0, 0, true, true) },
                    defaults,
                    new uint[] { 0 },
                    new uint[] { 0 },
                    RegisteredBlackboardRegistry.Empty);
            }

            internal static Fixture BuiltInAtOffset(BlackboardValue defaultValue, uint offset)
            {
                BuiltInBlackboardTypes.TryGet(defaultValue.Type, out var descriptor);
                return Create(
                    new[] { Slot(descriptor, offset, 0, true, true) },
                    CompiledBlackboardValueEncoder.Encode(defaultValue),
                    new uint[] { 0 },
                    new uint[] { 0 },
                    RegisteredBlackboardRegistry.Empty);
            }

            internal static Fixture TwoInt32Slots(int first, int second)
            {
                var descriptor = BuiltInBlackboardTypes.Int32;
                var defaults = new byte[8];
                Array.Copy(CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromInt32(first)), 0, defaults, 0, 4);
                Array.Copy(CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromInt32(second)), 0, defaults, 4, 4);
                return Create(
                    new[] { Slot(descriptor, 0, 0, true, true), Slot(descriptor, 4, 4, true, true) },
                    defaults,
                    new uint[] { 0, 1 },
                    new uint[] { 0, 1 },
                    RegisteredBlackboardRegistry.Empty);
            }

            internal static Fixture TwoNodeOrdinalMapping()
            {
                var descriptor = BuiltInBlackboardTypes.Int32;
                var defaults = new byte[8];
                Array.Copy(CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromInt32(1)), 0, defaults, 0, 4);
                Array.Copy(CompiledBlackboardValueEncoder.Encode(BlackboardValue.FromInt32(2)), 0, defaults, 4, 4);
                var nodes = new[]
                {
                    Node(new CompiledRange(0, 1), new CompiledRange(0, 1), new CompiledRange(0, 1)),
                    Node(new CompiledRange(0, 0), new CompiledRange(1, 1), new CompiledRange(1, 1)),
                };
                return Create(
                    new[] { Slot(descriptor, 0, 0, true, true), Slot(descriptor, 4, 4, true, true) },
                    defaults,
                    new uint[] { 1, 0 },
                    new uint[] { 1, 0 },
                    RegisteredBlackboardRegistry.Empty,
                    nodes,
                    new uint[] { 1 });
            }

            internal static Fixture Registered(
                RegisteredUnmanagedTypeDescriptor descriptor,
                byte[] defaults,
                RegisteredBlackboardRegistry registry)
            {
                var slot = new CompiledBlackboardSlotRecord(
                    1, descriptor.TypeId, descriptor.Version, 0, BlackboardScope.Tree,
                    0, (uint)descriptor.Size, (uint)descriptor.Alignment, 0,
                    CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write);
                return Create(new[] { slot }, defaults, new uint[] { 0 }, new uint[] { 0 }, registry);
            }

            private static CompiledBlackboardSlotRecord Slot(
                BlackboardTypeDescriptor descriptor,
                uint offset,
                uint defaultOffset,
                bool read,
                bool write,
                ulong enumContractId = 0,
                BlackboardScope scope = BlackboardScope.Tree)
            {
                var flags = CompiledBlackboardAccessFlags.None;
                if (read) flags |= CompiledBlackboardAccessFlags.Read;
                if (write) flags |= CompiledBlackboardAccessFlags.Write;
                return new CompiledBlackboardSlotRecord(
                    offset + 1, descriptor.TypeId, descriptor.Version, enumContractId, scope,
                    offset, (uint)descriptor.Size, (uint)descriptor.Alignment, defaultOffset, flags);
            }

            private static Fixture Create(
                CompiledBlackboardSlotRecord[] slots,
                byte[] defaults,
                uint[] reads,
                uint[] writes,
                RegisteredBlackboardRegistry registry,
                CompiledNodeRecord[] nodes = null,
                uint[] childIndices = null)
            {
                nodes = nodes ?? new[] { Node(
                    new CompiledRange(0, 0),
                    new CompiledRange(0, (uint)reads.Length),
                    new CompiledRange(0, (uint)writes.Length)) };
                childIndices = childIndices ?? Array.Empty<uint>();
                var header = new CompiledProgramHeader(
                    1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                    Hash, Hash, Hash, 1, Hash,
                    0, (uint)nodes.Length, (uint)childIndices.Length, (uint)slots.Length,
                    0, 0, 0, 1, 0, true);
                var program = new CompiledProgram(
                    header, nodes, childIndices, reads, writes, slots,
                    Array.Empty<CompiledObserverRecord>(), Array.Empty<uint>(),
                    Array.Empty<byte>(), defaults, Array.Empty<CompiledDebugMapEntry>());
                return new Fixture(program, registry);
            }

            private static CompiledNodeRecord Node(
                CompiledRange children,
                CompiledRange reads,
                CompiledRange writes)
            {
                return new CompiledNodeRecord(
                    1, 1, 0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                    children, CompiledNodeFlags.BurstDomain,
                    CompiledIndex.Invalid,
                    reads,
                    writes);
            }
        }
    }
}
