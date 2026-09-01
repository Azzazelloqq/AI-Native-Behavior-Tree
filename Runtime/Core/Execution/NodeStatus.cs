namespace AIBT
{
    public enum NodeStatus : byte
    {
        Success,
        Failure,
        Running
    }

    internal enum NodeExecutionState : byte
    {
        Inactive,
        Running,
        BudgetYielded,
        Success,
        Failure
    }

    // Public: the new project-facing IReferenceLeafBehavior contract (P7-008) surfaces this as an
    // Exit reason parameter; internal reference-executor code uses it identically to before.
    public enum NodeExitReason : byte
    {
        Success,
        Failure,
        Aborted
    }

    // Public: the new project-facing IReferenceLeafBehavior contract (P7-008) surfaces this as an
    // Abort reason parameter; internal reference-executor code uses it identically to before.
    public enum NodeAbortReason : byte
    {
        Explicit,
        ObserverSelf,
        ObserverLowerPriority,
        TreeStopped,
        HotReload,
        Timeout
    }
}
