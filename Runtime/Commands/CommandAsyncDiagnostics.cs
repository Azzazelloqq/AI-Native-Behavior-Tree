namespace AIBT
{
    internal static class CommandAsyncDiagnosticCodes
    {
        internal static readonly DiagnosticCode DuplicateCompletionOrderingKey = new DiagnosticCode("AIBT4101");
        internal static readonly DiagnosticCode NonIncreasingSourceSequence = new DiagnosticCode("AIBT4102");
        internal static readonly DiagnosticCode UnknownOperation = new DiagnosticCode("AIBT4103");
        internal static readonly DiagnosticCode StaleOperationGeneration = new DiagnosticCode("AIBT4104");
        internal static readonly DiagnosticCode CancelledOperation = new DiagnosticCode("AIBT4105");
        internal static readonly DiagnosticCode AlreadyConsumedOperation = new DiagnosticCode("AIBT4106");
        internal static readonly DiagnosticCode OperationSequenceOverflow = new DiagnosticCode("AIBT4107");
        internal static readonly DiagnosticCode InvalidCommand = new DiagnosticCode("AIBT4108");
        internal static readonly DiagnosticCode CommandSequenceOverflow = new DiagnosticCode("AIBT4109");
        internal static readonly DiagnosticCode CompletionPayloadMismatch = new DiagnosticCode("AIBT4110");
    }

    internal static class CommandAsyncDiagnostics
    {
        private static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(CommandAsyncDiagnosticCodes.DuplicateCompletionOrderingKey, DiagnosticSeverity.Error),
            Descriptor(CommandAsyncDiagnosticCodes.NonIncreasingSourceSequence, DiagnosticSeverity.Error),
            Descriptor(CommandAsyncDiagnosticCodes.UnknownOperation, DiagnosticSeverity.Warning),
            Descriptor(CommandAsyncDiagnosticCodes.StaleOperationGeneration, DiagnosticSeverity.Info),
            Descriptor(CommandAsyncDiagnosticCodes.CancelledOperation, DiagnosticSeverity.Info),
            Descriptor(CommandAsyncDiagnosticCodes.AlreadyConsumedOperation, DiagnosticSeverity.Info),
            Descriptor(CommandAsyncDiagnosticCodes.OperationSequenceOverflow, DiagnosticSeverity.Error),
            Descriptor(CommandAsyncDiagnosticCodes.InvalidCommand, DiagnosticSeverity.Error),
            Descriptor(CommandAsyncDiagnosticCodes.CommandSequenceOverflow, DiagnosticSeverity.Error),
            Descriptor(CommandAsyncDiagnosticCodes.CompletionPayloadMismatch, DiagnosticSeverity.Error),
        });

        internal static Diagnostic Create(DiagnosticCode code, string message, TreeInstanceId treeInstanceId)
        {
            return Catalog.Create(
                code,
                message,
                new DiagnosticLocation(treeInstanceId: treeInstanceId));
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code, DiagnosticSeverity severity)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.Execution,
                severity,
                requiredFields: DiagnosticField.TreeInstanceId);
        }
    }
}
