using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace AIBT.Tests.Runtime.NativeExecution.Blackboard.TreeAndAgent
{
    public sealed class NativeBlackboardProgramBindingTests
    {
        [Test]
        public void V2Binder_ProjectsExactScopeSlotAccessAndOuterHashWithoutChangingInnerHash()
        {
            var binding = Fixture.CreateBinding();
            var capacity = NativeProgramImageCapacityV2.Exact(binding);

            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding,
                capacity,
                Allocator.Persistent,
                out var owner,
                out var failure), Is.True, failure.Code.ToString());

            try
            {
                Assert.That(owner.TryAcquireReadLeaseV2(out var lease, out failure), Is.True, failure.Code.ToString());
                Assert.That(lease.View.Header.CompiledContentHash, Is.EqualTo(new NativeHash256V1(binding.OuterContentHash)));
                Assert.That(lease.View.Semantic.Header.CompiledContentHash,
                    Is.EqualTo(new NativeHash256V1(binding.SemanticProgram.Header.CompiledContentHash)));
                Assert.That(lease.View.Scopes.Length, Is.Zero);
                Assert.That(lease.View.Slots.Length, Is.EqualTo(1));
                Assert.That(lease.View.Slots[0].StableKeyId, Is.EqualTo(StableHash.Fnv1A64("health")));
                Assert.That(lease.View.Accesses.Length, Is.EqualTo(1));
                Assert.That(lease.View.Accesses[0].AccessOrdinal, Is.Zero);
                Assert.That(lease.View.NodeAccessRanges[0].Count, Is.EqualTo(1));
                Assert.That(owner.TryReleaseReadLease(lease, out failure), Is.True, failure.Code.ToString());
            }
            finally
            {
                Assert.That(owner.TryDispose(out failure), Is.True, failure.Code.ToString());
            }
        }

        [Test]
        public void V2Binder_RejectsInsufficientAccessCapacityBeforePublication()
        {
            var binding = Fixture.CreateBinding();
            var exact = NativeProgramImageCapacityV2.Exact(binding);
            var insufficient = new NativeProgramImageCapacityV2(
                exact.Semantic,
                exact.ScopeDescriptors,
                exact.ScopeLayoutBytes,
                exact.Slots,
                0,
                exact.NodeAccessRanges,
                exact.RegisteredTypes,
                exact.RegisteredFields);

            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, insufficient, Allocator.Persistent, out var owner, out var failure), Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.Code, Is.EqualTo(NativeRuntimeDiagnosticCodeV1.NativeProgramCapacityExceeded));
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.ProgramBlackboardAccesses));
        }

        [Test]
        public void V2Binder_RejectsCallerSuppliedOuterAndScopeHashesWithoutCanonicalByteAuthority()
        {
            var valid = Fixture.CreateBinding(BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Agent);
            var corruptedOuterBytes = valid.GetOuterPreimageCopy();
            corruptedOuterBytes[corruptedOuterBytes.Length - 1] ^= 1;
            var forgedOuter = new NativeProgramBlackboardBindingV2(
                valid.SemanticProgram,
                corruptedOuterBytes,
                valid.Scopes,
                valid.Slots,
                valid.SlotAuthorities,
                valid.Accesses,
                valid.WatchedSlots,
                valid.RegisteredTypes,
                valid.RegisteredFields);

            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                forgedOuter, NativeProgramImageCapacityV2.Exact(forgedOuter), Allocator.Persistent,
                out var owner, out var failure), Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.ProgramHash));

            var source = valid.Scopes[0];
            var corruptLayout = source.GetRawLayoutCopy();
            corruptLayout[corruptLayout.Length - 1] = 0xff;
            var forgedScope = new NativeBlackboardScopeBindingV2(
                source.Scope, source.ContractId, source.ContractVersion,
                source.FirstSlot, source.SlotCount,
                source.GetSchemaBytesCopy(), corruptLayout);
            var forgedScopes = new[] { forgedScope };
            var forgedLayout = new NativeProgramBlackboardBindingV2(
                valid.SemanticProgram,
                Fixture.OuterBytes(valid.SemanticProgram, forgedScopes, valid.Slots, valid.SlotAuthorities, valid.Accesses),
                forgedScopes,
                valid.Slots,
                valid.SlotAuthorities,
                valid.Accesses,
                valid.WatchedSlots,
                valid.RegisteredTypes,
                valid.RegisteredFields);

            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                forgedLayout, NativeProgramImageCapacityV2.Exact(forgedLayout), Allocator.Persistent,
                out owner, out failure), Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.ProgramScopeDescriptors));
        }

        [Test]
        public void V2Binder_RejectsRegisteredFieldEncodingThatDoesNotMatchItsExactValueType()
        {
            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.registered"), 1, 4, 4,
                Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.registered"));
            var forged = new NativeRegisteredBlackboardFieldBindingV2(
                StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1,
                0, 4, 4, NativeBlackboardFieldEncodingV2.Float32BitsLE, 0, default, 0);
            var binding = Fixture.CreateRegisteredBinding(
                "aibt.test.registered", "aibt.schema.registered", descriptor, 5, new[] { forged });

            Assert.That(NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out var owner, out var failure), Is.False);
            Assert.That(owner, Is.Null);
            Assert.That(failure.ResourceKind, Is.EqualTo(NativeResourceKindV1.ProgramRegisteredFields));
        }

        [Test]
        public void V2Binder_RejectsRegisteredNestedSchemaAndEqualityLinkageMismatch()
        {
            var outer = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.outer"), 1, 4, 4,
                Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.outer"));
            var inner = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.inner"), 1, 4, 4,
                Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.inner"));
            var innerField = new NativeRegisteredBlackboardFieldBindingV2(
                StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1, 0, 4, 4,
                NativeBlackboardFieldEncodingV2.Int32LE, 0, default, 0);
            var innerType = Fixture.RegisteredType("aibt.test.inner", "aibt.schema.inner", inner, 1, innerField);
            var fields = new[]
            {
                new NativeRegisteredBlackboardFieldBindingV2(
                    StableHash.Fnv1A64("inner"), inner.TypeId, inner.Version, 0, 4, 4,
                    NativeBlackboardFieldEncodingV2.Registered, inner.CanonicalSchemaId,
                    new CompiledHash(new string('7', 64)), inner.EqualityContractId),
                innerField,
            };
            var outerType = Fixture.RegisteredType("aibt.test.outer", "aibt.schema.outer", outer, 0, fields[0]);
            var types = new[] { outerType, innerType };
            var binding = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(outer), new byte[4], BlackboardScope.Tree,
                0, types, fields);
            AssertProgramRejected(binding, NativeResourceKindV1.ProgramRegisteredFields);
        }

        [Test]
        public void V2Binder_StrictAuthorityRejectsTruncationAppendGapOverlapOrderDefaultAndCountMismatch()
        {
            var valid = Fixture.CreateTwoSlotAgentBinding();
            var outer = valid.GetOuterPreimageCopy();
            AssertProgramRejected(Rebind(valid, Slice(outer, outer.Length - 1), valid.Scopes, valid.Slots, valid.SlotAuthorities), NativeResourceKindV1.ProgramHash);
            var appended = new byte[outer.Length + 1]; Array.Copy(outer, appended, outer.Length); appended[outer.Length] = 1;
            AssertProgramRejected(Rebind(valid, appended, valid.Scopes, valid.Slots, valid.SlotAuthorities), NativeResourceKindV1.ProgramHash);
            AssertProgramRejected(new NativeProgramBlackboardBindingV2(
                valid.SemanticProgram, outer, valid.Scopes, valid.Slots,
                new[] { valid.SlotAuthorities[0] }, valid.Accesses, valid.WatchedSlots,
                valid.RegisteredTypes, valid.RegisteredFields), NativeResourceKindV1.ProgramHash);

            var gapSlots = new[] { valid.Slots[0], Fixture.CopySlot(valid.Slots[1], 2, valid.Slots[1].Offset) };
            AssertScopeRejected(valid, gapSlots, valid.SlotAuthorities);
            var overlapSlots = new[] { valid.Slots[0], Fixture.CopySlot(valid.Slots[1], 1, 0) };
            Assert.That(() => Fixture.RebuildSemantic(valid.SemanticProgram, overlapSlots),
                Throws.TypeOf<ArgumentException>());
            var reversedSlots = new[]
            {
                Fixture.CopySlot(valid.Slots[1], 0, 0),
                Fixture.CopySlot(valid.Slots[0], 1, 4),
            };
            var reversedAuthorities = new[] { valid.SlotAuthorities[1], valid.SlotAuthorities[0] };
            AssertScopeRejected(valid, reversedSlots, reversedAuthorities);
            var wrongAuthorities = new[]
            {
                valid.SlotAuthorities[0],
                new NativeBlackboardSlotAuthorityV2("beta", "Int32", string.Empty, Encoding.UTF8.GetBytes("8")),
            };
            AssertScopeRejected(valid, valid.Slots, wrongAuthorities, NativeResourceKindV1.ProgramBlackboardSlots);

            var source = valid.Scopes[0];
            var extraSchema = Append(source.GetSchemaBytesCopy(), 1);
            var schemaScope = new NativeBlackboardScopeBindingV2(
                source.Scope, source.ContractId, source.ContractVersion, source.FirstSlot, source.SlotCount,
                extraSchema, source.GetRawLayoutCopy());
            AssertScopeRejected(valid, new[] { schemaScope }, valid.Slots, valid.SlotAuthorities);
            var extraLayout = Append(source.GetRawLayoutCopy(), 1);
            var layoutScope = new NativeBlackboardScopeBindingV2(
                source.Scope, source.ContractId, source.ContractVersion, source.FirstSlot, source.SlotCount,
                source.GetSchemaBytesCopy(), extraLayout);
            AssertScopeRejected(valid, new[] { layoutScope }, valid.Slots, valid.SlotAuthorities);
            var wrongDefault = source.GetRawLayoutCopy(); wrongDefault[wrongDefault.Length - 2] ^= 1;
            var defaultScope = new NativeBlackboardScopeBindingV2(
                source.Scope, source.ContractId, source.ContractVersion, source.FirstSlot, source.SlotCount,
                source.GetSchemaBytesCopy(), wrongDefault);
            AssertScopeRejected(valid, new[] { defaultScope }, valid.Slots, valid.SlotAuthorities);
        }

        [Test]
        public void V2Binder_RequiresExactScopeDescriptorSlotBijectionAndCapabilityBits()
        {
            var valid = Fixture.CreateBinding(BuiltInBlackboardTypes.Int32, 5, BlackboardScope.Agent);
            var missingDescriptor = new[] { Fixture.CopySlot(valid.Slots[0], 0, 0, CompiledIndex.Invalid) };
            AssertProgramRejected(Rebind(valid, valid.GetOuterPreimageCopy(), valid.Scopes, missingDescriptor, valid.SlotAuthorities),
                NativeResourceKindV1.ProgramScopeDescriptors);

            var duplicateDescriptors = new[] { valid.Scopes[0], valid.Scopes[0] };
            AssertProgramRejected(Rebind(valid, valid.GetOuterPreimageCopy(), duplicateDescriptors, valid.Slots, valid.SlotAuthorities),
                NativeResourceKindV1.ProgramScopeDescriptors);

            var wrongRange = new NativeBlackboardScopeBindingV2(
                BlackboardScope.Agent, valid.Scopes[0].ContractId, 1, 1, 1,
                valid.Scopes[0].GetSchemaBytesCopy(), valid.Scopes[0].GetRawLayoutCopy());
            AssertProgramRejected(Rebind(valid, valid.GetOuterPreimageCopy(), new[] { wrongRange }, valid.Slots, valid.SlotAuthorities),
                NativeResourceKindV1.ProgramScopeDescriptors);

            var missingBitProgram = Fixture.RebuildSemantic(valid.SemanticProgram, valid.Slots, 0);
            var missingBit = new NativeProgramBlackboardBindingV2(
                missingBitProgram, valid.GetOuterPreimageCopy(), valid.Scopes, valid.Slots, valid.SlotAuthorities,
                valid.Accesses, valid.WatchedSlots, valid.RegisteredTypes, valid.RegisteredFields);
            AssertProgramRejected(missingBit, NativeResourceKindV1.ProgramScopeDescriptors);

            var tree = Fixture.CreateBinding();
            var extraBitProgram = Fixture.RebuildSemantic(tree.SemanticProgram, tree.Slots, 1u << 7);
            var extraBit = new NativeProgramBlackboardBindingV2(
                extraBitProgram, tree.GetOuterPreimageCopy(), tree.Scopes, tree.Slots, tree.SlotAuthorities,
                tree.Accesses, tree.WatchedSlots, tree.RegisteredTypes, tree.RegisteredFields);
            AssertProgramRejected(extraBit, NativeResourceKindV1.ProgramScopeDescriptors);
        }

        [Test]
        public void V2Binder_RejectsSelfConsistentRegisteredSchemaAndCallerEqualityTampering()
        {
            var field = new NativeRegisteredBlackboardFieldBindingV2(
                StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1,
                0, 4, 4, NativeBlackboardFieldEncodingV2.Int32LE, 0, default, 0);
            var forgedDescriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.registered"), 1, 4, 4,
                123, StableHash.Fnv1A64("aibt.schema.registered"));
            var forgedType = Fixture.RegisteredType(
                "aibt.test.registered", "aibt.schema.registered", forgedDescriptor, 0, field);
            var equalityBinding = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(forgedDescriptor), new byte[4], BlackboardScope.Tree,
                0, new[] { forgedType }, new[] { field });
            AssertProgramRejected(equalityBinding, NativeResourceKindV1.ProgramRegisteredFields);

            var descriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.registered"), 1, 4, 4,
                Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.registered"));
            var schema = Fixture.RegisteredSchemaBytes(
                "aibt.test.registered", "aibt.schema.registered", descriptor, new[] { field });
            schema[schema.Length - 1] ^= 1;
            var tamperedType = new NativeRegisteredBlackboardTypeBindingV2(descriptor, schema, 0, 1);
            var schemaBinding = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(descriptor), new byte[4], BlackboardScope.Tree,
                0, new[] { tamperedType }, new[] { field });
            AssertProgramRejected(schemaBinding, NativeResourceKindV1.ProgramRegisteredFields);

            var secondDescriptor = new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64("aibt.test.second"), 1, 4, 4,
                Fixture.CanonicalEquality, StableHash.Fnv1A64("aibt.schema.second"));
            var secondType = Fixture.RegisteredType(
                "aibt.test.second", "aibt.schema.second", secondDescriptor, 0, field);
            var validType = Fixture.RegisteredType(
                "aibt.test.registered", "aibt.schema.registered", descriptor, 0, field);
            var overlappingRanges = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(descriptor), new byte[4], BlackboardScope.Tree,
                0, new[] { validType, secondType }, new[] { field });
            AssertProgramRejected(overlappingRanges, NativeResourceKindV1.ProgramRegisteredFields);

            var emptyType = Fixture.RegisteredType(
                "aibt.test.registered", "aibt.schema.registered", descriptor, 0);
            var uncoveredField = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(descriptor), new byte[4], BlackboardScope.Tree,
                0, new[] { emptyType }, new[] { field });
            AssertProgramRejected(uncoveredField, NativeResourceKindV1.ProgramRegisteredFields);
        }

        [Test]
        public void V2Binder_TotalDefaultValidationRejectsMalformedBoolAndNonFiniteFloatWithoutThrowing()
        {
            AssertProgramRejected(
                Fixture.CreateBinding(BuiltInBlackboardTypes.Bool, new byte[] { 2 }),
                NativeResourceKindV1.ProgramBlackboardSlots);
            AssertProgramRejected(
                Fixture.CreateBinding(BuiltInBlackboardTypes.Float32, float.NaN),
                NativeResourceKindV1.ProgramBlackboardSlots);
            var oversized = new byte[BuiltInBlackboardTypes.FixedString32.Size];
            oversized[0] = 31;
            AssertProgramRejected(
                Fixture.CreateBinding(BuiltInBlackboardTypes.FixedString32, oversized),
                NativeResourceKindV1.ProgramBlackboardSlots);
        }

        [Test]
        public void V2Binder_RejectsNegativeZeroInEveryCompiledFloatingDefaultShape()
        {
            AssertProgramRejected(Fixture.CreateBinding(
                BuiltInBlackboardTypes.Float32, new byte[] { 0, 0, 0, 0x80 }), NativeResourceKindV1.ProgramBlackboardSlots, "Float32");
            AssertProgramRejected(Fixture.CreateBinding(
                BuiltInBlackboardTypes.Float64, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x80 }), NativeResourceKindV1.ProgramBlackboardSlots, "Float64");
            AssertProgramRejected(Fixture.CreateBinding(
                BuiltInBlackboardTypes.Float2, new byte[] { 0, 0, 0, 0x80, 0, 0, 0x80, 0x3f }), NativeResourceKindV1.ProgramBlackboardSlots, "Float2");
            AssertProgramRejected(Fixture.CreateBinding(
                BuiltInBlackboardTypes.Float3, new byte[] { 0, 0, 0x80, 0x3f, 0, 0, 0, 0x80, 0, 0, 0, 0x40 }), NativeResourceKindV1.ProgramBlackboardSlots, "Float3");
            AssertProgramRejected(Fixture.CreateBinding(
                BuiltInBlackboardTypes.Quaternion, new byte[] { 0, 0, 0x80, 0x3f, 0, 0, 0, 0x40, 0, 0, 0x40, 0x40, 0, 0, 0, 0x80 }), NativeResourceKindV1.ProgramBlackboardSlots, "Quaternion");
        }

        [Test]
        public void V2Binder_RequiresCanonicalRegisteredTypeOrderUniqueIdentitiesAndExactFieldRanges()
        {
            var field = new NativeRegisteredBlackboardFieldBindingV2(
                StableHash.Fnv1A64("value"), BuiltInBlackboardTypes.Int32.TypeId, 1,
                0, 4, 4, NativeBlackboardFieldEncodingV2.Int32LE, 0, default, 0);
            var alpha = Descriptor("aibt.test.alpha", "aibt.schema.alpha");
            var beta = Descriptor("aibt.test.beta", "aibt.schema.beta");
            var alpha0 = Fixture.RegisteredType("aibt.test.alpha", "aibt.schema.alpha", alpha, 0, field);
            var alpha1 = Fixture.RegisteredType("aibt.test.alpha", "aibt.schema.alpha", alpha, 1, field);
            var duplicate = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(alpha), new byte[4], BlackboardScope.Tree, 0,
                new[] { alpha0, alpha1 }, new[] { field, field });
            AssertProgramRejected(duplicate, NativeResourceKindV1.ProgramRegisteredFields);

            var beta0 = Fixture.RegisteredType("aibt.test.beta", "aibt.schema.beta", beta, 0, field);
            var alpha1Reordered = Fixture.RegisteredType("aibt.test.alpha", "aibt.schema.alpha", alpha, 1, field);
            var reordered = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(beta), new byte[4], BlackboardScope.Tree, 0,
                new[] { beta0, alpha1Reordered }, new[] { field, field });
            AssertProgramRejected(reordered, NativeResourceKindV1.ProgramRegisteredFields);

            var alphaRenumbered = Fixture.RegisteredType("aibt.test.alpha", "aibt.schema.alpha", alpha, 1, field);
            var betaRenumbered = Fixture.RegisteredType("aibt.test.beta", "aibt.schema.beta", beta, 0, field);
            var renumbered = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(alpha), new byte[4], BlackboardScope.Tree, 0,
                new[] { alphaRenumbered, betaRenumbered }, new[] { field, field });
            AssertProgramRejected(renumbered, NativeResourceKindV1.ProgramRegisteredFields);

            var collidingSchema = Descriptor("aibt.test.beta", "aibt.schema.alpha");
            var betaCollision = Fixture.RegisteredType("aibt.test.beta", "aibt.schema.alpha", collidingSchema, 1, field);
            var collision = Fixture.CreateBinding(
                BlackboardTypeDescriptor.FromRegistered(alpha), new byte[4], BlackboardScope.Tree, 0,
                new[] { alpha0, betaCollision }, new[] { field, field });
            AssertProgramRejected(collision, NativeResourceKindV1.ProgramRegisteredFields);
        }

        private static RegisteredUnmanagedTypeDescriptor Descriptor(string typeId, string schemaId)
            => new RegisteredUnmanagedTypeDescriptor(
                StableHash.Fnv1A64(typeId), 1, 4, 4, Fixture.CanonicalEquality, StableHash.Fnv1A64(schemaId));

        private static void AssertScopeRejected(
            NativeProgramBlackboardBindingV2 valid,
            IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
            IReadOnlyList<NativeBlackboardSlotAuthorityV2> authorities,
            NativeResourceKindV1 expectedResource = NativeResourceKindV1.ProgramScopeDescriptors)
        {
            var semantic = Fixture.RebuildSemantic(valid.SemanticProgram, slots);
            var scope = Fixture.BuildScope(valid.Scopes[0], semantic, slots, authorities);
            var outer = Fixture.OuterBytes(semantic, new[] { scope }, slots, authorities, valid.Accesses);
            var binding = new NativeProgramBlackboardBindingV2(
                semantic, outer, new[] { scope }, slots, authorities, valid.Accesses,
                valid.WatchedSlots, valid.RegisteredTypes, valid.RegisteredFields);
            AssertProgramRejected(binding, expectedResource);
        }

        private static void AssertScopeRejected(
            NativeProgramBlackboardBindingV2 valid,
            IReadOnlyList<NativeBlackboardScopeBindingV2> scopes,
            IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
            IReadOnlyList<NativeBlackboardSlotAuthorityV2> authorities)
        {
            var outer = Fixture.OuterBytes(valid.SemanticProgram, scopes, slots, authorities, valid.Accesses);
            AssertProgramRejected(Rebind(valid, outer, scopes, slots, authorities), NativeResourceKindV1.ProgramScopeDescriptors);
        }

        private static NativeProgramBlackboardBindingV2 Rebind(
            NativeProgramBlackboardBindingV2 source,
            byte[] outer,
            IEnumerable<NativeBlackboardScopeBindingV2> scopes,
            IEnumerable<NativeBlackboardSlotBindingV2> slots,
            IEnumerable<NativeBlackboardSlotAuthorityV2> authorities)
            => new NativeProgramBlackboardBindingV2(
                source.SemanticProgram, outer, scopes, slots, authorities, source.Accesses,
                source.WatchedSlots, source.RegisteredTypes, source.RegisteredFields);

        private static void AssertProgramRejected(
            NativeProgramBlackboardBindingV2 binding,
            NativeResourceKindV1 resource,
            string label = null)
        {
            NativeProgramImageOwnerV1 owner = null;
            NativeRuntimeFailureV1 failure = default;
            var created = false;
            Assert.DoesNotThrow(() => created = NativeProgramImageOwnerV1.TryCreateV2(
                binding, NativeProgramImageCapacityV2.Exact(binding), Allocator.Persistent,
                out owner, out failure));
            Assert.That(created, Is.False, label);
            Assert.That(owner, Is.Null);
            Assert.That(failure.ResourceKind, Is.EqualTo(resource), label);
        }

        private static byte[] Slice(byte[] source, int count)
        { var result = new byte[count]; Array.Copy(source, result, count); return result; }

        private static byte[] Append(byte[] source, byte value)
        { var result = new byte[source.Length + 1]; Array.Copy(source, result, source.Length); result[source.Length] = value; return result; }

        public static class Fixture
        {
            internal static NativeProgramBlackboardBindingV2 CreateBinding()
                => CreateBinding(BuiltInBlackboardTypes.Int32, Bytes(5));

            public static NativeProgramBlackboardBindingV2 CreateBinding<T>(
                BlackboardTypeDescriptor descriptor,
                T defaultValue,
                BlackboardScope scope = BlackboardScope.Tree,
                ulong enumContractId = 0)
                where T : unmanaged
                => CreateBinding(descriptor, Bytes(defaultValue), scope, enumContractId);

            public static NativeProgramBlackboardBindingV2 CreateBinding(
                BlackboardTypeDescriptor descriptor,
                byte[] defaultBytes,
                BlackboardScope scope = BlackboardScope.Tree,
                ulong enumContractId = 0,
                NativeRegisteredBlackboardTypeBindingV2[] registeredTypes = null,
                NativeRegisteredBlackboardFieldBindingV2[] registeredFields = null)
            {
                registeredTypes = registeredTypes ?? Array.Empty<NativeRegisteredBlackboardTypeBindingV2>();
                registeredFields = registeredFields ?? Array.Empty<NativeRegisteredBlackboardFieldBindingV2>();
                var registeredTypeIndex = CompiledIndex.Invalid;
                if (descriptor.ValueType == BlackboardValueType.Registered)
                {
                    for (var index = 0; index < registeredTypes.Length; index++)
                        if (registeredTypes[index].Descriptor.TypeId == descriptor.TypeId
                            && registeredTypes[index].Descriptor.Version == descriptor.Version)
                        { if (registeredTypeIndex == CompiledIndex.Invalid) registeredTypeIndex = (uint)index; }
                    if (registeredTypeIndex == CompiledIndex.Invalid) throw new ArgumentException("Registered fixture descriptor is absent.");
                }
                var semantic = CreateProgram(descriptor, defaultBytes, scope, enumContractId);
                var contractId = scope == BlackboardScope.Tree ? "aibt.test.tree"
                    : scope == BlackboardScope.Agent ? "aibt.test.agent" : "aibt.test.shared";
                var canonicalTypeId = CanonicalTypeId(descriptor);
                var enumContract = enumContractId == 0 ? string.Empty : "aibt.test.enum";
                if (enumContractId != 0 && StableHash.Fnv1A64(enumContract) != enumContractId)
                    enumContract = string.Empty;
                byte[] canonicalDefault;
                if (descriptor.ValueType == BlackboardValueType.Registered) canonicalDefault = Array.Empty<byte>();
                else
                {
                    try
                    {
                        canonicalDefault = NativeBlackboardSlotAuthorityV2.CreateBuiltIn(
                            "health", descriptor, enumContract, defaultBytes).GetCanonicalDefaultJsonCopy();
                    }
                    catch (ArgumentException)
                    {
                        // Malformed public binding probes must reach TryCreateV2; the verifier owns rejection.
                        canonicalDefault = Array.Empty<byte>();
                    }
                }
                var scopes = Array.Empty<NativeBlackboardScopeBindingV2>();
                var scopeIndex = CompiledIndex.Invalid;
                if (scope != BlackboardScope.Tree)
                {
                    var schema = SchemaBytes(scope, contractId, canonicalTypeId, enumContract, canonicalDefault, NativeBlackboardReductionKindV2.None);
                    var schemaHash = new CompiledHash(StableHash.Sha256Hex(schema));
                    var layout = LayoutBytes(scope, contractId, schemaHash, descriptor, defaultBytes, enumContractId, NativeBlackboardReductionKindV2.None);
                    scopes = new[]
                    {
                        new NativeBlackboardScopeBindingV2(
                            scope, contractId, 1, 0, 1, schema, layout),
                    };
                    scopeIndex = 0;
                }
                var slot = new NativeBlackboardSlotBindingV2(
                    StableHash.Fnv1A64("health"),
                    descriptor.TypeId,
                    descriptor.Version,
                    enumContractId,
                    scope,
                    0,
                    0,
                    (uint)descriptor.Size,
                    (uint)descriptor.Alignment,
                    0,
                    (uint)descriptor.Size,
                    CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write,
                    scopeIndex,
                    registeredTypeIndex,
                    NativeBlackboardReductionKindV2.None);
                var slotAuthority = new NativeBlackboardSlotAuthorityV2(
                    "health", canonicalTypeId, enumContract, canonicalDefault);
                var access = new NativeBlackboardAccessBindingV2(
                    0,
                    0,
                    scope,
                    0,
                    NativeBlackboardAccessModeV2.ReadWrite,
                    descriptor.TypeId,
                    descriptor.Version,
                    enumContractId,
                    registeredTypeIndex,
                    NativeBlackboardReductionKindV2.None);
                var slots = new[] { slot };
                var slotAuthorities = new[] { slotAuthority };
                var accesses = new[] { access };
                var outer = OuterBytes(semantic, scopes, slots, slotAuthorities, accesses);
                return new NativeProgramBlackboardBindingV2(
                    semantic,
                    outer,
                    scopes,
                    slots,
                    slotAuthorities,
                    accesses,
                    Array.Empty<NativeBlackboardWatchedSlotBindingV2>(),
                    registeredTypes,
                    registeredFields);
            }

            public static NativeProgramBlackboardBindingV2 CreateSharedReductionBinding<T>(
                BlackboardTypeDescriptor descriptor,
                T defaultValue,
                NativeBlackboardReductionKindV2 reduction)
                where T : unmanaged
                => CreateSharedReductionBinding(descriptor, defaultValue, reduction, 0);

            public static NativeProgramBlackboardBindingV2 CreateSharedReductionBinding<T>(
                BlackboardTypeDescriptor descriptor,
                T defaultValue,
                NativeBlackboardReductionKindV2 reduction,
                ulong enumContractId)
                where T : unmanaged
            {
                if (reduction == NativeBlackboardReductionKindV2.None)
                    throw new ArgumentOutOfRangeException(nameof(reduction));
                return WithSharedReduction(
                    CreateBinding(descriptor, defaultValue, BlackboardScope.Shared, enumContractId), reduction);
            }

            public static NativeProgramBlackboardBindingV2 CreateRegisteredSharedReductionBinding<T>(
                string canonicalTypeId,
                string canonicalSchemaId,
                RegisteredUnmanagedTypeDescriptor descriptor,
                T defaultValue,
                NativeBlackboardReductionKindV2 reduction,
                NativeRegisteredBlackboardFieldBindingV2[] fields)
                where T : unmanaged
                => WithSharedReduction(
                    CreateRegisteredBinding(
                        canonicalTypeId, canonicalSchemaId, descriptor, defaultValue,
                        BlackboardScope.Shared, fields), reduction);

            private static NativeProgramBlackboardBindingV2 WithSharedReduction(
                NativeProgramBlackboardBindingV2 source,
                NativeBlackboardReductionKindV2 reduction)
            {
                if (reduction == NativeBlackboardReductionKindV2.None)
                    throw new ArgumentOutOfRangeException(nameof(reduction));
                var sourceSlot = source.Slots[0];
                var slots = new[]
                {
                    new NativeBlackboardSlotBindingV2(
                        sourceSlot.StableKeyId, sourceSlot.TypeId, sourceSlot.TypeVersion,
                        sourceSlot.EnumContractId, sourceSlot.Scope, sourceSlot.ScopeSlotIndex,
                        sourceSlot.Offset, sourceSlot.Size, sourceSlot.Alignment,
                        sourceSlot.DefaultOffset, sourceSlot.DefaultSize, sourceSlot.AccessFlags,
                        sourceSlot.ScopeDescriptorIndex, sourceSlot.RegisteredTypeIndex, reduction),
                };
                var sourceAccess = source.Accesses[0];
                var accesses = new[]
                {
                    new NativeBlackboardAccessBindingV2(
                        sourceAccess.NodeIndex, sourceAccess.AccessOrdinal, sourceAccess.Scope,
                        sourceAccess.SlotIndex, sourceAccess.Mode, sourceAccess.TypeId,
                        sourceAccess.TypeVersion, sourceAccess.EnumContractId,
                        sourceAccess.RegisteredTypeIndex, reduction),
                };
                var authorities = new[] { source.SlotAuthorities[0] };
                var scope = BuildScope(source.Scopes[0], source.SemanticProgram, slots, authorities);
                var scopes = new[] { scope };
                return new NativeProgramBlackboardBindingV2(
                    source.SemanticProgram,
                    OuterBytes(source.SemanticProgram, scopes, slots, authorities, accesses),
                    scopes, slots, authorities, accesses,
                    Array.Empty<NativeBlackboardWatchedSlotBindingV2>(),
                    source.RegisteredTypes,
                    source.RegisteredFields);
            }

            public static NativeProgramBlackboardBindingV2 CreateAgentSharedReductionBinding()
            {
                var descriptor = BuiltInBlackboardTypes.Int32;
                var defaults = new byte[] { 5, 0, 0, 0, 7, 0, 0, 0 };
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.test.success"), 1,
                        0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)),
                };
                var semanticSlots = new[]
                {
                    new CompiledBlackboardSlotRecord(
                        StableHash.Fnv1A64("alpha"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Agent, 0, 4, 4, 0,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write),
                    new CompiledBlackboardSlotRecord(
                        StableHash.Fnv1A64("beta"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Shared, 0, 4, 4, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write),
                };
                var debug = new[] { new CompiledDebugMapEntry(0, new NodeId("root"), "/root") };
                var preliminary = Build(Hash('0'), nodes, semanticSlots, defaults, debug, (1u << 7) | (1u << 8));
                var semantic = Build(ComputeHash(preliminary), nodes, semanticSlots, defaults, debug, (1u << 7) | (1u << 8));
                var slots = new[]
                {
                    new NativeBlackboardSlotBindingV2(
                        StableHash.Fnv1A64("alpha"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Agent, 0, 0, 4, 4, 0, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write,
                        0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.None),
                    new NativeBlackboardSlotBindingV2(
                        StableHash.Fnv1A64("beta"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Shared, 0, 0, 4, 4, 4, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write,
                        1, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.Sum),
                };
                var authorities = new[]
                {
                    NativeBlackboardSlotAuthorityV2.CreateBuiltIn(
                        "alpha", descriptor, string.Empty, new byte[] { 5, 0, 0, 0 }),
                    NativeBlackboardSlotAuthorityV2.CreateBuiltIn(
                        "beta", descriptor, string.Empty, new byte[] { 7, 0, 0, 0 }),
                };
                var accesses = new[]
                {
                    new NativeBlackboardAccessBindingV2(
                        0, 0, BlackboardScope.Agent, 0, NativeBlackboardAccessModeV2.ReadWrite,
                        descriptor.TypeId, 1, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.None),
                    new NativeBlackboardAccessBindingV2(
                        0, 1, BlackboardScope.Shared, 0, NativeBlackboardAccessModeV2.ReadWrite,
                        descriptor.TypeId, 1, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.Sum),
                };
                var agentSlots = new[] { slots[0] };
                var agentAuthorities = new[] { authorities[0] };
                var agentSchema = MultiSchemaBytes(
                    BlackboardScope.Agent, "aibt.test.agent", agentSlots, agentAuthorities);
                var agentHash = new CompiledHash(StableHash.Sha256Hex(agentSchema));
                var agentLayout = MultiLayoutBytes(
                    BlackboardScope.Agent, "aibt.test.agent", agentHash, semantic, agentSlots, agentAuthorities);
                var sharedSlots = new[] { slots[1] };
                var sharedAuthorities = new[] { authorities[1] };
                var sharedSchema = MultiSchemaBytes(
                    BlackboardScope.Shared, "aibt.test.shared", sharedSlots, sharedAuthorities);
                var sharedHash = new CompiledHash(StableHash.Sha256Hex(sharedSchema));
                var sharedLayout = MultiLayoutBytes(
                    BlackboardScope.Shared, "aibt.test.shared", sharedHash, semantic, sharedSlots, sharedAuthorities);
                var scopes = new[]
                {
                    new NativeBlackboardScopeBindingV2(
                        BlackboardScope.Agent, "aibt.test.agent", 1, 0, 1, agentSchema, agentLayout),
                    new NativeBlackboardScopeBindingV2(
                        BlackboardScope.Shared, "aibt.test.shared", 1, 1, 1, sharedSchema, sharedLayout),
                };
                return new NativeProgramBlackboardBindingV2(
                    semantic, OuterBytes(semantic, scopes, slots, authorities, accesses),
                    scopes, slots, authorities, accesses,
                    Array.Empty<NativeBlackboardWatchedSlotBindingV2>(),
                    Array.Empty<NativeRegisteredBlackboardTypeBindingV2>(),
                    Array.Empty<NativeRegisteredBlackboardFieldBindingV2>());
            }

            public static NativeProgramBlackboardBindingV2 CreateTwoSharedReductionBinding()
            {
                var descriptor = BuiltInBlackboardTypes.Int32;
                var defaults = new byte[] { 5, 0, 0, 0, 7, 0, 0, 0 };
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.test.success"), 1,
                        0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)),
                };
                var semanticSlots = new[]
                {
                    new CompiledBlackboardSlotRecord(
                        StableHash.Fnv1A64("alpha"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Shared, 0, 4, 4, 0,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write),
                    new CompiledBlackboardSlotRecord(
                        StableHash.Fnv1A64("beta"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Shared, 4, 4, 4, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write),
                };
                var debug = new[] { new CompiledDebugMapEntry(0, new NodeId("root"), "/root") };
                var preliminary = Build(Hash('0'), nodes, semanticSlots, defaults, debug, 1u << 8);
                var semantic = Build(ComputeHash(preliminary), nodes, semanticSlots, defaults, debug, 1u << 8);
                var slots = new[]
                {
                    new NativeBlackboardSlotBindingV2(
                        StableHash.Fnv1A64("alpha"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Shared, 0, 0, 4, 4, 0, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write,
                        0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.Sum),
                    new NativeBlackboardSlotBindingV2(
                        StableHash.Fnv1A64("beta"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Shared, 1, 4, 4, 4, 4, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write,
                        0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.Sum),
                };
                var authorities = new[]
                {
                    NativeBlackboardSlotAuthorityV2.CreateBuiltIn(
                        "alpha", descriptor, string.Empty, new byte[] { 5, 0, 0, 0 }),
                    NativeBlackboardSlotAuthorityV2.CreateBuiltIn(
                        "beta", descriptor, string.Empty, new byte[] { 7, 0, 0, 0 }),
                };
                var accesses = new[]
                {
                    new NativeBlackboardAccessBindingV2(
                        0, 0, BlackboardScope.Shared, 0, NativeBlackboardAccessModeV2.ReadWrite,
                        descriptor.TypeId, 1, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.Sum),
                    new NativeBlackboardAccessBindingV2(
                        0, 1, BlackboardScope.Shared, 1, NativeBlackboardAccessModeV2.ReadWrite,
                        descriptor.TypeId, 1, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.Sum),
                };
                var schema = MultiSchemaBytes(
                    BlackboardScope.Shared, "aibt.test.shared", slots, authorities);
                var schemaHash = new CompiledHash(StableHash.Sha256Hex(schema));
                var layout = MultiLayoutBytes(
                    BlackboardScope.Shared, "aibt.test.shared", schemaHash, semantic, slots, authorities);
                var scopes = new[]
                {
                    new NativeBlackboardScopeBindingV2(
                        BlackboardScope.Shared, "aibt.test.shared", 1, 0, 2, schema, layout),
                };
                return new NativeProgramBlackboardBindingV2(
                    semantic, OuterBytes(semantic, scopes, slots, authorities, accesses),
                    scopes, slots, authorities, accesses,
                    Array.Empty<NativeBlackboardWatchedSlotBindingV2>(),
                    Array.Empty<NativeRegisteredBlackboardTypeBindingV2>(),
                    Array.Empty<NativeRegisteredBlackboardFieldBindingV2>());
            }

            public static NativeProgramBlackboardBindingV2 CreateRegisteredBinding<T>(
                string canonicalTypeId,
                string canonicalSchemaId,
                RegisteredUnmanagedTypeDescriptor descriptor,
                T defaultValue,
                NativeRegisteredBlackboardFieldBindingV2[] fields)
                where T : unmanaged
                => CreateRegisteredBinding(
                    canonicalTypeId, canonicalSchemaId, descriptor, defaultValue, BlackboardScope.Tree, fields);

            public static NativeProgramBlackboardBindingV2 CreateRegisteredBinding<T>(
                string canonicalTypeId,
                string canonicalSchemaId,
                RegisteredUnmanagedTypeDescriptor descriptor,
                T defaultValue,
                BlackboardScope scope,
                NativeRegisteredBlackboardFieldBindingV2[] fields)
                where T : unmanaged
            {
                var type = RegisteredType(canonicalTypeId, canonicalSchemaId, descriptor, 0, fields);
                return CreateBinding(
                    BlackboardTypeDescriptor.FromRegistered(descriptor), Bytes(defaultValue),
                    scope, 0, new[] { type }, fields);
            }

            public const ulong CanonicalEquality = 0x69e3a80e385e338eUL;

            internal static NativeRegisteredBlackboardTypeBindingV2 RegisteredType(
                string canonicalTypeId,
                string canonicalSchemaId,
                RegisteredUnmanagedTypeDescriptor descriptor,
                uint firstField,
                params NativeRegisteredBlackboardFieldBindingV2[] fields)
                => new NativeRegisteredBlackboardTypeBindingV2(
                    descriptor, RegisteredSchemaBytes(canonicalTypeId, canonicalSchemaId, descriptor, fields),
                    firstField, (uint)fields.Length);

            internal static byte[] RegisteredSchemaBytes(
                string canonicalTypeId,
                string canonicalSchemaId,
                RegisteredUnmanagedTypeDescriptor descriptor,
                IReadOnlyList<NativeRegisteredBlackboardFieldBindingV2> fields)
            {
                var writer = new Writer();
                writer.Raw("AIBT-VALUE-SCHEMA-V1\0"); writer.U32(1);
                writer.String(canonicalTypeId); writer.U64(StableHash.Fnv1A64(canonicalTypeId)); writer.U32(descriptor.Version);
                writer.String(canonicalSchemaId); writer.U64(StableHash.Fnv1A64(canonicalSchemaId));
                writer.U32((uint)descriptor.Size); writer.U8((byte)descriptor.Alignment); writer.U32((uint)fields.Count);
                for (var index = 0; index < fields.Count; index++)
                {
                    var field = fields[index]; var fieldId = FieldId(field.FieldId); var typeId = TypeId(field.ValueTypeId);
                    writer.String(fieldId); writer.U64(field.FieldId); writer.String(typeId); writer.U64(field.ValueTypeId);
                    writer.U32(field.ValueTypeVersion); writer.HashOrZero(field.RegisteredSchemaHash);
                    writer.U32(field.Offset); writer.U32(field.Size); writer.U8((byte)field.Alignment); writer.U8((byte)field.Encoding);
                }
                return writer.ToArray();
            }

            private static string FieldId(ulong value)
            {
                foreach (var id in new[] { "value", "weight", "inner", "flag", "a_inner", "z_flag", "single", "wide" })
                    if (StableHash.Fnv1A64(id) == value) return id;
                throw new ArgumentException("Unknown test field ID.");
            }

            private static string TypeId(ulong value)
            {
                foreach (var id in new[] { "Bool", "Int32", "Float32", "Float64", "aibt.test.inner", "aibt.test.float-inner" })
                    if (StableHash.Fnv1A64(id) == value) return id;
                throw new ArgumentException("Unknown test value type ID.");
            }

            internal static NativeProgramBlackboardBindingV2 CreateTwoSlotAgentBinding()
            {
                var descriptor = BuiltInBlackboardTypes.Int32;
                var defaults = new byte[] { 5, 0, 0, 0, 7, 0, 0, 0 };
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.test.success"), 1,
                        0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)),
                };
                var semanticSlots = new[]
                {
                    new CompiledBlackboardSlotRecord(StableHash.Fnv1A64("alpha"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Agent, 0, 4, 4, 0, CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write),
                    new CompiledBlackboardSlotRecord(StableHash.Fnv1A64("beta"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Agent, 4, 4, 4, 4, CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write),
                };
                var debug = new[] { new CompiledDebugMapEntry(0, new NodeId("root"), "/root") };
                var preliminary = Build(Hash('0'), nodes, semanticSlots, defaults, debug, 1u << 7);
                var semantic = Build(ComputeHash(preliminary), nodes, semanticSlots, defaults, debug, 1u << 7);
                var slots = new[]
                {
                    new NativeBlackboardSlotBindingV2(StableHash.Fnv1A64("alpha"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Agent, 0, 0, 4, 4, 0, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.None),
                    new NativeBlackboardSlotBindingV2(StableHash.Fnv1A64("beta"), descriptor.TypeId, 1, 0,
                        BlackboardScope.Agent, 1, 4, 4, 4, 4, 4,
                        CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.None),
                };
                var authorities = new[]
                {
                    NativeBlackboardSlotAuthorityV2.CreateBuiltIn("alpha", descriptor, string.Empty, new byte[] { 5, 0, 0, 0 }),
                    NativeBlackboardSlotAuthorityV2.CreateBuiltIn("beta", descriptor, string.Empty, new byte[] { 7, 0, 0, 0 }),
                };
                var accesses = new[]
                {
                    new NativeBlackboardAccessBindingV2(0, 0, BlackboardScope.Agent, 0, NativeBlackboardAccessModeV2.ReadWrite,
                        descriptor.TypeId, 1, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.None),
                    new NativeBlackboardAccessBindingV2(0, 1, BlackboardScope.Agent, 1, NativeBlackboardAccessModeV2.ReadWrite,
                        descriptor.TypeId, 1, 0, CompiledIndex.Invalid, NativeBlackboardReductionKindV2.None),
                };
                var placeholder = new NativeBlackboardScopeBindingV2(
                    BlackboardScope.Agent, "aibt.test.agent", 1, 0, 2,
                    MultiSchemaBytes(BlackboardScope.Agent, "aibt.test.agent", slots, authorities),
                    new byte[] { 1 });
                var scope = BuildScope(placeholder, semantic, slots, authorities);
                var scopes = new[] { scope };
                return new NativeProgramBlackboardBindingV2(
                    semantic, OuterBytes(semantic, scopes, slots, authorities, accesses), scopes, slots,
                    authorities, accesses, Array.Empty<NativeBlackboardWatchedSlotBindingV2>(),
                    Array.Empty<NativeRegisteredBlackboardTypeBindingV2>(), Array.Empty<NativeRegisteredBlackboardFieldBindingV2>());
            }

            internal static NativeBlackboardSlotBindingV2 CopySlot(
                NativeBlackboardSlotBindingV2 source,
                uint scopeSlotIndex,
                uint offset)
                => new NativeBlackboardSlotBindingV2(
                    source.StableKeyId, source.TypeId, source.TypeVersion, source.EnumContractId,
                    source.Scope, scopeSlotIndex, offset, source.Size, source.Alignment,
                    source.DefaultOffset, source.DefaultSize, source.AccessFlags,
                    source.ScopeDescriptorIndex, source.RegisteredTypeIndex, source.Reduction);

            internal static NativeBlackboardSlotBindingV2 CopySlot(
                NativeBlackboardSlotBindingV2 source,
                uint scopeSlotIndex,
                uint offset,
                uint scopeDescriptorIndex)
                => new NativeBlackboardSlotBindingV2(
                    source.StableKeyId, source.TypeId, source.TypeVersion, source.EnumContractId,
                    source.Scope, scopeSlotIndex, offset, source.Size, source.Alignment,
                    source.DefaultOffset, source.DefaultSize, source.AccessFlags,
                    scopeDescriptorIndex, source.RegisteredTypeIndex, source.Reduction);

            internal static CompiledProgram RebuildSemantic(
                CompiledProgram source,
                IReadOnlyList<NativeBlackboardSlotBindingV2> slots)
                => RebuildSemantic(source, slots, source.Header.CapabilityFlags);

            internal static CompiledProgram RebuildSemantic(
                CompiledProgram source,
                IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
                uint capabilities)
            {
                var semanticSlots = new CompiledBlackboardSlotRecord[slots.Count];
                for (var index = 0; index < slots.Count; index++)
                {
                    var slot = slots[index];
                    semanticSlots[index] = new CompiledBlackboardSlotRecord(
                        slot.StableKeyId, slot.TypeId, slot.TypeVersion, slot.EnumContractId,
                        slot.Scope, slot.Offset, slot.Size, slot.Alignment, slot.DefaultOffset, slot.AccessFlags);
                }
                var defaults = new byte[source.DefaultValueBlob.Count];
                for (var index = 0; index < defaults.Length; index++) defaults[index] = source.DefaultValueBlob[index];
                var preliminary = Build(Hash('0'), source.Nodes, semanticSlots, defaults, source.DebugMap, capabilities);
                return Build(ComputeHash(preliminary), source.Nodes, semanticSlots, defaults, source.DebugMap, capabilities);
            }

            internal static NativeBlackboardScopeBindingV2 BuildScope(
                NativeBlackboardScopeBindingV2 source,
                CompiledProgram semantic,
                IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
                IReadOnlyList<NativeBlackboardSlotAuthorityV2> authorities)
            {
                var schema = MultiSchemaBytes(source.Scope, source.ContractId, slots, authorities);
                var schemaHash = new CompiledHash(StableHash.Sha256Hex(schema));
                var layout = MultiLayoutBytes(source.Scope, source.ContractId, schemaHash, semantic, slots, authorities);
                return new NativeBlackboardScopeBindingV2(
                    source.Scope, source.ContractId, source.ContractVersion, 0, (uint)slots.Count, schema, layout);
            }

            private static byte[] MultiSchemaBytes(
                BlackboardScope scope, string contractId,
                IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
                IReadOnlyList<NativeBlackboardSlotAuthorityV2> authorities)
            {
                var writer = new Writer(); writer.String("aibt.blackboard-scope"); writer.U32(1); writer.U8(Scope(scope));
                writer.String(contractId); writer.U32(1); writer.U32((uint)slots.Count);
                for (var index = 0; index < slots.Count; index++)
                { var slot = slots[index]; var authority = authorities[index]; writer.String(authority.CanonicalKeyId); writer.String(authority.CanonicalTypeId); writer.U32(slot.TypeVersion); writer.String(authority.EnumContract); writer.Bytes(authority.GetCanonicalDefaultJsonCopy()); writer.U8((byte)slot.Reduction); }
                return writer.ToArray();
            }

            private static byte[] MultiLayoutBytes(
                BlackboardScope scope, string contractId, CompiledHash schemaHash, CompiledProgram semantic,
                IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
                IReadOnlyList<NativeBlackboardSlotAuthorityV2> authorities)
            {
                var writer = new Writer(); writer.String("aibt.blackboard-layout"); writer.U32(1); writer.U8(Scope(scope));
                writer.String(contractId); writer.U32(1); writer.Hash(schemaHash); writer.U32((uint)slots.Count);
                for (var index = 0; index < slots.Count; index++)
                { var slot = slots[index]; writer.String(authorities[index].CanonicalKeyId); writer.U32(slot.ScopeSlotIndex); writer.U64(slot.TypeId); writer.U32(slot.TypeVersion); writer.U64(slot.EnumContractId); writer.U32(slot.Offset); writer.U32(slot.Size); writer.U32(slot.Alignment); writer.Bytes(semantic.DefaultValueBlob, slot.DefaultOffset, slot.DefaultSize); writer.U8((byte)slot.Reduction); }
                return writer.ToArray();
            }

            private static CompiledProgram CreateProgram(
                BlackboardTypeDescriptor descriptor,
                byte[] defaultBytes,
                BlackboardScope scope,
                ulong enumContractId)
            {
                var nodes = new[]
                {
                    new CompiledNodeRecord(
                        StableHash.Fnv1A64("aibt.test.success"), 1,
                        0, 0, 1, 0, 0, 1, NodeMemoryLifetime.Activation,
                        new CompiledRange(0, 0), CompiledNodeFlags.BurstDomain, 0,
                        new CompiledRange(0, 0), new CompiledRange(0, 0)),
                };
                var slots = new CompiledBlackboardSlotRecord[1];
                var debug = new[] { new CompiledDebugMapEntry(0, new NodeId("root"), "/root") };
                slots[0] = new CompiledBlackboardSlotRecord(
                    StableHash.Fnv1A64("health"), descriptor.TypeId, descriptor.Version, enumContractId,
                    scope, 0, (uint)descriptor.Size, (uint)descriptor.Alignment, 0,
                    CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Write);
                var capabilities = scope == BlackboardScope.Agent ? 1u << 7
                    : scope == BlackboardScope.Shared ? 1u << 8 : 0;
                var preliminary = Build(Hash('0'), nodes, slots, defaultBytes, debug, capabilities);
                return Build(ComputeHash(preliminary), nodes, slots, defaultBytes, debug, capabilities);
            }

            private static CompiledProgram Build(
                CompiledHash contentHash,
                IReadOnlyList<CompiledNodeRecord> nodes,
                IReadOnlyList<CompiledBlackboardSlotRecord> slots,
                byte[] defaultBytes,
                IReadOnlyList<CompiledDebugMapEntry> debug,
                uint capabilities)
            {
                var header = new CompiledProgramHeader(
                    2, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                    Hash('a'), Hash('b'), Hash('c'), 1, contentHash,
                    0, 1, 0, (uint)slots.Count, 1, 0, 0, 1,
                    capabilities, true);
                return new CompiledProgram(
                    header, nodes, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<uint>(), slots,
                    Array.Empty<CompiledObserverRecord>(), Array.Empty<uint>(), Array.Empty<byte>(),
                    defaultBytes, debug);
            }

            private static byte[] Bytes<T>(T value) where T : unmanaged
            {
                var native = new NativeArray<T>(1, Allocator.Temp);
                try
                {
                    native[0] = value;
                    var source = native.Reinterpret<byte>(UnsafeUtility.SizeOf<T>());
                    var result = new byte[source.Length];
                    for (var index = 0; index < source.Length; index++) result[index] = source[index];
                    return result;
                }
                finally { native.Dispose(); }
            }

            private static CompiledHash Hash(char value) => new CompiledHash(new string(value, 64));

            private static string CanonicalTypeId(BlackboardTypeDescriptor descriptor)
            {
                if (descriptor.ValueType != BlackboardValueType.Registered) return descriptor.ValueType.ToString();
                foreach (var value in new[] { "aibt.test.registered", "aibt.test.outer", "aibt.test.float-outer", "aibt.test.alpha", "aibt.test.beta" })
                    if (StableHash.Fnv1A64(value) == descriptor.TypeId) return value;
                throw new InvalidOperationException("The registered fixture requires an exact canonical type ID.");
            }

            private static byte[] SchemaBytes(
                BlackboardScope scope, string contractId, string canonicalTypeId, string enumContract,
                byte[] canonicalDefault, NativeBlackboardReductionKindV2 reduction)
            {
                var writer = new Writer();
                writer.String("aibt.blackboard-scope"); writer.U32(1); writer.U8(Scope(scope));
                writer.String(contractId); writer.U32(1); writer.U32(1);
                writer.String("health"); writer.String(canonicalTypeId); writer.U32(1);
                writer.String(enumContract); writer.Bytes(canonicalDefault); writer.U8((byte)reduction);
                return writer.ToArray();
            }

            private static byte[] LayoutBytes(
                BlackboardScope scope, string contractId, CompiledHash schemaHash,
                BlackboardTypeDescriptor descriptor, byte[] defaults, ulong enumContractId,
                NativeBlackboardReductionKindV2 reduction)
            {
                var writer = new Writer();
                writer.String("aibt.blackboard-layout"); writer.U32(1); writer.U8(Scope(scope));
                writer.String(contractId); writer.U32(1); writer.Hash(schemaHash); writer.U32(1);
                writer.String("health"); writer.U32(0); writer.U64(descriptor.TypeId); writer.U32(descriptor.Version);
                writer.U64(enumContractId); writer.U32(0); writer.U32((uint)descriptor.Size); writer.U32((uint)descriptor.Alignment);
                writer.Bytes(defaults); writer.U8((byte)reduction);
                return writer.ToArray();
            }

            internal static byte[] OuterBytes(
                CompiledProgram program,
                IReadOnlyList<NativeBlackboardScopeBindingV2> scopes,
                IReadOnlyList<NativeBlackboardSlotBindingV2> slots,
                IReadOnlyList<NativeBlackboardSlotAuthorityV2> slotAuthorities,
                IReadOnlyList<NativeBlackboardAccessBindingV2> accesses)
            {
                var header = program.Header; var writer = new Writer();
                writer.U32(header.Magic); writer.U32(2); writer.U32(header.ExecutionSemanticsVersion);
                writer.U16(header.CompilerVersion.Major); writer.U16(header.CompilerVersion.Minor); writer.U16(header.CompilerVersion.Patch); writer.U32(header.CompilerVersion.BuildRevision);
                writer.Hash(header.CanonicalSemanticHash); writer.Hash(header.NodeRegistryHash); writer.Hash(header.CanonicalPolicyHash);
                writer.U32(header.PolicyFormatVersion); writer.U32(header.RootNodeIndex); writer.U32((uint)program.Nodes.Count); writer.U32((uint)program.ChildIndices.Count);
                writer.U32((uint)slots.Count); writer.U32((uint)program.DebugMap.Count); writer.U32((uint)program.ConfigBlob.Count); writer.U32(header.InstanceNodeMemorySize);
                writer.U32(header.RequiredMaximumAlignment); var capabilities = header.CapabilityFlags;
                for (var index = 0; index < scopes.Count; index++) capabilities |= scopes[index].Scope == BlackboardScope.Agent ? 1u << 7 : 1u << 8;
                writer.U32(capabilities); writer.U8(header.DeterministicModeCompatible ? (byte)1 : (byte)0);
                writer.U32((uint)scopes.Count);
                for (var index = 0; index < scopes.Count; index++)
                { var item = scopes[index]; writer.U8(Scope(item.Scope)); writer.String(item.ContractId); writer.U64(item.ContractNumericId); writer.U32(item.ContractVersion); writer.Hash(item.SchemaHash); writer.Hash(item.LayoutHash); writer.U32(item.FirstSlot); writer.U32(item.SlotCount); }
                for (var index = 0; index < program.Nodes.Count; index++)
                {
                    var node = program.Nodes[index]; writer.U64(node.NodeTypeId); writer.U32(node.NodeTypeVersion); writer.U32(node.ConfigOffset); writer.U32(node.ConfigSize); writer.U32(node.ConfigAlignment);
                    writer.U32(node.InstanceMemoryOffset); writer.U32(node.InstanceMemorySize); writer.U32(node.InstanceMemoryAlignment); writer.U8((byte)node.MemoryLifetime);
                    writer.U32(node.Children.Offset); writer.U32(node.Children.Count); writer.U32((uint)node.Flags); writer.U32(node.DebugIdentityIndex);
                    uint firstRead = 0, readCount = 0, firstWrite = 0, writeCount = 0; var seenRead = false; var seenWrite = false;
                    for (var accessIndex = 0; accessIndex < accesses.Count; accessIndex++) if (accesses[accessIndex].NodeIndex == index)
                    { var access = accesses[accessIndex]; if (access.Mode != NativeBlackboardAccessModeV2.Write) { if (!seenRead) { firstRead = (uint)accessIndex; seenRead = true; } readCount++; } if (access.Mode != NativeBlackboardAccessModeV2.Read) { if (!seenWrite) { firstWrite = (uint)accessIndex; seenWrite = true; } writeCount++; } }
                    writer.U32(firstRead); writer.U32(readCount); writer.U32(firstWrite); writer.U32(writeCount);
                }
                for (var index = 0; index < program.ChildIndices.Count; index++) writer.U32(program.ChildIndices[index]);
                writer.U32((uint)accesses.Count);
                for (var index = 0; index < accesses.Count; index++) { var item = accesses[index]; writer.U32(item.NodeIndex); writer.U32(item.AccessOrdinal); writer.U8(Scope(item.Scope)); writer.U32(item.SlotIndex); writer.U8((byte)item.Mode); writer.U8((byte)item.Reduction); }
                writer.U32((uint)slots.Count);
                for (var index = 0; index < slots.Count; index++) { var item = slots[index]; writer.String(slotAuthorities[index].CanonicalKeyId); writer.U64(item.StableKeyId); writer.U64(item.TypeId); writer.U32(item.TypeVersion); writer.U64(item.EnumContractId); writer.U8(Scope(item.Scope)); writer.U32(item.ScopeSlotIndex); writer.U32(item.Offset); writer.U32(item.Size); writer.U32(item.Alignment); writer.U32(item.DefaultOffset); writer.U32(item.DefaultSize); writer.U8((byte)item.AccessFlags); writer.U32(uint.MaxValue); writer.U32(0); }
                writer.U32(0); writer.U32(0); writer.Bytes(program.ConfigBlob); writer.Bytes(program.DefaultValueBlob);
                writer.U32((uint)scopes.Count); for (var index = 0; index < scopes.Count; index++) writer.Bytes(scopes[index].GetRawLayoutCopy());
                writer.U32((uint)program.DebugMap.Count); for (var index = 0; index < program.DebugMap.Count; index++) { var item = program.DebugMap[index]; writer.U32(item.RuntimeNodeIndex); writer.String(item.AuthoringNodeId.Value); writer.String(item.SourcePath); writer.String(item.DisplayName ?? string.Empty); }
                return writer.ToArray();
            }

            private static byte Scope(BlackboardScope value) => value == BlackboardScope.Tree ? (byte)0 : value == BlackboardScope.Agent ? (byte)1 : (byte)2;

            private sealed class Writer
            {
                private readonly List<byte> _bytes = new List<byte>();
                internal void U8(byte value) => _bytes.Add(value);
                internal void U16(ushort value) { U8((byte)value); U8((byte)(value >> 8)); }
                internal void U32(uint value) { U8((byte)value); U8((byte)(value >> 8)); U8((byte)(value >> 16)); U8((byte)(value >> 24)); }
                internal void U64(ulong value) { U32((uint)value); U32((uint)(value >> 32)); }
                internal void String(string value) => Bytes(Encoding.UTF8.GetBytes(value));
                internal void Raw(string value) { var bytes = Encoding.UTF8.GetBytes(value); for (var index = 0; index < bytes.Length; index++) U8(bytes[index]); }
                internal void Bytes(IReadOnlyList<byte> value) { U32((uint)value.Count); for (var index = 0; index < value.Count; index++) U8(value[index]); }
                internal void Bytes(IReadOnlyList<byte> value, uint offset, uint count) { U32(count); for (var index = 0u; index < count; index++) U8(value[(int)(offset + index)]); }
                internal void Hash(CompiledHash value) { var hex = value.HexadecimalValue; for (var index = 0; index < 64; index += 2) U8((byte)((Nibble(hex[index]) << 4) | Nibble(hex[index + 1]))); }
                internal void HashOrZero(CompiledHash value) { if (value.IsValid) Hash(value); else for (var index = 0; index < 32; index++) U8(0); }
                internal byte[] ToArray() => _bytes.ToArray();
                private static int Nibble(char value) => value <= '9' ? value - '0' : value - 'a' + 10;
            }

            private static CompiledHash ComputeHash(CompiledProgram program)
            {
                var type = typeof(CompiledProgram).Assembly.GetType("AIBT.CompiledProgramContentHashV1", true);
                var method = type.GetMethod("Compute", BindingFlags.Static | BindingFlags.NonPublic);
                return (CompiledHash)method.Invoke(null, new object[] { program });
            }
        }
    }
}
