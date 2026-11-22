using System;
using System.Collections.Generic;

namespace AIBT.Authoring
{
    public static class TreeValidationDiagnosticCodes
    {
        public static readonly DiagnosticCode InvalidDocument = new DiagnosticCode("AIBT2010");
        public static readonly DiagnosticCode InvalidFormat = new DiagnosticCode("AIBT2011");
        public static readonly DiagnosticCode UnsupportedFormatVersion = new DiagnosticCode("AIBT2012");
        public static readonly DiagnosticCode InvalidTreeIdentity = new DiagnosticCode("AIBT2013");
        public static readonly DiagnosticCode MissingTreeName = new DiagnosticCode("AIBT2014");
        public static readonly DiagnosticCode InvalidRoot = new DiagnosticCode("AIBT2015");
        public static readonly DiagnosticCode InvalidNodeIdentity = new DiagnosticCode("AIBT2016");
        public static readonly DiagnosticCode DuplicateNodeIdentity = new DiagnosticCode("AIBT2017");
        public static readonly DiagnosticCode MissingChild = new DiagnosticCode("AIBT2018");
        public static readonly DiagnosticCode Cycle = new DiagnosticCode("AIBT2019");
        public static readonly DiagnosticCode UnreachableNode = new DiagnosticCode("AIBT2020");
        public static readonly DiagnosticCode UnknownNodeType = new DiagnosticCode("AIBT2021");
        public static readonly DiagnosticCode UnsupportedNodeVersion = new DiagnosticCode("AIBT2022");
        public static readonly DiagnosticCode ChildPolicy = new DiagnosticCode("AIBT2023");
        public static readonly DiagnosticCode DuplicateChild = new DiagnosticCode("AIBT2024");
        public static readonly DiagnosticCode UnknownParameter = new DiagnosticCode("AIBT2025");
        public static readonly DiagnosticCode MissingParameter = new DiagnosticCode("AIBT2026");
        public static readonly DiagnosticCode ParameterType = new DiagnosticCode("AIBT2027");
        public static readonly DiagnosticCode ParameterConstraint = new DiagnosticCode("AIBT2028");
        public static readonly DiagnosticCode DuplicateParameter = new DiagnosticCode("AIBT2029");
        public static readonly DiagnosticCode UnsupportedBlackboardScope = new DiagnosticCode("AIBT2030");
        public static readonly DiagnosticCode MissingBlackboardAccess = new DiagnosticCode("AIBT2031");
        public static readonly DiagnosticCode UnsupportedBlackboardWrite = new DiagnosticCode("AIBT2032");
        public static readonly DiagnosticCode InvalidObserverMode = new DiagnosticCode("AIBT2033");
        public static readonly DiagnosticCode InvalidObserverContext = new DiagnosticCode("AIBT2034");
        public static readonly DiagnosticCode InvalidWatchedKey = new DiagnosticCode("AIBT2035");
        public static readonly DiagnosticCode DuplicateWatchedKey = new DiagnosticCode("AIBT2036");
        public static readonly DiagnosticCode ParallelThreshold = new DiagnosticCode("AIBT2037");
        public static readonly DiagnosticCode PolicyViolation = new DiagnosticCode("AIBT2038");
        public static readonly DiagnosticCode UnsupportedExecutionDomain = new DiagnosticCode("AIBT2039");
        public static readonly DiagnosticCode UnsupportedNodeCapability = new DiagnosticCode("AIBT2040");
        public static readonly DiagnosticCode MultipleParents = new DiagnosticCode("AIBT2041");

        /// <summary>
        /// P7-006 (ADR-P7-005): a node's authored parameters were rewritten in memory by a
        /// registered migration rule chain to reach the currently-registered manifest version.
        /// Info severity -- the document already compiles/validates successfully; this is a
        /// heads-up for review, never a blocking failure.
        /// </summary>
        public static readonly DiagnosticCode MigrationApplied = new DiagnosticCode("AIBT2042");

        /// <summary>
        /// P7-039 (ADR-P7-039): a declared Tree-scope blackboard key has zero Write accesses across
        /// the whole tree, making it eligible for a native <c>ProductionTreeHost</c> external write
        /// (a compiled node never claims it). Info severity -- the document already compiles and
        /// validates successfully; this is a review heads-up, never a blocking failure.
        /// </summary>
        public static readonly DiagnosticCode TreeScopeSlotNeverWritten = new DiagnosticCode("AIBT2043");

        /// <summary>
        /// P11-001 (ADR-P11-001 §4.6): a node with <c>subtree</c> also has a type, type version,
        /// parameters, bindings, observer or children; its ID contains ':'; its tree ID is invalid;
        /// its interface version is below 1; or the document is not format version 3.
        /// </summary>
        public static readonly DiagnosticCode SubtreeReferenceShape = new DiagnosticCode("AIBT2047");

        /// <summary>P11-001: a string entry of <c>subtree.keys</c> does not name a declared Tree-scope key of the parent.</summary>
        public static readonly DiagnosticCode SubtreeKeyTarget = new DiagnosticCode("AIBT2048");

        /// <summary>P11-001: the tree root is a subtree reference.</summary>
        public static readonly DiagnosticCode SubtreeReferenceRoot = new DiagnosticCode("AIBT2049");

        /// <summary>
        /// P11-001: an <c>interface</c> key is not a declared Tree-scope key or is listed twice, the
        /// interface version is below 1, or the document is not format version 3.
        /// </summary>
        public static readonly DiagnosticCode SubtreeInterface = new DiagnosticCode("AIBT2050");

