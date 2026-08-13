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

    internal enum NodeExitReason : byte
    {
        Success,
        Failure,
        Aborted
    }

    internal enum NodeAbortReason : byte
    {
        Explicit,
        ObserverSelf,
        ObserverLowerPriority,
        TreeStopped,
        HotReload,
        Timeout
    }
}
