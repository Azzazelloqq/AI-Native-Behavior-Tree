using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    internal sealed class ReferenceObserverContractTests
    {
        [Test]
        public void RegistryRequiresExactNumericTypeAndVersion()
        {
            var evaluator = new ConstantEvaluator(NodeStatus.Success);
            var registry = new ReferenceObserverConditionRegistry(new[]
            {
                new ReferenceObserverConditionBinding(17, 2, evaluator),
            });

            Assert.That(registry.TryGet(17, 2, out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(evaluator));
            Assert.That(registry.TryGet(17, 1, out _), Is.False);
            Assert.That(registry.TryGet(18, 2, out _), Is.False);
        }

        [Test]
        public void RegistryRejectsDuplicateTypeAndVersion()
        {
            var binding = new ReferenceObserverConditionBinding(17, 1, new ConstantEvaluator(NodeStatus.Success));

            Assert.Throws<ArgumentException>(() =>
                new ReferenceObserverConditionRegistry(new[] { binding, binding }));
        }

        [TestCase(0ul, 1u)]
        [TestCase(1ul, 0u)]
        public void BindingRejectsInvalidIdentity(ulong typeId, uint version)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReferenceObserverConditionBinding(typeId, version, new ConstantEvaluator(NodeStatus.Success)));
        }

        private sealed class ConstantEvaluator : IReferenceObserverConditionEvaluator
        {
            private readonly NodeStatus _status;
            internal ConstantEvaluator(NodeStatus status) { _status = status; }
            public NodeStatus Evaluate(ref ReferenceObserverConditionContext context) => _status;
        }
    }
}