        /// <summary>
        /// P11-008 (review F10): a node has an <c>observer</c> block in a tree run by a
        /// <c>BehaviorTreeAgent</c>. The managed agent does not run observer re-evaluation, so the tree
        /// is refused when it is loaded instead of stopping the agent with AIBT4002 when a watched key
        /// changes. Raised by the managed layer's load and by the Editor's managed-policy validation.
        /// </summary>
        public static readonly DiagnosticCode ObserverNotImplementedInManagedAgent = new DiagnosticCode("AIBT2051");
    }

    public static class TreeValidationDiagnosticCatalog
    {
        private const DiagnosticField RequiredFields = DiagnosticField.JsonPointer;
        private const DiagnosticField OptionalFields = DiagnosticField.DocumentId
            | DiagnosticField.TreeId
            | DiagnosticField.NodeId
            | DiagnosticField.RelatedLocations;

        private static readonly DiagnosticCode[] Codes =
        {
            TreeValidationDiagnosticCodes.InvalidDocument,
            TreeValidationDiagnosticCodes.InvalidFormat,
            TreeValidationDiagnosticCodes.UnsupportedFormatVersion,
            TreeValidationDiagnosticCodes.InvalidTreeIdentity,
            TreeValidationDiagnosticCodes.MissingTreeName,
            TreeValidationDiagnosticCodes.InvalidRoot,
            TreeValidationDiagnosticCodes.InvalidNodeIdentity,
            TreeValidationDiagnosticCodes.DuplicateNodeIdentity,
            TreeValidationDiagnosticCodes.MissingChild,
            TreeValidationDiagnosticCodes.Cycle,
            TreeValidationDiagnosticCodes.UnreachableNode,
            TreeValidationDiagnosticCodes.UnknownNodeType,
            TreeValidationDiagnosticCodes.UnsupportedNodeVersion,
            TreeValidationDiagnosticCodes.ChildPolicy,
            TreeValidationDiagnosticCodes.DuplicateChild,
            TreeValidationDiagnosticCodes.UnknownParameter,
            TreeValidationDiagnosticCodes.MissingParameter,
            TreeValidationDiagnosticCodes.ParameterType,
            TreeValidationDiagnosticCodes.ParameterConstraint,
            TreeValidationDiagnosticCodes.DuplicateParameter,
            TreeValidationDiagnosticCodes.UnsupportedBlackboardScope,
            TreeValidationDiagnosticCodes.MissingBlackboardAccess,
            TreeValidationDiagnosticCodes.UnsupportedBlackboardWrite,
            TreeValidationDiagnosticCodes.InvalidObserverMode,
            TreeValidationDiagnosticCodes.InvalidObserverContext,
            TreeValidationDiagnosticCodes.InvalidWatchedKey,
            TreeValidationDiagnosticCodes.DuplicateWatchedKey,
            TreeValidationDiagnosticCodes.ParallelThreshold,
            TreeValidationDiagnosticCodes.PolicyViolation,
            TreeValidationDiagnosticCodes.UnsupportedExecutionDomain,
            TreeValidationDiagnosticCodes.UnsupportedNodeCapability,
            TreeValidationDiagnosticCodes.MultipleParents,
            TreeValidationDiagnosticCodes.MigrationApplied,
            TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten,
            TreeValidationDiagnosticCodes.SubtreeReferenceShape,
            TreeValidationDiagnosticCodes.SubtreeKeyTarget,
            TreeValidationDiagnosticCodes.SubtreeReferenceRoot,
            TreeValidationDiagnosticCodes.SubtreeInterface,
            TreeValidationDiagnosticCodes.ObserverNotImplementedInManagedAgent,
        };

        public static DiagnosticCatalog Catalog { get; } = CreateCatalog();

        internal static Diagnostic Create(
            DiagnosticCode code,
            string message,
            DiagnosticLocation location,
            IEnumerable<DiagnosticLocation> relatedLocations = null,
            DiagnosticSeverity? severity = null)
        {
            if (!Catalog.TryGet(code, out var descriptor))
            {
                throw new ArgumentException("The code is not registered in the tree validation catalog.", nameof(code));
            }

            return new Diagnostic(code, severity ?? descriptor.DefaultSeverity, message, location, relatedLocations);
        }

        private static DiagnosticCatalog CreateCatalog()
        {
            var descriptors = new DiagnosticDescriptor[Codes.Length];
            for (var index = 0; index < Codes.Length; index++)
            {
                // Every code defaults to Error except MigrationApplied and TreeScopeSlotNeverWritten:
                // both describe a document that already compiles/validates successfully, so neither
                // is a blocking failure -- only Info, a review heads-up (ADR-P7-005/ADR-P7-039).
                var defaultSeverity = Codes[index] == TreeValidationDiagnosticCodes.MigrationApplied
                    || Codes[index] == TreeValidationDiagnosticCodes.TreeScopeSlotNeverWritten
                    ? DiagnosticSeverity.Info
                    : DiagnosticSeverity.Error;
                descriptors[index] = new DiagnosticDescriptor(
                    Codes[index],
                    DiagnosticSubsystem.SemanticValidation,
                    defaultSeverity,
                    RequiredFields,
                    OptionalFields);
            }

            return new DiagnosticCatalog(descriptors);
        }
    }
}
