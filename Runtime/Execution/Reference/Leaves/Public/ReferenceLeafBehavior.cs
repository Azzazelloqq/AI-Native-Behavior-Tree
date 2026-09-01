using System;

namespace AIBT
{
    // Public, project-implementable equivalent of the internal IReferenceLeafHandler (P7-008,
    // applying ADR-P6-017). A project assembly cannot implement IReferenceLeafHandler directly --
    // it is internal, and ReferenceNodeContext is an internal ref struct exposing raw offsets into
    // shared backing arrays. This contract and ReferenceLeafContext are the public-safe equivalents.
    public interface IReferenceLeafBehavior
    {
        void Enter(ref ReferenceLeafContext context);

        NodeStatus Tick(ref ReferenceLeafContext context);

        void Abort(ref ReferenceLeafContext context, NodeAbortReason reason);

        void Exit(ref ReferenceLeafContext context, NodeExitReason reason);
    }

    // Public-safe view over the internal ReferenceNodeContext. Holds a private by-value copy --
    // safe because Configuration/Memory are span views over arrays already held by reference, and
    // blackboard I/O is forwarded through the internal context's own captured service interface,
    // not through mutable struct state this copy could go stale on.
    //
    // v1 scope: no async-operation support (TryStartOperation/TryConsumeCompletion/TryCancelOperation).
    // Not required by any accepted deliverable; recorded as a known limitation in Planning~/Evidence/P7-008/.
    public readonly ref struct ReferenceLeafContext
    {
        private readonly ReferenceNodeContext _inner;

        internal ReferenceLeafContext(ReferenceNodeContext inner)
        {
            _inner = inner;
        }

        public ReadOnlySpan<byte> Configuration => _inner.Configuration;

        public Span<byte> Memory => _inner.Memory;

        public bool TryReadBlackboard(uint declaredReadOrdinal, out BlackboardValue value)
            => _inner.TryReadBlackboard(declaredReadOrdinal, out value);

        public bool TryWriteBlackboard(uint declaredWriteOrdinal, BlackboardValue value)
            => _inner.TryWriteBlackboard(declaredWriteOrdinal, value);
    }

    // Internal adapter: drops a project-supplied public IReferenceLeafBehavior into the existing
    // internal ReferenceLeafBinding/ReferenceLeafRegistry machinery unchanged.
    internal sealed class ProjectReferenceLeafHandlerAdapter : IReferenceLeafHandler
    {
        private readonly IReferenceLeafBehavior _behavior;

        internal ProjectReferenceLeafHandlerAdapter(IReferenceLeafBehavior behavior)
        {
            _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        }

        public void Enter(ref ReferenceNodeContext context)
        {
            var publicContext = new ReferenceLeafContext(context);
            _behavior.Enter(ref publicContext);
        }

        public NodeStatus Tick(ref ReferenceNodeContext context)
        {
            var publicContext = new ReferenceLeafContext(context);
            return _behavior.Tick(ref publicContext);
        }

        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason)
        {
            var publicContext = new ReferenceLeafContext(context);
            _behavior.Abort(ref publicContext, reason);
        }

        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason)
        {
            var publicContext = new ReferenceLeafContext(context);
            _behavior.Exit(ref publicContext, reason);
        }
    }
}
