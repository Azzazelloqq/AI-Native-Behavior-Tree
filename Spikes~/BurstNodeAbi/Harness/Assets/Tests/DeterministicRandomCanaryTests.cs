using AIBT.Burst;
using AIBT.BurstAbi.Canary;
using AIBT.BurstAbi.Feasibility;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace AIBT.BurstAbi.Tests
{
    public sealed class DeterministicRandomCanaryTests
    {
        private const ulong LifecycleCatalog = 0x13579bdf2468ace0ul;

        [BurstCompile(CompileSynchronously = true)]
        private struct PublishedVectorJob : IJob
        {
            public ulong RootSeed;
            public DeterministicSemanticHashCanary SemanticHash;
            public ulong TreeInstanceId;
            public uint RuntimeNodeIndex;
            public NativeArray<uint> Outputs;
            public NativeArray<int> Success;

            [BurstCompile(CompileSynchronously = true)]
            public void Execute()
            {
                if (!DeterministicRandomCanary.TryCreate(
                        RootSeed,
                        in SemanticHash,
                        TreeInstanceId,
                        RuntimeNodeIndex,
                        out DeterministicRandomCanary random))
                {
                    Success[0] = 0;
                    return;
                }

                for (int index = 0; index < 6; index++)
                {
                    if (!random.TryNextUInt32(out uint value))
                    {
                        Success[0] = 0;
                        return;
                    }

                    Outputs[index] = value;
                }

                Success[0] = 1;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct EnterTickContinuityJob : IJob
        {
            public NativeArray<int> Failure;
            public NativeArray<uint> Outputs;

            [BurstCompile(CompileSynchronously = true)]
            public void Execute()
            {
                BurstExecutionBatch batch = BurstContractTestSeam.Batch(
                    LifecycleCatalog, 1u, 0u, 0ul, 0ul, 0ul, 0ul, 0ul, 0ul, true);
                var hash = new DeterministicSemanticHashCanary(0ul, 0ul, 0ul, 0ul);
                if (!BurstContractTestSeam.SetRandom(ref batch, 0ul, in hash, 1ul, 0u, true))
                {
                    Fail(ref batch, 1);
                    return;
                }

                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                BurstContractTestSeam.SetWorkCount(ref batch, 3u);
                BurstContractTestSeam.SetExecutionRequest(ref batch, 0u, 0u, BurstCallbackPhase.Enter);
                uint enterValue = 0u;
                if (BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch, 0u, 0u, 0u, BurstCallbackPhase.Enter, out BurstDispatchFrame enterFrame)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateEnterContext(in enterFrame, out BurstEnterContext enterContext)
                    != BurstContextResult.Success
                    || enterContext.TryNextUInt32(out enterValue) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteEnter(ref batch, in enterFrame, ref enterContext)
                    != BurstContextResult.Success)
                {
                    Fail(ref batch, 2);
                    return;
                }
                Outputs[0] = enterValue;

                BurstContractTestSeam.SetExecutionRequest(ref batch, 1u, 0u, BurstCallbackPhase.Tick);
                if (!TryCompleteTick(ref batch, 1u, 1, 3))
                    return;

                BurstContractTestSeam.SetExecutionRequest(ref batch, 2u, 0u, BurstCallbackPhase.Tick);
                if (!TryCompleteTick(ref batch, 2u, 2, 4))
                    return;

                if (BurstContractTestSeam.RandomState(in batch) == initialState)
                {
                    Fail(ref batch, 5);
                    return;
                }

                BurstContractTestSeam.Release(ref batch);
            }

            private bool TryCompleteTick(
                ref BurstExecutionBatch batch,
                uint instanceOrdinal,
                int outputIndex,
                int failureCode)
            {
                uint value = 0u;
                if (BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch, instanceOrdinal, 0u, 0u, BurstCallbackPhase.Tick, out BurstDispatchFrame frame)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateTickContext(in frame, out BurstTickContext context)
                    != BurstContextResult.Success
                    || context.TryNextUInt32(out value) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref context, AIBT.NodeStatus.Running)
                    != BurstContextResult.Success)
                {
                    Fail(ref batch, failureCode);
                    return false;
                }

                Outputs[outputIndex] = value;
                return true;
            }

            private void Fail(ref BurstExecutionBatch batch, int code)
            {
                Failure[0] = code;
                BurstContractTestSeam.Release(ref batch);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct ContextValidationJob : IJob
        {
            public NativeArray<int> Failure;

            [BurstCompile(CompileSynchronously = true)]
            public void Execute()
            {
                BurstTickContext defaultContext = default;
                if (defaultContext.TryNextUInt32(0u, out uint defaultValue) != BurstContextResult.InvalidHandle
                    || defaultValue != 0u)
                {
                    Failure[0] = 1;
                    return;
                }

                if (!VerifyCapabilityPrecedesBoundValidation())
                    return;
                if (!VerifyZeroBoundDoesNotPublish())
                    return;
                if (!VerifyCopiedContextIsSingleClaim())
                    return;
                VerifyForgedAndCrossFrameContexts();
            }

            private bool VerifyCapabilityPrecedesBoundValidation()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 1ul, false, BurstCallbackPhase.Tick);
                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                if (!TryAcquireTick(ref batch, out BurstDispatchFrame frame, out BurstTickContext context)
                    || context.TryNextUInt32(0u, out uint value) != BurstContextResult.PhaseViolation
                    || value != 0u
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref context, AIBT.NodeStatus.Success)
                    != BurstContextResult.PhaseViolation
                    || BurstContractTestSeam.RandomState(in batch) != initialState)
                {
                    Fail(ref batch, 2);
                    return false;
                }

                BurstContractTestSeam.Release(ref batch);
                return true;
            }

            private bool VerifyZeroBoundDoesNotPublish()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 2ul, true, BurstCallbackPhase.Tick);
                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                if (!TryAcquireTick(ref batch, out BurstDispatchFrame frame, out BurstTickContext context)
                    || context.TryNextUInt32(0u, out uint value) != BurstContextResult.InvalidStatus
                    || value != 0u
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref context, AIBT.NodeStatus.Success)
                    != BurstContextResult.InvalidStatus
                    || BurstContractTestSeam.RandomState(in batch) != initialState)
                {
                    Fail(ref batch, 3);
                    return false;
                }

                BurstContractTestSeam.Release(ref batch);
                return true;
            }

            private bool VerifyCopiedContextIsSingleClaim()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 3ul, true, BurstCallbackPhase.Tick);
                if (!TryAcquireTick(ref batch, out BurstDispatchFrame frame, out BurstTickContext context))
                {
                    Fail(ref batch, 4);
                    return false;
                }

                BurstTickContext copy = context;
                if (context.TryNextUInt32(out uint first) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref context, AIBT.NodeStatus.Success)
                    != BurstContextResult.Success
                    || copy.TryNextUInt32(out uint staleValue) != BurstContextResult.InvalidHandle
                    || staleValue != 0u
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref copy, AIBT.NodeStatus.Success)
                    != BurstContextResult.InvalidHandle
                    || first != 0x650f0350u)
                {
                    Fail(ref batch, 5);
                    return false;
                }

                BurstContractTestSeam.Release(ref batch);
                return true;
            }

            private void VerifyForgedAndCrossFrameContexts()
            {
                BurstExecutionBatch forgedBatch = CreateRandomBatch(LifecycleCatalog + 4ul, true, BurstCallbackPhase.Tick);
                ulong forgedInitialState = BurstContractTestSeam.RandomState(in forgedBatch);
                if (!TryAcquireTick(ref forgedBatch, out BurstDispatchFrame forgedFrame, out _))
                {
                    Fail(ref forgedBatch, 6);
                    return;
                }

                BurstTickContext forged = BurstContractTestSeam.ForgeTick(in forgedFrame, forgedInitialState, 2ul);
                if (forged.TryNextUInt32(out uint forgedValue) != BurstContextResult.PhaseViolation
                    || forgedValue != 0u
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(
                        ref forgedBatch, in forgedFrame, ref forged, AIBT.NodeStatus.Success)
                    != BurstContextResult.PhaseViolation
                    || BurstContractTestSeam.RandomState(in forgedBatch) != forgedInitialState)
                {
                    Fail(ref forgedBatch, 7);
                    return;
                }
                BurstContractTestSeam.Release(ref forgedBatch);

                BurstExecutionBatch sourceBatch = CreateRandomBatch(LifecycleCatalog + 5ul, true, BurstCallbackPhase.Tick);
                if (!TryAcquireTick(ref sourceBatch, out _, out BurstTickContext crossFrame))
                {
                    Fail(ref sourceBatch, 8);
                    return;
                }

                BurstExecutionBatch targetBatch = CreateRandomBatch(LifecycleCatalog + 6ul, true, BurstCallbackPhase.Tick);
                ulong targetInitialState = BurstContractTestSeam.RandomState(in targetBatch);
                if (!TryAcquireTick(ref targetBatch, out BurstDispatchFrame targetFrame, out _)
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(
                        ref targetBatch, in targetFrame, ref crossFrame, AIBT.NodeStatus.Success)
                    != BurstContextResult.InvalidHandle
                    || BurstContractTestSeam.RandomState(in targetBatch) != targetInitialState)
                {
                    BurstContractTestSeam.Release(ref sourceBatch);
                    Fail(ref targetBatch, 9);
                    return;
                }

                BurstContractTestSeam.Release(ref sourceBatch);
                BurstContractTestSeam.Release(ref targetBatch);
            }

            private static BurstExecutionBatch CreateRandomBatch(
                ulong catalog,
                bool capability,
                BurstCallbackPhase phase)
            {
                BurstExecutionBatch batch = BurstContractTestSeam.Batch(
                    catalog, 1u, 0u, 0ul, 0ul, 0ul, 0ul, 0ul, 0ul, true);
                var hash = new DeterministicSemanticHashCanary(0ul, 0ul, 0ul, 0ul);
                BurstContractTestSeam.SetRandom(ref batch, 0ul, in hash, 1ul, 0u, capability);
                BurstContractTestSeam.SetExecutionRequest(ref batch, 0u, 0u, phase);
                return batch;
            }

            private static bool TryAcquireTick(
                ref BurstExecutionBatch batch,
                out BurstDispatchFrame frame,
                out BurstTickContext context)
            {
                context = default;
                return BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                           ref batch, 0u, 0u, 0u, BurstCallbackPhase.Tick, out frame)
                       == BurstContextResult.Success
                    && BurstGeneratedRuntimeBridge.TryCreateTickContext(in frame, out context)
                       == BurstContextResult.Success;
            }

            private void Fail(ref BurstExecutionBatch batch, int code)
            {
                Failure[0] = code;
                BurstContractTestSeam.Release(ref batch);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct NonConsumingLifecycleJob : IJob
        {
            public NativeArray<int> Failure;
            public NativeArray<uint> Outputs;

            [BurstCompile(CompileSynchronously = true)]
            public void Execute()
            {
                if (!VerifyAbortDoesNotConsume())
                    return;
                if (!VerifyObserverDoesNotConsume())
                    return;
                if (!VerifyBudgetPauseDoesNotConsume())
                    return;
                VerifyFailedFrameDoesNotConsume();
            }

            private bool VerifyAbortDoesNotConsume()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 7ul, BurstCallbackPhase.Abort);
                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                if (BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch, 0u, 0u, 0u, BurstCallbackPhase.Abort, out BurstDispatchFrame frame)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateAbortContext(in frame, out _)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteAbort(ref batch, in frame)
                    != BurstContextResult.Success
                    || BurstContractTestSeam.RandomState(in batch) != initialState)
                {
                    Fail(ref batch, 1);
                    return false;
                }

                BurstContractTestSeam.Release(ref batch);
                return true;
            }

            private bool VerifyObserverDoesNotConsume()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 8ul, BurstCallbackPhase.Observer);
                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                if (BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch, 0u, 0u, 0u, BurstCallbackPhase.Observer, out BurstDispatchFrame frame)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateObserverContext(in frame, out _)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteObserver(ref batch, in frame, ConditionResult.Success)
                    != BurstContextResult.Success
                    || BurstContractTestSeam.RandomState(in batch) != initialState)
                {
                    Fail(ref batch, 2);
                    return false;
                }

                BurstContractTestSeam.Release(ref batch);
                return true;
            }

            private bool VerifyBudgetPauseDoesNotConsume()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 9ul, BurstCallbackPhase.Tick);
                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                BurstContractTestSeam.SetWorkCount(ref batch, 0u);
                if (BurstContractTestSeam.RandomState(in batch) != initialState
                    || BurstContractTestSeam.CallbackCount(in batch) != 0u)
                {
                    Fail(ref batch, 3);
                    return false;
                }

                BurstContractTestSeam.SetWorkCount(ref batch, 1u);
                uint resumedValue = 0u;
                if (BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch, 0u, 0u, 0u, BurstCallbackPhase.Tick, out BurstDispatchFrame frame)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateTickContext(in frame, out BurstTickContext context)
                    != BurstContextResult.Success
                    || context.TryNextUInt32(out resumedValue) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref context, AIBT.NodeStatus.Success)
                    != BurstContextResult.Success)
                {
                    Fail(ref batch, 4);
                    return false;
                }
                Outputs[0] = resumedValue;

                BurstContractTestSeam.Release(ref batch);
                return true;
            }

            private void VerifyFailedFrameDoesNotConsume()
            {
                BurstExecutionBatch batch = CreateRandomBatch(LifecycleCatalog + 10ul, BurstCallbackPhase.Tick);
                ulong initialState = BurstContractTestSeam.RandomState(in batch);
                uint privateValue = 0u;
                if (BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(
                        ref batch, 0u, 0u, 0u, BurstCallbackPhase.Tick, out BurstDispatchFrame frame)
                    != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryCreateTickContext(in frame, out BurstTickContext context)
                    != BurstContextResult.Success
                    || context.TryNextUInt32(out privateValue) != BurstContextResult.Success
                    || BurstGeneratedRuntimeBridge.TryFailDispatch(ref batch, in frame, BurstContextResult.CapacityExceeded)
                    != BurstContextResult.CapacityExceeded
                    || BurstContractTestSeam.RandomState(in batch) != initialState)
                {
                    Fail(ref batch, 5);
                    return;
                }
                Outputs[1] = privateValue;

                BurstContractTestSeam.Release(ref batch);
            }

            private static BurstExecutionBatch CreateRandomBatch(ulong catalog, BurstCallbackPhase phase)
            {
                BurstExecutionBatch batch = BurstContractTestSeam.Batch(
                    catalog, 1u, 0u, 0ul, 0ul, 0ul, 0ul, 0ul, 0ul, true);
                var hash = new DeterministicSemanticHashCanary(0ul, 0ul, 0ul, 0ul);
                BurstContractTestSeam.SetRandom(ref batch, 0ul, in hash, 1ul, 0u, true);
                BurstContractTestSeam.SetExecutionRequest(ref batch, 0u, 0u, phase);
                return batch;
            }

            private void Fail(ref BurstExecutionBatch batch, int code)
            {
                Failure[0] = code;
                BurstContractTestSeam.Release(ref batch);
            }
        }

        [Test]
        public void PublishedVector_ZeroInputs_FirstSixOutputsMatch()
        {
            AssertPublishedVector(
                0x0000000000000000ul,
                new DeterministicSemanticHashCanary(0ul, 0ul, 0ul, 0ul),
                1ul,
                0u,
                0x650f0350u, 0x19bf2775u, 0x93792ebdu,
                0xf8d15448u, 0x80f1bd3cu, 0x1312f9f2u);
        }

        [Test]
        public void PublishedVector_SequentialHash_FirstSixOutputsMatch()
        {
            AssertPublishedVector(
                0x0123456789abcdeful,
                new DeterministicSemanticHashCanary(
                    0x0706050403020100ul,
                    0x0f0e0d0c0b0a0908ul,
                    0x1716151413121110ul,
                    0x1f1e1d1c1b1a1918ul),
                1ul,
                42u,
                0x94286b1au, 0x4ff48da5u, 0xce86bc0du,
                0x55e6545au, 0x8ba0f814u, 0x83be6712u);
        }

        [Test]
        public void PublishedVector_MaximumInputs_FirstSixOutputsMatch()
        {
            AssertPublishedVector(
                0xfffffffffffffffful,
                new DeterministicSemanticHashCanary(
                    0xfffffffffffffffful,
                    0xfffffffffffffffful,
                    0xfffffffffffffffful,
                    0xfffffffffffffffful),
                18364758544493064720ul,
                4294967294u,
                0x56a75281u, 0x2089b2deu, 0x5e76d072u,
                0x81b053c5u, 0x0dde67a2u, 0xc869d193u);
        }

        [Test]
        public void BoundedAndFloatOperations_AdvanceExactlyAsSpecified()
        {
            DeterministicRandomCanary bounded = CreateZeroVector();
            Assert.That(bounded.TryNextUInt32(0x90000000u, out uint boundedValue), Is.True);
            Assert.That(boundedValue, Is.EqualTo(0x03792ebdu), "The first two draws must be rejected.");
            Assert.That(bounded.TryNextUInt32(out uint afterBounded), Is.True);
            Assert.That(afterBounded, Is.EqualTo(0xf8d15448u));

            DeterministicRandomCanary floating = CreateZeroVector();
            Assert.That(floating.TryNextFloat32(out float floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(0x650f03u / 16777216.0f));
            Assert.That(floating.TryNextUInt32(out uint afterFloat), Is.True);
            Assert.That(afterFloat, Is.EqualTo(0x19bf2775u));
        }

        [Test]
        public void ZeroBound_IsRejectedWithoutConsumingBeforeFloat()
        {
            DeterministicRandomCanary random = CreateZeroVector();

            Assert.That(random.TryNextUInt32(0u, out uint rejected), Is.False);
            Assert.That(rejected, Is.Zero);
            Assert.That(random.TryNextFloat32(out float value), Is.True);
            Assert.That(value, Is.EqualTo(0x650f03u / 16777216.0f));
            Assert.That(random.TryNextUInt32(out uint next), Is.True);
            Assert.That(next, Is.EqualTo(0x19bf2775u));
        }

        [Test]
        public void RestartReplaysSequence_AndReseedChangesIt()
        {
            DeterministicRandomCanary random = CreateZeroVector();
            Assert.That(random.TryNextUInt32(out uint first), Is.True);
            Assert.That(random.TryNextUInt32(out _), Is.True);

            Assert.That(random.TryRestart(), Is.True);
            Assert.That(random.TryNextUInt32(out uint restarted), Is.True);
            Assert.That(restarted, Is.EqualTo(first));

            var sequentialHash = new DeterministicSemanticHashCanary(
                0x0706050403020100ul,
                0x0f0e0d0c0b0a0908ul,
                0x1716151413121110ul,
                0x1f1e1d1c1b1a1918ul);
            Assert.That(random.TryReseed(0x0123456789abcdeful, in sequentialHash, 1ul, 42u), Is.True);
            Assert.That(random.TryNextUInt32(out uint reseeded), Is.True);
            Assert.That(reseeded, Is.EqualTo(0x94286b1au));
            Assert.That(reseeded, Is.Not.EqualTo(first));
        }

        [Test]
        public void AbortAndBudgetLifecycle_DoNotConsumeRandomValues()
        {
            DeterministicRandomCanary random = CreateZeroVector();
            DeterministicRandomCanary baseline = random;

            random.NotifyAbort();
            random.NotifyBudgetSuspended();
            random.NotifyBudgetResumed();

            Assert.That(random.TryNextUInt32(out uint actual), Is.True);
            Assert.That(baseline.TryNextUInt32(out uint expected), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual, Is.EqualTo(0x650f0350u));
        }

        [Test]
        public void BurstContexts_EnterAndOrdinaryTickReentry_ContinueOneCommittedStream()
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True, "Burst must be enabled for context lifecycle verification.");
            using (var failure = new NativeArray<int>(1, Allocator.TempJob))
            using (var outputs = new NativeArray<uint>(3, Allocator.TempJob))
            {
                new EnterTickContinuityJob { Failure = failure, Outputs = outputs }.Schedule().Complete();

                Assert.That(failure[0], Is.Zero, "Lifecycle job failed at checkpoint " + failure[0] + ".");
                Assert.That(outputs[0], Is.EqualTo(0x650f0350u));
                Assert.That(outputs[1], Is.EqualTo(0x19bf2775u));
                Assert.That(outputs[2], Is.EqualTo(0x93792ebdu));
            }
        }

        [Test]
        public void BurstContexts_ValidationPrecedence_AndSingleClaimAreEnforced()
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True, "Burst must be enabled for context validation verification.");
            using (var failure = new NativeArray<int>(1, Allocator.TempJob))
            {
                new ContextValidationJob { Failure = failure }.Schedule().Complete();
                Assert.That(failure[0], Is.Zero, "Validation job failed at checkpoint " + failure[0] + ".");
            }
        }

        [Test]
        public void BurstContexts_AbortObserverBudgetAndRejectedFrame_DoNotConsumeCommittedRandom()
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True, "Burst must be enabled for non-consuming lifecycle verification.");
            using (var failure = new NativeArray<int>(1, Allocator.TempJob))
            using (var outputs = new NativeArray<uint>(2, Allocator.TempJob))
            {
                new NonConsumingLifecycleJob { Failure = failure, Outputs = outputs }.Schedule().Complete();

                Assert.That(failure[0], Is.Zero, "Non-consuming job failed at checkpoint " + failure[0] + ".");
                Assert.That(outputs[0], Is.EqualTo(0x650f0350u), "Budget resume must observe the first stream value.");
                Assert.That(outputs[1], Is.EqualTo(0x650f0350u), "A rejected frame may advance only its private context copy.");
            }
        }

        private static DeterministicRandomCanary CreateZeroVector()
        {
            var hash = new DeterministicSemanticHashCanary(0ul, 0ul, 0ul, 0ul);
            Assert.That(
                DeterministicRandomCanary.TryCreate(0ul, in hash, 1ul, 0u, out DeterministicRandomCanary random),
                Is.True);
            return random;
        }

        private static void AssertPublishedVector(
            ulong rootSeed,
            DeterministicSemanticHashCanary semanticHash,
            ulong treeInstanceId,
            uint runtimeNodeIndex,
            uint output0,
            uint output1,
            uint output2,
            uint output3,
            uint output4,
            uint output5)
        {
            Assert.That(BurstCompiler.IsEnabled, Is.True, "Burst must be enabled for the RNG canary.");
            using (var outputs = new NativeArray<uint>(6, Allocator.TempJob))
            using (var success = new NativeArray<int>(1, Allocator.TempJob))
            {
                var job = new PublishedVectorJob
                {
                    RootSeed = rootSeed,
                    SemanticHash = semanticHash,
                    TreeInstanceId = treeInstanceId,
                    RuntimeNodeIndex = runtimeNodeIndex,
                    Outputs = outputs,
                    Success = success,
                };

                job.Schedule().Complete();

                Assert.That(success[0], Is.EqualTo(1));
                Assert.That(outputs[0], Is.EqualTo(output0));
                Assert.That(outputs[1], Is.EqualTo(output1));
                Assert.That(outputs[2], Is.EqualTo(output2));
                Assert.That(outputs[3], Is.EqualTo(output3));
                Assert.That(outputs[4], Is.EqualTo(output4));
                Assert.That(outputs[5], Is.EqualTo(output5));
            }
        }
    }
}
