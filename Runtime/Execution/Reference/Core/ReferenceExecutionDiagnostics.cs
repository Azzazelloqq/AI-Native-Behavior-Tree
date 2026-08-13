namespace AIBT
{
    internal static class ReferenceExecutionDiagnosticCodes
    {
        internal static readonly DiagnosticCode InvalidOperation = new DiagnosticCode("AIBT4001");
        internal static readonly DiagnosticCode MissingHandler = new DiagnosticCode("AIBT4002");
        internal static readonly DiagnosticCode HandlerFault = new DiagnosticCode("AIBT4003");
        internal static readonly DiagnosticCode ActivationGenerationOverflow = new DiagnosticCode("AIBT4004");
        internal static readonly DiagnosticCode UnsupportedNode = new DiagnosticCode("AIBT4005");
        internal static readonly DiagnosticCode InvalidCompositeState = new DiagnosticCode("AIBT4006");
        internal static readonly DiagnosticCode InvalidNodeConfiguration = new DiagnosticCode("AIBT4007");
        internal static readonly DiagnosticCode TimeOverflow = new DiagnosticCode("AIBT4008");
    }

    internal static class ReferenceExecutionDiagnostics
    {
        private static readonly DiagnosticCatalog Catalog = new DiagnosticCatalog(new[]
        {
            Descriptor(ReferenceExecutionDiagnosticCodes.InvalidOperation),
            Descriptor(ReferenceExecutionDiagnosticCodes.MissingHandler),
            Descriptor(ReferenceExecutionDiagnosticCodes.HandlerFault),
            Descriptor(ReferenceExecutionDiagnosticCodes.ActivationGenerationOverflow),
            Descriptor(ReferenceExecutionDiagnosticCodes.UnsupportedNode),
            Descriptor(ReferenceExecutionDiagnosticCodes.InvalidCompositeState),
            Descriptor(ReferenceExecutionDiagnosticCodes.InvalidNodeConfiguration),
            Descriptor(ReferenceExecutionDiagnosticCodes.TimeOverflow),
        });

        internal static Diagnostic Create(
            DiagnosticCode code,
            string message,
            TreeInstanceId instanceId,
            RuntimeNodeIndex nodeIndex = default)
        {
            var nodeId = default(NodeId);
            return Catalog.Create(
                code,
                message,
                new DiagnosticLocation(
                    nodeId: nodeId,
                    treeInstanceId: instanceId));
        }

        private static DiagnosticDescriptor Descriptor(DiagnosticCode code)
        {
            return new DiagnosticDescriptor(
                code,
                DiagnosticSubsystem.Execution,
                DiagnosticSeverity.Error,
                requiredFields: DiagnosticField.TreeInstanceId);
        }
    }
}
