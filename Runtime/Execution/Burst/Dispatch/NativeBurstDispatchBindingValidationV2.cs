using AIBT.Burst;
using Unity.Collections;

namespace AIBT.Execution.Burst.Dispatch
{
    internal static class NativeBurstDispatchBindingValidationV2
    {
        internal static bool Validate(in NativeBurstDispatchCreateInputV2 input)
        {
            for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
            {
                var request = input.Requests[requestIndex];
                if (!request.TreeInstanceId.IsValid
                    || !new RuntimeNodeIndex(request.RuntimeNodeIndex).IsValid
                    || request.ActivationGeneration == 0
                    || !Range(request.FirstResolvedBinding, request.ResolvedBindingCount, input.BindingInput.IsEnabled
                        ? input.BindingInput.ResolvedBindings.Length
                        : 0))
                {
                    return false;
                }

                var dispatchCase = input.Cases[(int)request.CatalogCaseIndex];
                if (!ValidateCanonicalStorage(
                        input.ConfigurationFields,
                        dispatchCase.FirstConfigurationField,
                        dispatchCase.ConfigurationFieldCount,
                        input.ConfigurationBytes,
                        request.ConfigurationOffset,
                        dispatchCase.ConfigurationSize,
                        true)
                    || !ValidateCanonicalStorage(
                        input.MemoryFields,
                        dispatchCase.FirstMemoryField,
                        dispatchCase.MemoryFieldCount,
                        input.MemoryBytes,
                        request.MemoryOffset,
                        dispatchCase.MemorySize,
                        false))
                {
                    return false;
                }
            }

            var bindingInput = input.BindingInput;
            if (!bindingInput.IsEnabled)
            {
                if (bindingInput.ValueFields.IsCreated
                    || bindingInput.ResolvedBindings.IsCreated
                    || bindingInput.LiveValueBytes.IsCreated
                    || bindingInput.Completions.IsCreated
                    || bindingInput.CompletionPayloadBytes.IsCreated
                    || HasCapacity(bindingInput.Capacity))
                {
                    return false;
                }

                for (var caseIndex = 0; caseIndex < input.Cases.Length; caseIndex++)
                {
                    if (input.Cases[caseIndex].FirstBinding != 0
                        || input.Cases[caseIndex].BindingCount != 0)
                    {
                        return false;
                    }
                }

                return ValidateCanonicalMetadata(in input);
            }

            if (!bindingInput.ValueFields.IsCreated
                || !bindingInput.ResolvedBindings.IsCreated
                || !bindingInput.LiveValueBytes.IsCreated
                || !bindingInput.Completions.IsCreated
                || !bindingInput.CompletionPayloadBytes.IsCreated
                || !input.CanonicalInput.CaseRanges.IsCreated
                || !input.CanonicalInput.BindingRanges.IsCreated
                || !input.CanonicalInput.Rules.IsCreated
                || !FitsInt(bindingInput.Capacity.MaxValueSessionsPerFrame)
                || !FitsInt(bindingInput.Capacity.MaxValueStagingBytesPerFrame)
                || !FitsInt(bindingInput.Capacity.MaxCommands)
                || !FitsInt(bindingInput.Capacity.MaxCommandPayloadBytes)
                || !FitsInt(bindingInput.Capacity.MaxOperations)
                || bindingInput.Capacity.FirstOperationSequence == 0)
            {
                return false;
            }

            uint runningBindingCursor = 0;
            for (var caseIndex = 0; caseIndex < input.Cases.Length; caseIndex++)
            {
                var dispatchCase = input.Cases[caseIndex];
                if (dispatchCase.FirstBinding != runningBindingCursor
                    || !Range(dispatchCase.FirstBinding, dispatchCase.BindingCount, bindingInput.Bindings.Length))
                {
                    return false;
                }

                runningBindingCursor += dispatchCase.BindingCount;

                uint generatedHandleCount = 0;
                for (uint fieldIndex = 0; fieldIndex < dispatchCase.ConfigurationFieldCount; fieldIndex++)
                {
                    var field = input.ConfigurationFields[(int)(dispatchCase.FirstConfigurationField + fieldIndex)];
                    if (field.Encoding == NativeBurstDispatchFieldEncodingV2.GeneratedHandle)
                    {
                        generatedHandleCount++;
                    }
                }

                if (generatedHandleCount != dispatchCase.BindingCount)
                {
                    return false;
                }

                for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
                {
                    var request = input.Requests[requestIndex];
                    if (request.CatalogCaseIndex == (uint)caseIndex
                        && request.ResolvedBindingCount != dispatchCase.BindingCount)
                    {
                        return false;
                    }
                }

                for (uint localOrdinal = 0; localOrdinal < dispatchCase.BindingCount; localOrdinal++)
                {
                    var globalOrdinal = dispatchCase.FirstBinding + localOrdinal;
                    var binding = bindingInput.Bindings[(int)globalOrdinal];
                    if (binding.BindingOrdinal != localOrdinal
                        || !ValidateBinding(in binding, bindingInput.ValueFields)
                        || !TryFindGeneratedHandleField(
                            input.ConfigurationFields,
                            dispatchCase.FirstConfigurationField,
                            dispatchCase.ConfigurationFieldCount,
                            binding.ConfigurationFieldOrdinal,
                            out var handleField))
                    {
                        return false;
                    }

                    for (uint priorLocal = 0; priorLocal < localOrdinal; priorLocal++)
                    {
                        if (bindingInput.Bindings[(int)(dispatchCase.FirstBinding + priorLocal)].ConfigurationFieldOrdinal
                            == binding.ConfigurationFieldOrdinal)
                        {
                            return false;
                        }
                    }

                    for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
                    {
                        var request = input.Requests[requestIndex];
                        if (request.CatalogCaseIndex != (uint)caseIndex)
                        {
                            continue;
                        }

                        var offset = (ulong)request.ConfigurationOffset + handleField.ByteOffset;
                        if (offset + 4UL > (ulong)input.ConfigurationBytes.Length
                            || ReadUInt32(input.ConfigurationBytes, (uint)offset) != localOrdinal)
                        {
                            return false;
                        }
                    }
                }
            }

            if (runningBindingCursor != bindingInput.Bindings.Length)
            {
                return false;
            }

            if (!ValidateCanonicalMetadata(in input))
            {
                return false;
            }

            uint runningResolvedCursor = 0;
            for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
            {
                var request = input.Requests[requestIndex];
                var dispatchCase = input.Cases[(int)request.CatalogCaseIndex];
                if (request.FirstResolvedBinding != runningResolvedCursor
                    || request.ResolvedBindingCount != dispatchCase.BindingCount)
                {
                    return false;
                }

                runningResolvedCursor += request.ResolvedBindingCount;
            }

            if (runningResolvedCursor != bindingInput.ResolvedBindings.Length)
            {
                return false;
            }

            for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
            {
                var request = input.Requests[requestIndex];
                var dispatchCase = input.Cases[(int)request.CatalogCaseIndex];
                for (uint localOrdinal = 0; localOrdinal < dispatchCase.BindingCount; localOrdinal++)
                {
                    var binding = bindingInput.Bindings[(int)(dispatchCase.FirstBinding + localOrdinal)];
                    var resolved = bindingInput.ResolvedBindings[(int)(request.FirstResolvedBinding + localOrdinal)];
                    var isLive = IsLiveBinding(binding.Kind);
                    if (resolved.BindingOrdinal != localOrdinal
                        || resolved.TargetOrdinal == uint.MaxValue
                        || isLive != (resolved.LiveValueOffset != NativeBurstDispatchBindingV2.NoOffset)
                        || isLive && !ValidateCanonicalStorage(
                            bindingInput.ValueFields,
                            binding.FirstPrimaryValueField,
                            binding.PrimaryValueFieldCount,
                            bindingInput.LiveValueBytes,
                            resolved.LiveValueOffset,
                            binding.PrimaryValueSize,
                            false)
                        || isLive && !ValidateBindingCanonicalBytes(
                            input.CanonicalInput,
                            dispatchCase.FirstBinding + localOrdinal,
                            false,
                            bindingInput.LiveValueBytes,
                            resolved.LiveValueOffset,
                            binding.PrimaryValueSize))
                    {
                        return false;
                    }
                }
            }

            for (var leftRequestIndex = 0; leftRequestIndex < input.Requests.Length; leftRequestIndex++)
            {
                var leftRequest = input.Requests[leftRequestIndex];
                var leftCase = input.Cases[(int)leftRequest.CatalogCaseIndex];
                for (uint leftLocal = 0; leftLocal < leftCase.BindingCount; leftLocal++)
                {
                    var left = bindingInput.Bindings[(int)(leftCase.FirstBinding + leftLocal)];
                    if (!IsLiveBinding(left.Kind))
                    {
                        continue;
                    }

                    var leftResolved = bindingInput.ResolvedBindings[(int)(leftRequest.FirstResolvedBinding + leftLocal)];
                    for (var rightRequestIndex = leftRequestIndex; rightRequestIndex < input.Requests.Length; rightRequestIndex++)
                    {
                        var rightRequest = input.Requests[rightRequestIndex];
                        var rightCase = input.Cases[(int)rightRequest.CatalogCaseIndex];
                        var rightStart = rightRequestIndex == leftRequestIndex ? leftLocal + 1 : 0;
                        for (var rightLocal = rightStart; rightLocal < rightCase.BindingCount; rightLocal++)
                        {
                            var right = bindingInput.Bindings[(int)(rightCase.FirstBinding + rightLocal)];
                            var rightResolved = bindingInput.ResolvedBindings[(int)(rightRequest.FirstResolvedBinding + rightLocal)];
                            if (!IsLiveBinding(right.Kind))
                            {
                                continue;
                            }

                            var sameIdentity = left.Scope == right.Scope
                                && leftResolved.TargetOrdinal == rightResolved.TargetOrdinal;
                            var overlap = RangesOverlap(
                                leftResolved.LiveValueOffset,
                                left.PrimaryValueSize,
                                rightResolved.LiveValueOffset,
                                right.PrimaryValueSize);
                            if (overlap && !sameIdentity
                                || sameIdentity
                                    && (!SamePrimaryLayout(
                                            in left,
                                            in right,
                                            bindingInput.ValueFields)
                                        || !SameCanonicalRules(
                                            input.CanonicalInput,
                                            leftCase.FirstBinding + leftLocal,
                                            rightCase.FirstBinding + rightLocal,
                                            false)
                                        || leftResolved.LiveValueOffset != rightResolved.LiveValueOffset))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            for (var completionIndex = 0; completionIndex < bindingInput.Completions.Length; completionIndex++)
            {
                var completion = bindingInput.Completions[completionIndex];
                if (!completion.OperationId.IsValid
                    || (byte)completion.Outcome > (byte)BurstCompletionOutcome.Cancelled
                    || completion.State != NativeBurstDispatchCompletionStateV2.Available)
                {
                    return false;
                }

                if (!TryFindCompletionBinding(
                        in input,
                        completion.TargetOrdinal,
                        in completion.OperationId,
                        out var globalBindingOrdinal,
                        out var binding)
                    || !ValidateCanonicalStorage(
                        bindingInput.ValueFields,
                        binding.FirstPrimaryValueField,
                        binding.PrimaryValueFieldCount,
                        bindingInput.CompletionPayloadBytes,
                        completion.PayloadOffset,
                        binding.PrimaryValueSize,
                        false)
                    || !ValidateBindingCanonicalBytes(
                        input.CanonicalInput,
                        globalBindingOrdinal,
                        false,
                        bindingInput.CompletionPayloadBytes,
                        completion.PayloadOffset,
                        binding.PrimaryValueSize))
                {
                    return false;
                }

                for (var prior = 0; prior < completionIndex; prior++)
                {
                    var candidate = bindingInput.Completions[prior];
                    if (candidate.TargetOrdinal == completion.TargetOrdinal
                        && candidate.OperationId == completion.OperationId)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidateBinding(
            in NativeBurstDispatchBindingV2 binding,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields)
        {
            if ((byte)binding.Kind > (byte)NativeBurstDispatchBindingKindV2.Completion
                || binding.PrimaryTypeNumericId == 0
                || binding.PrimaryTypeVersion == 0
                || !ValidateFieldLayout(
                    fields,
                    binding.FirstPrimaryValueField,
                    binding.PrimaryValueFieldCount,
                    binding.PrimaryValueSize))
            {
                return false;
            }

            var isBlackboard = binding.Kind <= NativeBurstDispatchBindingKindV2.BlackboardReadWrite;
            var expectedPhase = binding.Kind == NativeBurstDispatchBindingKindV2.EffectCommand
                ? NativeBurstDispatchBindingPhaseMaskV2.Execute
                : binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation
                    ? NativeBurstDispatchBindingPhaseMaskV2.Execute | NativeBurstDispatchBindingPhaseMaskV2.Cancel
                    : binding.Kind == NativeBurstDispatchBindingKindV2.Completion
                        ? NativeBurstDispatchBindingPhaseMaskV2.Completion
                        : NativeBurstDispatchBindingPhaseMaskV2.None;
            if (binding.PhaseMask != expectedPhase
                || isBlackboard && binding.Scope > (byte)BlackboardScope.Shared
                || !isBlackboard && binding.Scope != NativeBurstDispatchBindingV2.NoScope
                || (binding.Kind == NativeBurstDispatchBindingKindV2.BlackboardWrite
                    || binding.Kind == NativeBurstDispatchBindingKindV2.BlackboardReadWrite)
                    && binding.Scope == (byte)BlackboardScope.Shared)
            {
                return false;
            }

            var isAsync = binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation;
            if (isAsync)
            {
                return binding.SecondaryTypeNumericId != 0
                    && binding.SecondaryTypeVersion != 0
                    && ValidateFieldLayout(
                        fields,
                        binding.FirstSecondaryValueField,
                        binding.SecondaryValueFieldCount,
                        binding.SecondaryValueSize);
            }

            return binding.SecondaryTypeNumericId == 0
                && binding.SecondaryTypeVersion == 0
                && binding.FirstSecondaryValueField == 0
                && binding.SecondaryValueFieldCount == 0
                && binding.SecondaryValueSize == 0;
        }

        private static bool ValidateFieldLayout(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint first,
            uint count,
            uint valueSize)
        {
            if (count == 0 || !Range(first, count, fields.Length) || valueSize == 0)
            {
                return false;
            }

            ulong priorEnd = 0;
            NativeBurstDispatchFieldV2 prior = default;
            for (uint index = 0; index < count; index++)
            {
                var field = fields[(int)(first + index)];
                var canonicalIdentity = index == 0
                    ? field.FieldOrdinal == 0 && field.FirstElementIndex == 0
                    : field.FieldOrdinal == prior.FieldOrdinal
                        ? field.FirstElementIndex == prior.FirstElementIndex + prior.ElementCount
                        : field.FieldOrdinal == prior.FieldOrdinal + 1 && field.FirstElementIndex == 0;
                var end = (ulong)field.ByteOffset + (ulong)field.ElementCount * field.ElementSize;
                if (!canonicalIdentity
                    || field.ElementCount == 0
                    || field.ElementSize == 0
                    || field.ElementSize != EncodingSize(field.Encoding)
                    || field.Encoding == NativeBurstDispatchFieldEncodingV2.GeneratedHandle
                    || field.ByteOffset < priorEnd
                    || end > valueSize)
                {
                    return false;
                }

                prior = field;
                priorEnd = end;
            }

            return true;
        }

        private static bool ValidateCanonicalStorage(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint first,
            uint count,
            NativeArray<byte>.ReadOnly bytes,
            uint baseOffset,
            uint storageSize,
            bool allowHandles)
        {
            if (!Range(first, count, fields.Length) || !Range(baseOffset, storageSize, bytes.Length))
            {
                return false;
            }

            uint priorEnd = 0;
            for (uint descriptorIndex = 0; descriptorIndex < count; descriptorIndex++)
            {
                var field = fields[(int)(first + descriptorIndex)];
                if (field.Encoding == NativeBurstDispatchFieldEncodingV2.GeneratedHandle && !allowHandles)
                {
                    return false;
                }

                for (var padding = priorEnd; padding < field.ByteOffset; padding++)
                {
                    if (bytes[(int)(baseOffset + padding)] != 0)
                    {
                        return false;
                    }
                }

                for (uint element = 0; element < field.ElementCount; element++)
                {
                    var offset = baseOffset + field.ByteOffset + element * field.ElementSize;
                    if (!ValidateScalar(bytes, offset, field.Encoding))
                    {
                        return false;
                    }
                }

                priorEnd = field.ByteOffset + field.ElementCount * field.ElementSize;
            }

            for (var padding = priorEnd; padding < storageSize; padding++)
            {
                if (bytes[(int)(baseOffset + padding)] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateScalar(
            NativeArray<byte>.ReadOnly bytes,
            uint offset,
            NativeBurstDispatchFieldEncodingV2 encoding)
        {
            switch (encoding)
            {
                case NativeBurstDispatchFieldEncodingV2.Boolean:
                    return bytes[(int)offset] <= 1;
                case NativeBurstDispatchFieldEncodingV2.Float32:
                {
                    var bits = ReadUInt32(bytes, offset);
                    return bits != 0x80000000u && (bits & 0x7f800000u) != 0x7f800000u;
                }
                case NativeBurstDispatchFieldEncodingV2.Float64:
                {
                    var bits = ReadUInt64(bytes, offset);
                    return bits != 0x8000000000000000UL
                        && (bits & 0x7ff0000000000000UL) != 0x7ff0000000000000UL;
                }
                case NativeBurstDispatchFieldEncodingV2.GeneratedHandle:
                    return ReadUInt32(bytes, offset) != uint.MaxValue;
                default:
                    return EncodingSize(encoding) != 0;
            }
        }

        private static bool TryFindGeneratedHandleField(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint first,
            uint count,
            uint fieldOrdinal,
            out NativeBurstDispatchFieldV2 field)
        {
            field = default;
            var found = false;
            for (uint index = 0; index < count; index++)
            {
                var candidate = fields[(int)(first + index)];
                if (candidate.FieldOrdinal != fieldOrdinal)
                {
                    continue;
                }

                if (found
                    || candidate.FirstElementIndex != 0
                    || candidate.ElementCount != 1
                    || candidate.ElementSize != 4
                    || candidate.Encoding != NativeBurstDispatchFieldEncodingV2.GeneratedHandle)
                {
                    return false;
                }

                field = candidate;
                found = true;
            }

            return found;
        }

        private static bool ValidateCanonicalMetadata(in NativeBurstDispatchCreateInputV2 input)
        {
            var bindingInput = input.BindingInput;
            var canonical = input.CanonicalInput;
            if (!ValidateShapeMetadata(
                    input.Cases,
                    input.ConfigurationFields,
                    input.MemoryFields,
                    bindingInput.Bindings,
                    bindingInput.ValueFields,
                    in canonical))
            {
                return false;
            }

            if (!canonical.IsCreated)
            {
                return true;
            }

            for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
            {
                var request = input.Requests[requestIndex];
                var dispatchCase = input.Cases[(int)request.CatalogCaseIndex];
                var configurationRange = canonical.CaseRanges[(int)request.CatalogCaseIndex * 2];
                var memoryRange = canonical.CaseRanges[(int)request.CatalogCaseIndex * 2 + 1];
                if (!NativeBurstDispatchCanonicalV2.ValidateBytes(
                        canonical.Rules,
                        in configurationRange,
                        input.ConfigurationBytes,
                        request.ConfigurationOffset,
                        dispatchCase.ConfigurationSize)
                    || !NativeBurstDispatchCanonicalV2.ValidateBytes(
                        canonical.Rules,
                        in memoryRange,
                        input.MemoryBytes,
                        request.MemoryOffset,
                        dispatchCase.MemorySize,
                        NativeBurstDispatchCanonicalStoragePolicyV2.AllowZeroOpaqueSentinel))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool ValidateShapeMetadata(
            NativeArray<NativeBurstDispatchCaseV2>.ReadOnly cases,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly configurationFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly memoryFields,
            NativeArray<NativeBurstDispatchBindingV2>.ReadOnly bindings,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly valueFields,
            in NativeBurstDispatchCanonicalInputV2 canonical)
        {
            var bindingEnabled = bindings.IsCreated;
            if (!bindingEnabled && valueFields.IsCreated)
            {
                return false;
            }

            uint bindingCursor = 0;
            for (var caseIndex = 0; caseIndex < cases.Length; caseIndex++)
            {
                var dispatchCase = cases[caseIndex];
                if (dispatchCase.FirstBinding != bindingCursor
                    || !Range(dispatchCase.FirstBinding, dispatchCase.BindingCount,
                        bindingEnabled ? bindings.Length : 0))
                {
                    return false;
                }

                uint generatedHandleCount = 0;
                for (uint fieldIndex = 0; fieldIndex < dispatchCase.ConfigurationFieldCount; fieldIndex++)
                {
                    var field = configurationFields[(int)(dispatchCase.FirstConfigurationField + fieldIndex)];
                    if (field.Encoding == NativeBurstDispatchFieldEncodingV2.GeneratedHandle)
                    {
                        generatedHandleCount++;
                    }
                }

                if (generatedHandleCount != dispatchCase.BindingCount)
                {
                    return false;
                }

                for (uint localOrdinal = 0; localOrdinal < dispatchCase.BindingCount; localOrdinal++)
                {
                    var binding = bindings[(int)(dispatchCase.FirstBinding + localOrdinal)];
                    if (binding.BindingOrdinal != localOrdinal
                        || !ValidateBinding(in binding, valueFields)
                        || !TryFindGeneratedHandleField(
                            configurationFields,
                            dispatchCase.FirstConfigurationField,
                            dispatchCase.ConfigurationFieldCount,
                            binding.ConfigurationFieldOrdinal,
                            out _))
                    {
                        return false;
                    }

                    for (uint priorLocal = 0; priorLocal < localOrdinal; priorLocal++)
                    {
                        if (bindings[(int)(dispatchCase.FirstBinding + priorLocal)].ConfigurationFieldOrdinal
                            == binding.ConfigurationFieldOrdinal)
                        {
                            return false;
                        }
                    }
                }

                bindingCursor += dispatchCase.BindingCount;
            }

            if (bindingCursor != (bindingEnabled ? bindings.Length : 0))
            {
                return false;
            }

            if (!canonical.IsCreated)
            {
                return !HasCanonicalAnnotations(configurationFields, memoryFields, valueFields)
                    && !bindingEnabled;
            }

            if (!canonical.BindingRanges.IsCreated
                || !canonical.Rules.IsCreated
                || (long)cases.Length * 2L != canonical.CaseRanges.Length
                || (long)(bindingEnabled ? bindings.Length : 0) * 2L
                    != canonical.BindingRanges.Length)
            {
                return false;
            }

            uint ruleCursor = 0;
            for (var caseIndex = 0; caseIndex < cases.Length; caseIndex++)
            {
                var dispatchCase = cases[caseIndex];
                var configurationRange = canonical.CaseRanges[caseIndex * 2];
                var memoryRange = canonical.CaseRanges[caseIndex * 2 + 1];
                if (!ValidateCanonicalRange(
                        canonical.Rules,
                        in configurationRange,
                        ref ruleCursor,
                        configurationFields,
                        dispatchCase.FirstConfigurationField,
                        dispatchCase.ConfigurationFieldCount,
                        dispatchCase.ConfigurationSize)
                    || !ValidateCanonicalRange(
                        canonical.Rules,
                        in memoryRange,
                        ref ruleCursor,
                        memoryFields,
                        dispatchCase.FirstMemoryField,
                        dispatchCase.MemoryFieldCount,
                        dispatchCase.MemorySize))
                {
                    return false;
                }
            }

            for (var bindingIndex = 0; bindingIndex < (bindingEnabled ? bindings.Length : 0); bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                var primaryRange = canonical.BindingRanges[bindingIndex * 2];
                var secondaryRange = canonical.BindingRanges[bindingIndex * 2 + 1];
                if (!ValidateCanonicalRange(
                        canonical.Rules,
                        in primaryRange,
                        ref ruleCursor,
                        valueFields,
                        binding.FirstPrimaryValueField,
                        binding.PrimaryValueFieldCount,
                        binding.PrimaryValueSize)
                    || !NativeBurstDispatchCanonicalV2.ValidateTopLevelRule(
                        binding.PrimaryTypeNumericId,
                        binding.PrimaryTypeVersion,
                        binding.PrimaryValueSize,
                        canonical.Rules,
                        in primaryRange)
                    || !ValidateCanonicalRange(
                        canonical.Rules,
                        in secondaryRange,
                        ref ruleCursor,
                        valueFields,
                        binding.FirstSecondaryValueField,
                        binding.SecondaryValueFieldCount,
                        binding.SecondaryValueSize)
                    || binding.Kind != NativeBurstDispatchBindingKindV2.AsyncOperation
                        && secondaryRange.RuleCount != 0
                    || binding.Kind == NativeBurstDispatchBindingKindV2.AsyncOperation
                        && !NativeBurstDispatchCanonicalV2.ValidateTopLevelRule(
                            binding.SecondaryTypeNumericId,
                            binding.SecondaryTypeVersion,
                            binding.SecondaryValueSize,
                            canonical.Rules,
                            in secondaryRange))
                {
                    return false;
                }
            }

            return ruleCursor == canonical.Rules.Length;
        }

        private static bool HasCanonicalAnnotations(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly configurationFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly memoryFields,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly valueFields)
            => HasCanonicalAnnotations(configurationFields)
                || HasCanonicalAnnotations(memoryFields)
                || valueFields.IsCreated && HasCanonicalAnnotations(valueFields);

        private static bool HasCanonicalAnnotations(
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields)
        {
            for (var index = 0; index < fields.Length; index++)
            {
                if (fields[index].CanonicalRuleKind
                    != NativeBurstDispatchCanonicalRuleKindV2.None)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidateCanonicalRange(
            NativeArray<NativeBurstDispatchCanonicalRuleV2>.ReadOnly rules,
            in NativeBurstDispatchCanonicalRangeV2 range,
            ref uint cursor,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields,
            uint firstField,
            uint fieldCount,
            uint storageSize)
        {
            if (range.FirstRule != cursor
                || !NativeBurstDispatchCanonicalV2.ValidateRuleLayout(
                    rules,
                    in range,
                    fields,
                    firstField,
                    fieldCount,
                    storageSize))
            {
                return false;
            }

            cursor += range.RuleCount;
            return true;
        }

        private static bool SamePrimaryLayout(
            in NativeBurstDispatchBindingV2 left,
            in NativeBurstDispatchBindingV2 right,
            NativeArray<NativeBurstDispatchFieldV2>.ReadOnly fields)
        {
            if (left.PrimaryTypeNumericId != right.PrimaryTypeNumericId
                || left.PrimaryTypeVersion != right.PrimaryTypeVersion
                || left.PrimaryValueFieldCount != right.PrimaryValueFieldCount
                || left.PrimaryValueSize != right.PrimaryValueSize)
            {
                return false;
            }

            for (uint index = 0; index < left.PrimaryValueFieldCount; index++)
            {
                var leftField = fields[(int)(left.FirstPrimaryValueField + index)];
                var rightField = fields[(int)(right.FirstPrimaryValueField + index)];
                if (leftField.FieldOrdinal != rightField.FieldOrdinal
                    || leftField.FirstElementIndex != rightField.FirstElementIndex
                    || leftField.ByteOffset != rightField.ByteOffset
                    || leftField.ElementCount != rightField.ElementCount
                    || leftField.ElementSize != rightField.ElementSize
                    || leftField.Encoding != rightField.Encoding
                    || leftField.CanonicalRuleKind != rightField.CanonicalRuleKind)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameCanonicalRules(
            in NativeBurstDispatchCanonicalInputV2 canonical,
            uint leftBindingOrdinal,
            uint rightBindingOrdinal,
            bool secondary)
        {
            var side = secondary ? 1u : 0u;
            var leftRange = canonical.BindingRanges[(int)(leftBindingOrdinal * 2 + side)];
            var rightRange = canonical.BindingRanges[(int)(rightBindingOrdinal * 2 + side)];
            if (leftRange.RuleCount != rightRange.RuleCount)
            {
                return false;
            }

            for (uint index = 0; index < leftRange.RuleCount; index++)
            {
                var left = canonical.Rules[(int)(leftRange.FirstRule + index)];
                var right = canonical.Rules[(int)(rightRange.FirstRule + index)];
                if (left.Kind != right.Kind || left.ByteOffset != right.ByteOffset)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateBindingCanonicalBytes(
            in NativeBurstDispatchCanonicalInputV2 canonical,
            uint globalBindingOrdinal,
            bool secondary,
            NativeArray<byte>.ReadOnly bytes,
            uint baseOffset,
            uint storageSize)
        {
            var range = canonical.BindingRanges[(int)(globalBindingOrdinal * 2 + (secondary ? 1u : 0u))];
            return NativeBurstDispatchCanonicalV2.ValidateBytes(
                canonical.Rules,
                in range,
                bytes,
                baseOffset,
                storageSize);
        }

        private static bool TryFindCompletionBinding(
            in NativeBurstDispatchCreateInputV2 input,
            uint targetOrdinal,
            in OperationId operationId,
            out uint globalBindingOrdinal,
            out NativeBurstDispatchBindingV2 binding)
        {
            globalBindingOrdinal = default;
            binding = default;
            var found = false;
            for (var requestIndex = 0; requestIndex < input.Requests.Length; requestIndex++)
            {
                var request = input.Requests[requestIndex];
                if (request.TreeInstanceId != operationId.TreeInstanceId
                    || request.RuntimeNodeIndex != operationId.NodeIndex.Value
                    || request.ActivationGeneration != operationId.ActivationGeneration)
                {
                    continue;
                }

                var dispatchCase = input.Cases[(int)request.CatalogCaseIndex];
                for (uint localOrdinal = 0; localOrdinal < dispatchCase.BindingCount; localOrdinal++)
                {
                    var resolved = input.BindingInput.ResolvedBindings[(int)(request.FirstResolvedBinding + localOrdinal)];
                    var candidateGlobalOrdinal = dispatchCase.FirstBinding + localOrdinal;
                    var candidate = input.BindingInput.Bindings[(int)candidateGlobalOrdinal];
                    if (resolved.TargetOrdinal != targetOrdinal
                        || candidate.Kind != NativeBurstDispatchBindingKindV2.Completion)
                    {
                        continue;
                    }

                    if (found
                        && (!SamePrimaryLayout(
                                in candidate,
                                in binding,
                                input.BindingInput.ValueFields)
                            || !SameCanonicalRules(
                                input.CanonicalInput,
                                candidateGlobalOrdinal,
                                globalBindingOrdinal,
                                false)))
                    {
                        globalBindingOrdinal = default;
                        binding = default;
                        return false;
                    }

                    globalBindingOrdinal = candidateGlobalOrdinal;
                    binding = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static bool IsLiveBinding(NativeBurstDispatchBindingKindV2 kind)
            => kind <= NativeBurstDispatchBindingKindV2.SnapshotRead;

        private static bool HasCapacity(NativeBurstDispatchBindingCapacityV2 value)
            => value.MaxValueSessionsPerFrame != 0
                || value.MaxValueStagingBytesPerFrame != 0
                || value.MaxCommands != 0
                || value.MaxCommandPayloadBytes != 0
                || value.MaxOperations != 0
                || value.FirstOperationSequence != 0;

        private static bool FitsInt(uint value) => value <= int.MaxValue;

        private static bool Range(uint offset, uint count, int length)
            => (ulong)offset + count <= (ulong)length;

        private static bool RangesOverlap(uint leftOffset, uint leftCount, uint rightOffset, uint rightCount)
            => (ulong)leftOffset < (ulong)rightOffset + rightCount
                && (ulong)rightOffset < (ulong)leftOffset + leftCount;

        private static uint EncodingSize(NativeBurstDispatchFieldEncodingV2 encoding)
        {
            switch (encoding)
            {
                case NativeBurstDispatchFieldEncodingV2.Boolean:
                case NativeBurstDispatchFieldEncodingV2.Int8:
                case NativeBurstDispatchFieldEncodingV2.UInt8:
                    return 1;
                case NativeBurstDispatchFieldEncodingV2.Int16:
                case NativeBurstDispatchFieldEncodingV2.UInt16:
                    return 2;
                case NativeBurstDispatchFieldEncodingV2.Int32:
                case NativeBurstDispatchFieldEncodingV2.UInt32:
                case NativeBurstDispatchFieldEncodingV2.Float32:
                case NativeBurstDispatchFieldEncodingV2.GeneratedHandle:
                    return 4;
                case NativeBurstDispatchFieldEncodingV2.Int64:
                case NativeBurstDispatchFieldEncodingV2.UInt64:
                case NativeBurstDispatchFieldEncodingV2.Float64:
                    return 8;
                default:
                    return 0;
            }
        }

        private static uint ReadUInt32(NativeArray<byte>.ReadOnly bytes, uint offset)
            => bytes[(int)offset]
                | (uint)bytes[(int)(offset + 1)] << 8
                | (uint)bytes[(int)(offset + 2)] << 16
                | (uint)bytes[(int)(offset + 3)] << 24;

        private static ulong ReadUInt64(NativeArray<byte>.ReadOnly bytes, uint offset)
            => ReadUInt32(bytes, offset) | (ulong)ReadUInt32(bytes, offset + 4) << 32;
    }
}
