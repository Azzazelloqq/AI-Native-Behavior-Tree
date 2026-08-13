using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ParallelContractTests
    {
        [Test]
        public void BuiltInRegistryUsesExactTypeAndVersion()
        {
            var registry = ReferenceParallelRegistry.CreatePhase1BuiltIns();

            Assert.That(registry.Contains(StableHash.Fnv1A64("aibt.core.parallel"), 1), Is.True);
            Assert.That(registry.Contains(StableHash.Fnv1A64("aibt.core.parallel"), 2), Is.False);
            Assert.That(registry.Contains(StableHash.Fnv1A64("aibt.core.unknown"), 1), Is.False);
        }

        [Test]
        public void RegistryRejectsDuplicateTypeAndVersion()
        {
            var binding = new ReferenceParallelBinding(17, 1);

            Assert.Throws<ArgumentException>(() => new ReferenceParallelRegistry(new[] { binding, binding }));
        }

        [TestCase(0, ReferenceParallelPolicy.RequireAllSuccess)]
        [TestCase(1, ReferenceParallelPolicy.RequireAnySuccess)]
        public void NonThresholdPoliciesDecodeWithZeroThresholdFields(byte persistedPolicy, ReferenceParallelPolicy expected)
        {
            var decoded = ReferenceParallelConfigurationDecoder.Decode(Configuration(persistedPolicy, 0, 0, 0), 3);

            Assert.That(decoded.Policy, Is.EqualTo(expected));
            Assert.That(decoded.SuccessThreshold, Is.Zero);
            Assert.That(decoded.FailureThreshold, Is.Zero);
        }

        [TestCase(0, ReferenceParallelTieBreak.FailureFirst)]
        [TestCase(1, ReferenceParallelTieBreak.SuccessFirst)]
        public void ThresholdPolicyDecodesTieBreak(byte persistedTieBreak, ReferenceParallelTieBreak expected)
        {
            var decoded = ReferenceParallelConfigurationDecoder.Decode(Configuration(2, 2, 2, persistedTieBreak), 3);

            Assert.That(decoded.Policy, Is.EqualTo(ReferenceParallelPolicy.Threshold));
            Assert.That(decoded.TieBreak, Is.EqualTo(expected));
        }

        [TestCase(0u, 1u, 3u)]
        [TestCase(1u, 0u, 3u)]
        [TestCase(4u, 1u, 3u)]
        [TestCase(1u, 4u, 3u)]
        [TestCase(3u, 2u, 3u)]
        public void ThresholdValidationRejectsUnreachableOrOutOfRangePolicies(uint success, uint failure, uint childCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ReferenceParallelConfigurationDecoder.Decode(Configuration(2, success, failure, 0), childCount));
        }

        [Test]
        public void EmptyParallelIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ReferenceParallelConfigurationDecoder.Decode(Configuration(0, 0, 0, 0), 0));
        }

        [Test]
        public void NonThresholdPolicyRejectsThresholdFieldsAndTieBreak()
        {
            Assert.Throws<ArgumentException>(() =>
                ReferenceParallelConfigurationDecoder.Decode(Configuration(0, 1, 0, 0), 2));
            Assert.Throws<ArgumentException>(() =>
                ReferenceParallelConfigurationDecoder.Decode(Configuration(0, 0, 0, 1), 2));
        }

        [Test]
        public void DecoderRejectsWrongSizeUndefinedEnumsAndNonZeroPadding()
        {
            Assert.Throws<ArgumentException>(() => ReferenceParallelConfigurationDecoder.Decode(new byte[15], 2));
            Assert.Throws<ArgumentException>(() => ReferenceParallelConfigurationDecoder.Decode(Configuration(3, 0, 0, 0), 2));
            Assert.Throws<ArgumentException>(() => ReferenceParallelConfigurationDecoder.Decode(Configuration(2, 1, 1, 2), 2));
            var padded = Configuration(0, 0, 0, 0);
            padded[15] = 1;
            Assert.Throws<ArgumentException>(() => ReferenceParallelConfigurationDecoder.Decode(padded, 2));
        }

        [TestCase(ReferenceParallelPolicy.RequireAllSuccess, "SS", true, NodeStatus.Success)]
        [TestCase(ReferenceParallelPolicy.RequireAllSuccess, "SR", false, NodeStatus.Running)]
        [TestCase(ReferenceParallelPolicy.RequireAllSuccess, "SF", true, NodeStatus.Failure)]
        [TestCase(ReferenceParallelPolicy.RequireAnySuccess, "SR", true, NodeStatus.Success)]
        [TestCase(ReferenceParallelPolicy.RequireAnySuccess, "FR", false, NodeStatus.Running)]
        [TestCase(ReferenceParallelPolicy.RequireAnySuccess, "FF", true, NodeStatus.Failure)]
        public void BuiltInPoliciesEvaluateOnlyAfterTheFullVisit(
            ReferenceParallelPolicy policy,
            string states,
            bool expectedTerminal,
            NodeStatus expectedStatus)
        {
            var configuration = new ReferenceParallelConfiguration(policy, 0, 0, ReferenceParallelTieBreak.FailureFirst, 2);

            var result = ReferenceParallelPolicyEvaluator.Evaluate(configuration, Branches(states));

            Assert.That(result.IsTerminal, Is.EqualTo(expectedTerminal));
            Assert.That(result.Status, Is.EqualTo(expectedStatus));
        }

        [TestCase(ReferenceParallelTieBreak.FailureFirst, NodeStatus.Failure)]
        [TestCase(ReferenceParallelTieBreak.SuccessFirst, NodeStatus.Success)]
        public void ThresholdTieUsesConfiguredDeterministicOrder(ReferenceParallelTieBreak tieBreak, NodeStatus expected)
        {
            var configuration = new ReferenceParallelConfiguration(ReferenceParallelPolicy.Threshold, 1, 1, tieBreak, 2);

            var result = ReferenceParallelPolicyEvaluator.Evaluate(configuration, Branches("SF"));

            Assert.That(result.IsTerminal, Is.True);
            Assert.That(result.Status, Is.EqualTo(expected));
        }

        private static IReadOnlyList<ReferenceParallelBranch> Branches(string states)
        {
            var result = new ReferenceParallelBranch[states.Length];
            for (var index = 0; index < states.Length; index++)
            {
                result[index] = new ReferenceParallelBranch((uint)index)
                {
                    State = states[index] == 'S'
                        ? ReferenceParallelChildState.Success
                        : states[index] == 'F'
                            ? ReferenceParallelChildState.Failure
                            : ReferenceParallelChildState.Running,
                };
            }
            return result;
        }

        private static byte[] Configuration(byte policy, uint success, uint failure, byte tieBreak)
        {
            var bytes = new byte[16];
            bytes[0] = policy;
            WriteUInt32(bytes, 4, success);
            WriteUInt32(bytes, 8, failure);
            bytes[12] = tieBreak;
            return bytes;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            for (var index = 0; index < 4; index++) bytes[offset + index] = (byte)(value >> (index * 8));
        }
    }
}
